using System.Diagnostics;
using System.Net.NetworkInformation;

namespace NautilyouService;

// Pointe les interfaces réseau actives de la machine vers le filtre DNS local (127.0.0.1) pour
// que le filtrage s'applique réellement, et restaure le DNS automatique (DHCP) à l'arrêt pour ne
// jamais laisser la machine sans résolution DNS fonctionnelle. Nécessite les droits admin (comme
// DnsFilterServer qui doit d'abord réussir à écouter sur le port 53).
//
// Attention (leçon du 2026-09-23) : `netsh` peut refuser une commande faute de droits admin sans
// que `Process.Start` lève d'exception — il faut explicitement vérifier `ExitCode` et la sortie
// du process, sinon on croit à tort que le DNS a été changé alors que netsh a juste échoué en
// silence.
public static class DnsAdapterConfigurator
{
    // Retourne true seulement si le DNS a été réellement pointé vers le filtre sur au moins une
    // interface (donc qu'il faudra le restaurer plus tard). false si netsh a échoué partout
    // (log warning déjà émis), ce qui évite d'appeler RestoreDhcp() pour rien.
    public static bool PointAtLocalFilter(ILogger logger)
    {
        var anySucceeded = false;
        foreach (var name in ActiveAdapterNames())
        {
            if (RunNetsh(logger, $"interface ip set dns name=\"{name}\" static 127.0.0.1"))
            {
                anySucceeded = true;
                logger.LogInformation("DNS de l'interface '{Name}' pointe vers le filtre local", name);
            }
        }
        return anySucceeded;
    }

    public static void RestoreDhcp(ILogger logger)
    {
        foreach (var name in ActiveAdapterNames())
        {
            if (RunNetsh(logger, $"interface ip set dns name=\"{name}\" dhcp"))
            {
                logger.LogInformation("DNS de l'interface '{Name}' restaure en automatique (DHCP)", name);
            }
        }
    }

    // Bug corrige le 2026-09-23 (3e incident DNS reel) : cette methode ciblait TOUTES les
    // interfaces "Up", y compris des pseudo-interfaces internes de Windows sans rapport avec la
    // connexion reelle (ex. "WFP Native MAC Layer Filter-0000", "QoS Packet Scheduler-0000",
    // "VirtualBox NDIS Light-Weight Filter-0000") visibles dans les logs. Reconfigurer le DNS sur
    // ces couches internes perturbait la resolution globale meme si le filtre local, lui,
    // fonctionnait parfaitement (verifie via nslookup direct sur 127.0.0.1). On ne cible desormais
    // que les interfaces ayant une vraie passerelle IPv4 (donc reellement connectees a un reseau),
    // ce qui exclut naturellement ces pseudo-adaptateurs.
    private static IEnumerable<string> ActiveAdapterNames() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                        && n.GetIPProperties().GatewayAddresses.Any(g =>
                            g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
            .Select(n => n.Name);

    private static bool RunNetsh(ILogger logger, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                logger.LogWarning("Echec de configuration DNS (netsh {Args}) : le process n'a pas pu demarrer", args);
                return false;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(5000);

            if (proc.ExitCode != 0)
            {
                logger.LogWarning(
                    "Echec de configuration DNS (netsh {Args}) : code {ExitCode} - {Output}",
                    args, proc.ExitCode, string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim());
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Echec de configuration DNS (netsh {Args}) : {Message}", args, ex.Message);
            return false;
        }
    }
}
