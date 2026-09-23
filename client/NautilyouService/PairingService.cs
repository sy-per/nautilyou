namespace NautilyouService;

// Logique de pairing partagée entre le prompt console (dev/debug, voir Worker.EnsurePairedAsync)
// et le pairing via le pipe local demandé par le Companion (voir dev-context, section "Interface
// de pairing dans le Companion") — évite de dupliquer la validation d'URL et l'appel API.
public static class PairingService
{
    public static async Task<(bool Success, string Message)> TryPairAsync(string serverUrlRaw, string codeRaw)
    {
        var serverUrl = (serverUrlRaw ?? "").Trim();
        var code = (codeRaw ?? "").Trim();

        if (!serverUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            serverUrl = "http://" + serverUrl;
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out _))
        {
            return (false, $"URL invalide : '{serverUrl}'");
        }

        try
        {
            var (_, publicKeyBase64) = DeviceKeyStore.LoadOrCreate();
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
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
