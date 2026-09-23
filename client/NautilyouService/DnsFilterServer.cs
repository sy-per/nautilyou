using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Linq;

using NautilyouShared;

namespace NautilyouService;

// Filtrage de sites par DNS local : remplace le blocage via hosts file, qui ne peut techniquement
// pas implémenter le mode liste blanche ("tout bloqué sauf X" — un hosts file ne peut qu'ajouter
// des redirections ponctuelles, pas changer le comportement par défaut). Un petit résolveur DNS
// tourne en local (127.0.0.1:53), le poste est configuré pour l'utiliser comme DNS
// (DnsAdapterConfigurator), et chaque requête est autorisée (relayée telle quelle vers un DNS
// public en amont) ou bloquée selon la config WEB courante.
//
// Un site bloqué n'est pas résolu en NXDOMAIN mais renvoyé vers une IP locale (127.0.0.2,
// SinkholeIp) où BlockedPageServer sert une page "site bloqué" avec un bouton de demande d'accès
// — ça ne marche que pour les requêtes A (IPv4) ; les autres types de requête restent en NXDOMAIN.
// Limitation connue : ça ne fonctionne visuellement que pour le trafic HTTP en clair, la plupart
// des sites modernes forcent HTTPS et le navigateur affichera un avertissement de sécurité avant
// que la page ne puisse s'afficher (pas de certificat de confiance pour le domaine bloqué), voir
// dev-context pour le détail de cette limitation.
public class DnsFilterServer
{
    public static readonly IPAddress SinkholeIp = IPAddress.Parse("127.0.0.2");

    private readonly ILogger _logger;
    private readonly int _port;
    private readonly IPEndPoint _upstream = new(IPAddress.Parse("1.1.1.1"), 53);
    private UdpClient? _listener;
    private WebConfig _config = new();

    // Compteur de visites par site (domaine racine approximatif), pour la carte "Sites les plus
    // consultes" du dashboard - alimente par chaque requete DNS autorisee. ConcurrentDictionary
    // car HandleQueryAsync tourne en parallele pour plusieurs requetes simultanees.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _siteCounts = new();

    // Resolveurs DNS-over-HTTPS connus + domaine "canari" de Firefox. Quand la supervision web est
    // active, ces noms recoivent NXDOMAIN : Chrome/Edge (mode automatique) retombent alors sur le
    // DNS systeme (donc sur ce filtre) et Firefox desactive son DoH quand le canari n'existe pas.
    // Limite : un navigateur configure pour joindre un resolveur DoH directement par adresse IP,
    // ou vers un fournisseur absent de cette liste, n'est pas couvert (voir dev-context).
    private static readonly string[] DohDomains =
    {
        "use-application-dns.net", "dns.google", "dns.google.com", "cloudflare-dns.com",
        "one.one.one.one", "dns.quad9.net", "dns9.quad9.net", "dns10.quad9.net", "dns11.quad9.net",
        "doh.opendns.com", "dns.adguard.com", "dns.adguard-dns.com", "doh.cleanbrowsing.org",
        "doh.dns.sb", "dns.nextdns.io", "doh.mullvad.net", "dns.controld.com", "doh.libredns.gr",
    };

    private static bool IsDohProvider(string domain)
    {
        domain = domain.TrimEnd('.').ToLowerInvariant();
        return DohDomains.Any(d => domain == d || domain.EndsWith("." + d, StringComparison.Ordinal));
    }

    // Leve quand un site est bloque (au plus une fois toutes les 10 s par entree, pour ne pas
    // inonder l'interface lors du chargement d'une page qui declenche des dizaines de requetes).
    public event Action<string>? SiteBlocked;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _lastBlockedNotified = new();

    private void NotifyBlocked(string domain)
    {
        var entry = FindBlockingEntry(domain);
        var now = DateTime.UtcNow;
        var last = _lastBlockedNotified.GetOrAdd(entry, DateTime.MinValue);
        if (now - last < TimeSpan.FromSeconds(10)) return;
        _lastBlockedNotified[entry] = now;
        SiteBlocked?.Invoke(entry);
    }

    private string FindBlockingEntry(string domain)
    {
        var web = _config;
        domain = domain.TrimEnd('.').ToLowerInvariant();
        if (!web.WhitelistMode)
        {
            var hit = web.Blacklist.FirstOrDefault(d =>
            {
                var n = d.ToLowerInvariant();
                return domain == n || domain.EndsWith("." + n, StringComparison.Ordinal);
            });
            if (hit is not null) return hit;
        }
        return RootDomain(domain);
    }

    public bool IsRunning => _listener is not null;

    public DnsFilterServer(ILogger logger, int port = 53)
    {
        _logger = logger;
        _port = port;
    }

    public void UpdateConfig(WebConfig web) => _config = web;

    // Vue instantanee des sites les plus visites (domaine, nombre de requetes), triee par ordre
    // decroissant. Approximation volontairement simple : compte chaque requete DNS autorisee sur
    // le domaine racine (2 derniers labels, ex. "www.youtube.com" -> "youtube.com") - ne gere pas
    // les suffixes composes (ex. "site.co.uk"), suffisant pour un premier apercu utile au parent.
    public IReadOnlyList<(string Site, int Count)> GetTopSites(int limit) =>
        _siteCounts
            .OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

    public void LoadCounts(IDictionary<string, int> counts)
    {
        foreach (var kv in counts) _siteCounts[kv.Key] = kv.Value;
    }

    public Dictionary<string, int> ExportCounts() => new(_siteCounts);

    public void ResetDailyCounters()
    {
        _siteCounts.Clear();
        _lastCounted.Clear();
    }

    // Exclut les domaines techniques qui polluent le classement "sites les plus consultes" sans
    // representer une vraie visite : requetes DNS inverses (in-addr.arpa) et notre propre domaine
    // de healthcheck utilise par DnsWatchdog.
    private static readonly string[] InfrastructureSuffixes =
    {
        ".arpa", ".local", ".localdomain", ".lan", ".home", ".internal",
        "googleapis.com", "gstatic.com", "googleusercontent.com", "googlevideo.com", "ggpht.com",
        "gvt1.com", "gvt2.com", "doubleclick.net", "googlesyndication.com", "google-analytics.com",
        "googletagmanager.com", "fbcdn.net", "akamaihd.net", "akamaiedge.net", "akamai.net",
        "cloudfront.net", "cloudflare.com", "fastly.net", "azureedge.net", "windowsupdate.com",
        "microsoft.com", "msftconnecttest.com", "msedge.net", "office.com", "office.net",
        "live.com", "bing.com", "digicert.com", "letsencrypt.org", "sentry.io", "trafficmanager.net",
    };

    private static bool ShouldCountAsVisit(string domain)
    {
        domain = domain.TrimEnd('.').ToLowerInvariant();
        if (domain == "nautilyou-healthcheck.local" || domain == "wpad" || !domain.Contains('.')) return false;
        return !InfrastructureSuffixes.Any(s => s.StartsWith('.') ? domain.EndsWith(s, StringComparison.Ordinal)
                                                                   : domain == s || domain.EndsWith("." + s, StringComparison.Ordinal));
    }

    // Un site compte au plus une fois par minute : mesure une presence dans le temps,
    // pas un volume de requetes (une page ou un onglet en arriere-plan en genere des dizaines).
    private static readonly TimeSpan VisitBucket = TimeSpan.FromMinutes(1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _lastCounted = new();

    private void CountVisit(string domain)
    {
        var root = RootDomain(domain);
        var now = DateTime.UtcNow;
        var last = _lastCounted.GetOrAdd(root, DateTime.MinValue);
        if (now - last < VisitBucket) return;
        _lastCounted[root] = now;
        _siteCounts.AddOrUpdate(root, 1, (_, count) => count + 1);
    }

    private static string RootDomain(string domain)
    {
        var labels = domain.TrimEnd('.').Split('.');
        return labels.Length <= 2 ? domain.TrimEnd('.') : string.Join('.', labels[^2..]);
    }

    public void Start(CancellationToken ct)
    {
        try
        {
            _listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, _port));
            // Bufferi reseau agrandi (defaut Windows souvent ~8-64 Ko) : une page web genere
            // facilement plusieurs dizaines de requetes DNS en rafale (sous-domaines, trackers,
            // CDN...) - sans marge suffisante, des paquets peuvent etre perdus silencieusement au
            // niveau du systeme sous forte charge, y compris ceux du propre healthcheck du
            // DnsWatchdog, donnant l'impression a tort que le filtre a cesse de repondre (constate
            // le 2026-09-23 : le filtre resolvait normalement juste avant un hoquet de ~15s).
            _listener.Client.ReceiveBufferSize = 1024 * 1024;

            // Desactive SIO_UDP_CONNRESET (Windows uniquement) : sans ca, un ICMP "port injoignable"
            // recu suite a une requete forwardee vers un site qui ne repond pas peut faire lever une
            // SocketException (WSAECONNRESET) au prochain ReceiveAsync, meme si le socket est tout a
            // fait valide. C'etait la vraie cause du "hoquet" du filtre trouve en testant le
            // 2026-09-23 (voir aussi le correctif dans ListenLoopAsync qui ne tue plus la boucle sur
            // ce genre d'exception transitoire - les deux se completent).
            if (OperatingSystem.IsWindows())
            {
                const int sioUdpConnReset = unchecked((int)0x9800000C);
                _listener.Client.IOControl(sioUdpConnReset, new byte[] { 0 }, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Filtrage DNS indisponible sur le port {Port} (droits admin requis ?) : {Message}",
                _port, ex.Message);
            _listener = null;
            return;
        }

        _logger.LogInformation("Filtrage DNS demarre sur 127.0.0.1:{Port}", _port);
        _ = Task.Run(() => ListenLoopAsync(ct), ct);
    }

    public void Stop()
    {
        _listener?.Dispose();
        _listener = null;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            UdpReceiveResult result;
            try
            {
                result = await _listener.ReceiveAsync(ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                break; // socket ferme (Stop()) ou service arrete : la seule vraie raison d'arreter
            }
            catch (Exception ex)
            {
                // Bug trouve le 2026-09-23 (cause du "hoquet" du filtre pendant les tests reels) :
                // un `catch { break; }` generique ici tuait TOUTE la boucle d'ecoute silencieusement
                // des la premiere exception transitoire - typiquement une SocketException
                // (WSAECONNRESET) declenchee par un ICMP "port injoignable" recu en reponse a une
                // requete forwardee vers un site qui ne repond pas, un phenomene tres courant et
                // normal sur un socket UDP Windows, pas une vraie panne. Le filtre restait ensuite
                // muet indefiniment (plus aucune requete traitee, meme le healthcheck du watchdog)
                // jusqu'a la restauration DHCP. Desormais on logue et on CONTINUE d'ecouter : seule
                // une annulation ou une fermeture reelle du socket doit arreter la boucle.
                _logger.LogWarning(ex, "Erreur transitoire sur le socket DNS (on continue d'ecouter) : {Message}", ex.Message);
                continue;
            }

            _ = HandleQueryAsync(result.Buffer, result.RemoteEndPoint);
        }
    }

    private async Task HandleQueryAsync(byte[] query, IPEndPoint client)
    {
        try
        {
            var domain = TryParseQuestionName(query);
            if (domain is not null && _config.SupervisionOn && IsDohProvider(domain))
            {
                var nx = (byte[])query.Clone();
                nx[2] |= 0x80; // QR = 1
                nx[3] = (byte)((nx[3] & 0xF0) | 0x03); // RCODE = NXDOMAIN
                if (_listener is not null) await _listener.SendAsync(nx, client);
                _logger.LogInformation("Resolveur DNS-over-HTTPS neutralise (NXDOMAIN) : {Domain}", domain);
                return;
            }
            var allowed = domain is null || IsAllowed(domain);
            _logger.LogInformation("Requete DNS : {Domain} -> {Decision}", domain ?? "(non parsee)", allowed ? "autorise" : "bloque");

            if (allowed)
            {
                if (domain is not null && ShouldCountAsVisit(domain))
                {
                    CountVisit(domain);
                }
                await ForwardAsync(query, client);
            }
            else
            {
                var blocked = BuildSinkholeOrNxDomainResponse(query);
                if (_listener is not null) await _listener.SendAsync(blocked, client);
                _logger.LogInformation("Site bloque (DNS) : {Domain}", domain);
                if (domain is not null) NotifyBlocked(domain);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erreur de traitement d'une requete DNS : {Message}", ex.Message);
        }
    }

    private bool IsAllowed(string domain)
    {
        var web = _config;
        if (!web.SupervisionOn) return true;

        domain = domain.TrimEnd('.').ToLowerInvariant();
        bool MatchesAny(List<string> list) => list.Any(d =>
        {
            var normalized = d.ToLowerInvariant();
            return domain == normalized || domain.EndsWith("." + normalized, StringComparison.Ordinal);
        });

        return web.WhitelistMode ? MatchesAny(web.Whitelist) : !MatchesAny(web.Blacklist);
    }

    private async Task ForwardAsync(byte[] query, IPEndPoint client)
    {
        if (_listener is null) return;
        try
        {
            using var upstreamClient = new UdpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await upstreamClient.SendAsync(query, _upstream, cts.Token);
            var response = await upstreamClient.ReceiveAsync(cts.Token);
            await _listener.SendAsync(response.Buffer, client);
            _logger.LogInformation("Resolution en amont OK ({Bytes} octets recus de {Upstream})", response.Buffer.Length, _upstream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Echec de resolution DNS en amont vers {Upstream} : {Message}", _upstream, ex.Message);
        }
    }

    // Réponse à un site bloqué : pour une requête A (IPv4), renvoie l'IP du sinkhole (127.0.0.2)
    // où BlockedPageServer sert la page "site bloqué" ; pour tout autre type, NXDOMAIN (plus
    // simple et suffisant, ces requêtes ne mènent pas à une page web affichable de toute façon).
    private static byte[] BuildSinkholeOrNxDomainResponse(byte[] query)
    {
        if (query.Length < 4) return query;

        var questionEnd = TryParseQuestionEnd(query);
        var qtype = questionEnd is int end && end >= 4 ? (query[end - 4] << 8) | query[end - 3] : -1;

        if (questionEnd is int qEnd && qtype == 1) // A record
        {
            var header = new byte[12];
            Array.Copy(query, 0, header, 0, 2); // ID
            header[2] = (byte)(query[2] | 0x80); // QR = 1 (garde Opcode/AA/TC/RD de la requete)
            header[3] = 0x80; // RA = 1, Z = 0, RCODE = 0 (succes)
            header[5] = 0x01; // QDCOUNT = 1
            header[7] = 0x01; // ANCOUNT = 1
            // NSCOUNT / ARCOUNT restent a 0

            var question = new byte[qEnd - 12];
            Array.Copy(query, 12, question, 0, question.Length);

            var answer = new byte[] { 0xC0, 0x0C, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x04 }
                .Concat(SinkholeIp.GetAddressBytes())
                .ToArray();

            return header.Concat(question).Concat(answer).ToArray();
        }

        var nxdomain = (byte[])query.Clone();
        nxdomain[2] |= 0x80; // QR = 1
        nxdomain[3] = (byte)((nxdomain[3] & 0xF0) | 0x03); // RCODE = 3 (NXDOMAIN)
        return nxdomain;
    }

    private static string? TryParseQuestionName(byte[] packet)
    {
        try
        {
            var pos = 12; // apres l'en-tete DNS fixe de 12 octets
            var labels = new List<string>();
            while (pos < packet.Length)
            {
                int len = packet[pos];
                if (len == 0)
                {
                    pos++;
                    break;
                }
                pos++;
                if (pos + len > packet.Length) return null;
                labels.Add(Encoding.ASCII.GetString(packet, pos, len));
                pos += len;
            }
            return labels.Count > 0 ? string.Join('.', labels) : null;
        }
        catch
        {
            return null;
        }
    }

    // Retourne la position juste apres le QTYPE+QCLASS de la question (donc la fin de la section
    // question), pour savoir ou s'arrete la partie a recopier telle quelle dans la reponse.
    private static int? TryParseQuestionEnd(byte[] packet)
    {
        try
        {
            var pos = 12;
            while (pos < packet.Length)
            {
                int len = packet[pos];
                if (len == 0)
                {
                    pos++;
                    break;
                }
                pos += 1 + len;
            }
            pos += 4; // QTYPE (2) + QCLASS (2)
            return pos <= packet.Length ? pos : null;
        }
        catch
        {
            return null;
        }
    }
}
