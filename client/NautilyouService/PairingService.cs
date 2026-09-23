using System.Net;

namespace NautilyouService;

// Logique de pairing partagée entre le prompt console (dev/debug, voir Worker.EnsurePairedAsync)
// et le pairing via le pipe local demandé par le Companion (voir dev-context, section "Interface
// de pairing dans le Companion") — évite de dupliquer la validation d'URL et l'appel API.
public static class PairingService
{
    public static async Task<(bool Success, string Message)> TryPairAsync(string serverUrlRaw, string codeRaw)
    {
        var input = (serverUrlRaw ?? "").Trim().TrimEnd('/');
        var code = (codeRaw ?? "").Trim();

        // Sans schéma ("192.168.1.50"), on essaie HTTPS d'abord (cas normal : le serveur Docker est
        // derrière nginx en HTTPS), puis HTTP (serveur de développement en clair).
        var hasScheme = input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var candidates = hasScheme ? new[] { input } : new[] { "https://" + input, "http://" + input };

        if (candidates.Any(c => !Uri.TryCreate(c, UriKind.Absolute, out _)))
        {
            return (false, $"Adresse invalide : '{serverUrlRaw}'");
        }

        var (_, publicKeyBase64) = DeviceKeyStore.LoadOrCreate();
        string lastError = "";

        foreach (var serverUrl in candidates)
        {
            try
            {
                var tls = new TlsPinning(null, allowFirstUse: true);
                var api = new NautilyouApiClient(serverUrl, tls);
                var device = await api.CompletePairingAsync(code, Environment.MachineName, "Windows (client Nautilyou)", publicKeyBase64);

                var state = new PairingState
                {
                    ServerUrl = serverUrl,
                    DeviceId = device.Id,
                    CertSha256 = tls.PinToStore,
                };
                state.Save();

                return (true, $"Appairage reussi : appareil '{device.Name}'.");
            }
            catch (HttpRequestException ex) when (ex.StatusCode is not null)
            {
                // Le serveur a répondu : inutile d'essayer l'autre schéma, le problème est le code.
                return (false, ex.StatusCode switch
                {
                    HttpStatusCode.NotFound => "Code invalide. Verifie les 6 chiffres affiches dans le dashboard.",
                    HttpStatusCode.Gone => "Code expire ou deja utilise. Genere-en un nouveau dans le dashboard.",
                    _ => $"Le serveur a refuse la demande ({(int)ex.StatusCode})."
                });
            }
            catch (Exception ex)
            {
                lastError = ex.InnerException?.Message ?? ex.Message;
            }
        }

        return (false, $"Serveur injoignable a cette adresse ({lastError}).");
    }
}
