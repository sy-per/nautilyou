using NautilyouShared;

namespace NautilyouService;

// Boucle principale du service (enforcement pur, aucune UI — voir dev-context pour pourquoi le
// systray a été déplacé dans un processus séparé, NautilyouCompanion, relié ici par PipeServer).
// Synchronisation en temps réel via un WebSocket persistant avec le serveur (config poussée
// instantanément, activité envoyée au fil de l'eau) plutôt qu'un DataChannel WebRTC P2P : plus
// simple à mettre en oeuvre et tout aussi réactif, puisque le serveur distant est celui du parent
// lui-même, pas un tiers à contourner.
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly DnsFilterServer _dnsFilter;
    private readonly BlockedPageServer _blockedPageServer;
    private readonly PipeServer _pipeServer;
    private bool _dnsAdapterConfigured;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    private readonly LocalState _state = LocalState.Load();
    private DeviceDetail? _latestDevice;
    private DateOnly _trackedDate = DateOnly.FromDateTime(DateTime.Now);
    private DateTime _lastTick = DateTime.UtcNow;

    // Remonté par le Companion via le pipe (SystemEvents.SessionSwitch, qui a besoin d'une boucle
    // de messages Windows que seul le Companion a). Par défaut false (actif) : si le Companion
    // n'est pas encore connecté ou a été tué, on continue de compter le temps plutôt que d'offrir
    // du temps gratuit illimité à un enfant qui saurait fermer l'appli compagnon.
    private bool _companionReportsLocked;

    private int UsedMinutesToday => _state.UsedSecondsToday / 60;
    private Dictionary<string, int> AppMinutesToday() =>
        _state.AppSecondsToday.ToDictionary(kv => kv.Key, kv => kv.Value / 60);

    private string _deviceId = "";

    // Filtre enfant : listes publiques telechargees (BlocklistStore) et appliquees au filtre DNS.
    private readonly BlocklistStore _blocklists;
    private int _childFilterVersion;
    private DateTime _lastChildFilterSync = DateTime.MinValue;

    // Inventaire des applications installees (voir InstalledAppsScanner) : envoye au serveur pour que le
    // dashboard propose une liste a choisir, et utilise localement pour relier un nom d'app a ses process.
    private List<InstalledApp> _installedApps = new();
    private IReadOnlyDictionary<string, string[]> _appInventory = new Dictionary<string, string[]>();
    private bool _appsSentThisConnection;
    private DateTime _lastAppsScan = DateTime.MinValue;
    private DeviceSocketClient? _socket;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _dnsFilter = new DnsFilterServer(logger);
        _blockedPageServer = new BlockedPageServer(logger, OnAccessRequestedAsync);
        _pipeServer = new PipeServer(logger);
        _blocklists = new BlocklistStore(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RestoreLocalState();

        // Le pipe demarre AVANT le pairing : c'est via lui (message "pair-request" depuis le
        // Companion) qu'un vrai service sans console peut maintenant etre appaire, au lieu de
        // dependre exclusivement du prompt console `dotnet run` (voir dev-context, section
        // "Interface de pairing dans le Companion").
        _pipeServer.SessionLockChanged += locked => _companionReportsLocked = locked;
        _pipeServer.Start(stoppingToken);

        var pairing = await EnsurePairedAsync(stoppingToken);
        _deviceId = pairing.DeviceId;
        var tls = new TlsPinning(pairing.CertSha256, allowFirstUse: false);
        var (rsa, _) = DeviceKeyStore.LoadOrCreate();

        _logger.LogInformation("Nautilyou service demarre - appareil {DeviceId} sur {ServerUrl}", pairing.DeviceId, pairing.ServerUrl);
        if (_latestDevice is not null)
        {
            _logger.LogInformation("Derniere config connue rechargee depuis le cache local (mode hors-ligne) en attendant la connexion");
            _dnsFilter.UpdateConfig(_latestDevice.Config.Web);
            _ = ApplyChildFilterAsync(_latestDevice.Config.Web.ChildFilter, stoppingToken);
        }

        _dnsFilter.Start(stoppingToken);
        // Variable d'environnement de diagnostic (ajoutee le 2026-09-23 apres plusieurs pannes
        // reelles) : NAUTILYOU_SKIP_DNS_ADAPTER=1 laisse le filtre tourner et ecouter sur le port
        // 53 normalement, mais NE TOUCHE JAMAIS au DNS reel des interfaces reseau. Permet de
        // diagnostiquer le filtre (via `nslookup domaine 127.0.0.1`) sans aucun risque de couper
        // l'acces internet pendant les tests.
        var skipAdapter = Environment.GetEnvironmentVariable("NAUTILYOU_SKIP_DNS_ADAPTER") == "1";
        if (skipAdapter)
        {
            _logger.LogWarning("NAUTILYOU_SKIP_DNS_ADAPTER=1 : le filtre DNS ecoute sur le port 53 mais le DNS systeme n'est PAS reconfigure (mode diagnostic)");
        }
        else if (_dnsFilter.IsRunning)
        {
            _dnsAdapterConfigured = DnsAdapterConfigurator.PointAtLocalFilter(_logger);
            if (!_dnsAdapterConfigured)
            {
                _logger.LogWarning("Filtre DNS demarre mais le DNS systeme n'a pas pu etre reconfigure (droits admin requis) - le filtrage ne s'applique pas reellement");
            }
            else
            {
                // Garde-fou (voir dev-context, incidents DNS du 2026-09-23) : si le filtre local
                // cesse de repondre reellement (process bloque, port libere, etc.) alors que le
                // DNS systeme pointe toujours vers lui, restaure automatiquement le DHCP au lieu
                // de laisser le poste sans acces internet jusqu'a une intervention manuelle.
                var watchdog = new DnsWatchdog(_logger);
                _ = watchdog.RunAsync(
                    () => _dnsAdapterConfigured,
                    () =>
                    {
                        DnsAdapterConfigurator.RestoreDhcp(_logger);
                        _dnsAdapterConfigured = false;
                    },
                    stoppingToken);
            }
        }
        _blockedPageServer.Start(stoppingToken);

        var socket = new DeviceSocketClient(pairing.ServerUrl, pairing.DeviceId, rsa, _logger, tls);
        _socket = socket;
        socket.ConfigReceived += device =>
        {
            _latestDevice = device;
            _dnsFilter.UpdateConfig(device.Config.Web);
            _ = ApplyChildFilterAsync(device.Config.Web.ChildFilter, stoppingToken);
            _state.CachedConfig = device.Config;
            _state.CachedBonusMinutesToday = device.BonusMinutesToday;
            _state.Save();
        };
        socket.CommandReceived += (type, payload) =>
        {
            if (type == "lock")
            {
                _logger.LogInformation("Commande de verrouillage recue en direct -> verrouillage immediat");
                SessionLock.Lock();
            }
            else if (type == "request-resolved")
            {
                HandleRequestResolved(payload);
            }
        };

        _dnsFilter.SiteBlocked += domain => _ = _pipeServer.BroadcastBlockedSiteAsync(domain);
        _pipeServer.AccessRequested += domain => _ = OnAccessRequestedAsync(domain);

        _pipeServer.TimeRequested += minutes =>
        {
            _ = socket.SendRequestAsync("time", new { minutes });
            _logger.LogInformation("Demande de {Minutes}min de temps supplementaire envoyee", minutes);
        };

        var socketTask = socket.RunAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(socket);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Erreur pendant le cycle local : {Message}", ex.Message);
                }

                await Task.Delay(TickInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // arret normal du service
        }
        finally
        {
            _dnsFilter.Stop();
            _blockedPageServer.Stop();
            if (_dnsAdapterConfigured)
            {
                DnsAdapterConfigurator.RestoreDhcp(_logger);
            }
        }

        await socketTask;
    }

    private async Task OnAccessRequestedAsync(string domain)
    {
        if (_socket is not null && _socket.IsConnected)
        {
            await _socket.SendRequestAsync("site", new { domain });
            _logger.LogInformation("Demande d'acces a {Domain} envoyee", domain);
        }
    }

    private void HandleRequestResolved(System.Text.Json.JsonElement payload)
    {
        var status = payload.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (status == "approved")
        {
            _ = _pipeServer.BroadcastBalloonAsync(new BalloonMessage { Title = "Demande approuvée", Text = "Un parent a approuvé ta demande." });
        }
        else if (status == "denied")
        {
            _ = _pipeServer.BroadcastBalloonAsync(new BalloonMessage { Title = "Demande refusée", Text = "Un parent a refusé ta demande." });
        }
    }

    private void RestoreLocalState()
    {
        var todayStr = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        if (_state.TrackedDate != todayStr)
        {
            _state.TrackedDate = todayStr;
            _state.UsedSecondsToday = 0;
            _state.AppSecondsToday = new Dictionary<string, int>();
            _state.SiteMinutesToday = new Dictionary<string, int>();
        }
        _dnsFilter.LoadCounts(_state.SiteMinutesToday);

        if (_state.CachedConfig is not null)
        {
            _latestDevice = new DeviceDetail
            {
                Config = _state.CachedConfig,
                BonusMinutesToday = _state.CachedBonusMinutesToday,
            };
        }
    }

    private async Task TickAsync(DeviceSocketClient socket)
    {
        AccumulateUsedTime();
        var usedMinutesToday = UsedMinutesToday;

        // Pas encore reçu de config (ni en cache, ni du serveur) : rien à appliquer, mais on ne
        // bloque rien non plus (fail-safe = ne pas pénaliser avant d'avoir un premier état connu).
        if (_latestDevice is null)
        {
            await _pipeServer.BroadcastStatusAsync(new StatusMessage
            {
                RemainingMinutes = 0,
                QuotaEnabled = true,
                AppMinutes = AppMinutesToday(),
                ConnectedToServer = socket.IsConnected,
            });
            return;
        }

        var device = _latestDevice;

        await RefreshInstalledAppsAsync(socket);

        // Rafraichit les listes du filtre enfant au moins une fois par jour (le magasin ne retelecharge
        // qu'une liste vieille de plus d'une semaine).
        if (DateTime.UtcNow - _lastChildFilterSync > TimeSpan.FromHours(24))
        {
            _ = ApplyChildFilterAsync(device.Config.Web.ChildFilter, CancellationToken.None);
        }
        AppLimitEnforcer.Enforce(device.Config.Apps, _state.AppSecondsToday, _appInventory, _logger);

        var quota = device.Config.Time.DailyQuotaMinutes + device.BonusMinutesToday;
        var remaining = quota - usedMinutesToday;
        var nowHHmm = DateTime.Now.ToString("HH:mm");
        var withinWindow = string.Compare(device.Config.Time.WindowStart, nowHHmm, StringComparison.Ordinal) <= 0
                            && string.Compare(nowHHmm, device.Config.Time.WindowEnd, StringComparison.Ordinal) <= 0;

        var quotaOk = !device.Config.Time.QuotaEnabled || remaining > 0;
        var windowOk = !device.Config.Time.WindowEnabled || withinWindow;
        var ok = quotaOk && windowOk;

        await _pipeServer.BroadcastStatusAsync(new StatusMessage
        {
            RemainingMinutes = device.Config.Time.QuotaEnabled ? remaining : 0,
            QuotaEnabled = device.Config.Time.QuotaEnabled,
            AppMinutes = AppMinutesToday(),
            ConnectedToServer = socket.IsConnected,
        });

        _logger.LogInformation(
            "Temps utilise: {Used}min / quota {Quota}min (bonus inclus) - fenetre {Start}-{End} - autorise: {Ok} - ws connecte: {Connected}",
            usedMinutesToday, quota, device.Config.Time.WindowStart, device.Config.Time.WindowEnd, ok, socket.IsConnected);

        // Hors connexion WebSocket l'activite n'est pas envoyee : le compteur local continue et sera
        // renvoye des la reconnexion (l'ancien repli HTTP non authentifie a ete supprime).
        if (socket.IsConnected)
        {
            await socket.SendActivityAsync(usedMinutesToday, _dnsFilter.GetTopSites(8));
        }

        if (!ok)
        {
            _logger.LogInformation("Quota ou plage horaire depasse -> verrouillage de la session");
            SessionLock.Lock();
        }
    }

    // Charge les listes choisies pour le filtre enfant (telechargement au besoin) puis les applique au
    // filtre DNS. Une configuration plus recente lancee pendant le telechargement l'emporte.
    private async Task ApplyChildFilterAsync(ChildFilterConfig config, CancellationToken ct)
    {
        var version = Interlocked.Increment(ref _childFilterVersion);
        _lastChildFilterSync = DateTime.UtcNow;
        try
        {
            var ids = config.Enabled ? config.Lists.Distinct().ToList() : new List<string>();
            var lists = new List<ulong[]>();
            foreach (var id in ids)
            {
                var source = BlocklistCatalog.Find(id);
                if (source is null) continue; // "cloudflare-family" : pas de liste, seulement le DNS amont
                var hashes = await _blocklists.EnsureAsync(source, ct);
                if (hashes is not null) lists.Add(hashes);
            }

            if (version != _childFilterVersion) return;
            _dnsFilter.SetChildFilter(config.Enabled, lists.ToArray(), ids.Contains(BlocklistCatalog.CloudflareFamilyId));
            _logger.LogInformation("Filtre enfant : {State}, {Count} liste(s) active(s)", config.Enabled ? "actif" : "desactive", lists.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Filtre enfant : application impossible ({Message})", ex.Message);
        }
    }

    // Rescanne toutes les 6 h (une installation ou desinstallation apparait donc dans le dashboard au plus
    // tard 6 h plus tard) et renvoie la liste a chaque nouvelle connexion WebSocket.
    private async Task RefreshInstalledAppsAsync(DeviceSocketClient socket)
    {
        if (DateTime.UtcNow - _lastAppsScan > TimeSpan.FromHours(6))
        {
            _lastAppsScan = DateTime.UtcNow;
            _installedApps = await Task.Run(InstalledAppsScanner.Scan);
            _appInventory = _installedApps.ToDictionary(a => a.Name, a => a.Exes.ToArray(), StringComparer.OrdinalIgnoreCase);
            _appsSentThisConnection = false;
            _logger.LogInformation("Inventaire des applications : {Count} applications detectees", _installedApps.Count);
        }

        if (!socket.IsConnected)
        {
            _appsSentThisConnection = false;
        }
        else if (!_appsSentThisConnection)
        {
            await socket.SendInstalledAppsAsync(_installedApps);
            _appsSentThisConnection = true;
        }
    }

    private void AccumulateUsedTime()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _trackedDate)
        {
            _trackedDate = today;
            _state.UsedSecondsToday = 0;
            _state.AppSecondsToday = new Dictionary<string, int>();
            _dnsFilter.ResetDailyCounters();
            _state.SiteMinutesToday = new Dictionary<string, int>();
        }

        var now = DateTime.UtcNow;
        var elapsed = now - _lastTick;
        _lastTick = now;

        // Actif tant que le Companion ne signale pas la session verrouillée — pas basé sur
        // l'activité clavier/souris, qui compterait à tort comme "absent" un enfant en train de
        // regarder une vidéo sans bouger la souris pendant plusieurs minutes.
        if (!_companionReportsLocked)
        {
            var elapsedSeconds = (int)Math.Round(elapsed.TotalSeconds);
            _state.UsedSecondsToday += elapsedSeconds;

            var appName = ForegroundAppTracker.GetForegroundProcessName();
            if (appName is not null && elapsedSeconds > 0)
            {
                _state.AppSecondsToday[appName] = _state.AppSecondsToday.GetValueOrDefault(appName) + elapsedSeconds;
            }
        }

        _state.SiteMinutesToday = _dnsFilter.ExportCounts();
        _state.TrackedDate = _trackedDate.ToString("yyyy-MM-dd");
        _state.Save();
    }

    // Chemin principal : le Companion (systray) affiche un formulaire de pairing tant qu'aucun
    // pairing n'existe (signal "pairing-needed" diffuse ci-dessous), et envoie {serverUrl, code}
    // via le pipe local ("pair-request", geré par PipeServer -> PairingService.TryPairAsync qui
    // sauvegarde PairingState). Cette boucle attend juste que ce fichier apparaisse.
    //
    // En mode interactif (`dotnet run`, dev/debug uniquement) un prompt console reste disponible
    // en secours, pratique pour tester sans lancer le Companion. Les deux chemins peuvent
    // coexister : le premier qui reussit gagne.
    private async Task<PairingState> EnsurePairedAsync(CancellationToken stoppingToken)
    {
        var existing = PairingState.Load();
        if (existing is not null) return existing;

        if (!Environment.UserInteractive)
        {
            _logger.LogWarning("Aucun pairing existant - en attente d'un appairage depuis l'appli compagnon (systray)...");
            while (!stoppingToken.IsCancellationRequested)
            {
                await _pipeServer.BroadcastPairingNeededAsync();
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                var retry = PairingState.Load();
                if (retry is not null) return retry;
            }
            stoppingToken.ThrowIfCancellationRequested();
        }

        // Boucle jusqu'a un pairing reussi (via le prompt console OU via le pipe pendant qu'on
        // attend une saisie) : une saisie invalide (URL mal formee, code expire, serveur
        // injoignable...) redemande au lieu de planter tout le service (bug corrige le 2026-09-23
        // : une URL sans schema http(s):// faisait lever une UriFormatException non rattrapee,
        // qui arretait le BackgroundService et donc tout le service Windows).
        while (true)
        {
            // Le pairing peut avoir reussi entre-temps via le Companion (pipe) pendant qu'on
            // attendait une saisie console lors du tour precedent - sans ce check, le Worker
            // restait bloque indefiniment sur Console.ReadLine() meme apres un appairage reussi
            // par ailleurs, et ne demarrait donc jamais la boucle qui diffuse les "status" (bug
            // trouve en testant le 2026-09-23 : le Companion affichait "appairage reussi" mais ne
            // basculait jamais sur l'ecran de statut).
            var alreadyPaired = PairingState.Load();
            if (alreadyPaired is not null) return alreadyPaired;

            Console.WriteLine("=== Appairage Nautilyou (ou utilise le Companion) ===");
            Console.Write("URL du serveur (ex: http://192.168.1.10:4100) : ");
            var serverUrl = await ReadLineWithPairingBroadcastAsync(stoppingToken);
            if (serverUrl is null) continue; // pairing reussi via le pipe pendant l'attente

            Console.Write("Code d'appairage (6 chiffres, affiche dans le dashboard) : ");
            var code = await ReadLineWithPairingBroadcastAsync(stoppingToken);
            if (code is null) continue;

            var (success, message) = await PairingService.TryPairAsync(serverUrl, code);
            Console.WriteLine(message);

            var state = PairingState.Load();
            if (success && state is not null) return state;
        }
    }

    // Console.ReadLine() bloque le thread indefiniment : sans ca, un Companion qui se connecte
    // apres l'unique diffusion "pairing-needed" (au debut de la boucle) ne la recevrait jamais et
    // resterait bloque sur son ecran de chargement (bug trouve en testant le 2026-09-23). On lit
    // la ligne sur un thread separe et on continue de rediffuser toutes les 2s pendant l'attente,
    // en verifiant aussi si un pairing via le pipe a reussi entre-temps (retourne alors null pour
    // signaler a l'appelant d'abandonner la saisie console en cours).
    private async Task<string?> ReadLineWithPairingBroadcastAsync(CancellationToken ct)
    {
        var readTask = Task.Run(() => Console.ReadLine() ?? "", ct);
        while (!readTask.IsCompleted)
        {
            if (PairingState.Load() is not null) return null;
            await _pipeServer.BroadcastPairingNeededAsync();
            await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(2), ct));
        }
        return (await readTask).Trim();
    }
}
