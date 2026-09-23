using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using NautilyouShared;

namespace NautilyouService;

// Parle au serveur Nautilyou en HTTP pour le pairing initial et comme repli d'activité quand le
// WebSocket (DeviceSocketClient) est déconnecté.
public class NautilyouApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public NautilyouApiClient(string serverUrl, TlsPinning? tls = null)
    {
        var handler = new SocketsHttpHandler();
        if (tls is not null) handler.SslOptions.RemoteCertificateValidationCallback = tls.Validate;
        _http = new HttpClient(handler) { BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/api/") };
    }

    public async Task<DeviceDetail> CompletePairingAsync(string code, string deviceName, string model, string publicKey)
    {
        var res = await _http.PostAsJsonAsync("pairing/complete", new PairingCompleteRequest
        {
            Code = code,
            DeviceName = deviceName,
            Model = model,
            PublicKey = publicKey,
        }, JsonOptions);
        res.EnsureSuccessStatusCode();
        var envelope = await res.Content.ReadFromJsonAsync<DeviceDetailEnvelope>(JsonOptions);
        return envelope!.Device;
    }

    public async Task<DeviceDetail?> FetchDeviceAsync(string deviceId)
    {
        var res = await _http.GetAsync($"devices/{deviceId}");
        if (!res.IsSuccessStatusCode) return null;
        var envelope = await res.Content.ReadFromJsonAsync<DeviceDetailEnvelope>(JsonOptions);
        return envelope?.Device;
    }
}
