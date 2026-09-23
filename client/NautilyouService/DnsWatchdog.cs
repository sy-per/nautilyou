using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NautilyouService;

// Garde-fou ajoute le 2026-09-23 suite a deux pannes DNS reelles pendant les tests (le poste
// pointait vers le filtre local via netsh, mais celui-ci ne repondait plus - process ferme,
// port libere, etc. - coupant tout acces internet jusqu'a une restauration manuelle via
// restore-dns.ps1). Ce watchdog envoie regulierement une vraie requete DNS au filtre local
// (127.0.0.1:port, exactement le chemin qu'emprunterait un vrai navigateur) et restaure le DNS
// DHCP automatiquement si le filtre ne repond plus, sans attendre une intervention manuelle.
public class DnsWatchdog
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    // Timeout assoupli 2s -> 3s le 2026-09-23 (theorie : une rafale de requetes lors d'un
    // chargement de page peut ralentir/perdre temporairement la reponse au healthcheck sans que
    // le filtre soit reellement en panne - voir DnsFilterServer.Start pour le buffer agrandi en
    // complement). Reste largement suffisant face a une vraie panne (process mort, port libere).
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);
    private const int FailureThreshold = 4; // ~20s de filtre injoignable avant de restaurer le DHCP

    private readonly ILogger _logger;
    private readonly int _port;
    private int _consecutiveFailures;

    public DnsWatchdog(ILogger logger, int port = 53)
    {
        _logger = logger;
        _port = port;
    }

    // S'arrete des que le filtre est jugé injoignable (appelle alors onUnhealthy, typiquement
    // DnsAdapterConfigurator.RestoreDhcp) ou que le token est annule. isAdapterConfigured permet
    // au Worker de signaler que le DNS n'est de toute facon plus pointe vers le filtre local
    // (deja restaure ailleurs) pour que le watchdog arrete de verifier inutilement.
    public async Task RunAsync(Func<bool> isAdapterConfigured, Action onUnhealthy, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!isAdapterConfigured())
            {
                _consecutiveFailures = 0;
                continue;
            }

            var healthy = await CheckAsync(ct);
            if (healthy)
            {
                _consecutiveFailures = 0;
                continue;
            }

            _consecutiveFailures++;
            _logger.LogWarning(
                "Watchdog DNS : le filtre local ne repond pas ({Count}/{Threshold})",
                _consecutiveFailures, FailureThreshold);

            if (_consecutiveFailures >= FailureThreshold)
            {
                _logger.LogError(
                    "Watchdog DNS : filtre local injoignable depuis trop longtemps - restauration automatique du DNS DHCP pour ne pas couper l'acces internet");
                onUnhealthy();
                return;
            }
        }
    }

    // Envoie une vraie requete DNS (type A) au resolveur local et verifie qu'une reponse valide
    // revient avec le meme ID de transaction - peu importe le contenu (accepte, bloque, NXDOMAIN),
    // seul compte le fait que quelque chose repond reellement sur le port.
    private async Task<bool> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(CheckTimeout);

            var query = BuildHealthcheckQuery(out var id);
            await udp.SendAsync(query, new IPEndPoint(IPAddress.Loopback, _port), timeoutCts.Token);
            var result = await udp.ReceiveAsync(timeoutCts.Token);

            return result.Buffer.Length >= 2
                && result.Buffer[0] == (byte)(id >> 8)
                && result.Buffer[1] == (byte)id;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Watchdog DNS : healthcheck en timeout apres {Timeout}", CheckTimeout);
            return false;
        }
        catch (Exception ex)
        {
            // Avant le 2026-09-23 cette exception etait avalee sans etre logguee, rendant
            // impossible de savoir pourquoi le healthcheck echouait (timeout ? erreur reseau ?
            // socket refuse ?) lors d'un incident reel (le filtre avait cesse de repondre ~15s
            // apres avoir resolu des requetes normalement, cause encore inconnue).
            _logger.LogWarning(ex, "Watchdog DNS : healthcheck en echec ({Type}) : {Message}", ex.GetType().Name, ex.Message);
            return false;
        }
    }

    private static byte[] BuildHealthcheckQuery(out ushort id)
    {
        id = (ushort)Random.Shared.Next(1, ushort.MaxValue);

        var header = new byte[]
        {
            (byte)(id >> 8), (byte)id, // ID
            0x01, 0x00,                // Flags : query standard, recursion desiree
            0x00, 0x01,                // QDCOUNT = 1
            0x00, 0x00,                // ANCOUNT = 0
            0x00, 0x00,                // NSCOUNT = 0
            0x00, 0x00,                // ARCOUNT = 0
        };

        var question = EncodeQuestion("nautilyou-healthcheck.local");
        return header.Concat(question).ToArray();
    }

    private static byte[] EncodeQuestion(string domain)
    {
        var bytes = new List<byte>();
        foreach (var label in domain.Split('.'))
        {
            var labelBytes = Encoding.ASCII.GetBytes(label);
            bytes.Add((byte)labelBytes.Length);
            bytes.AddRange(labelBytes);
        }
        bytes.Add(0x00); // fin du nom

        bytes.Add(0x00); bytes.Add(0x01); // QTYPE = A
        bytes.Add(0x00); bytes.Add(0x01); // QCLASS = IN

        return bytes.ToArray();
    }
}
