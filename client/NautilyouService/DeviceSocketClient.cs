using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using NautilyouShared;

namespace NautilyouService;

// Canal de données temps réel avec le serveur : WebSocket persistant, reconnexion automatique.
// Authentification par défi/réponse RSA à la connexion (voir DeviceKeyStore.cs) : le serveur
// envoie un nonce aléatoire, le client le signe avec sa clé privée pour prouver son identité —
// aucun secret statique ne transite jamais sur le fil (contrairement à la V1 qui envoyait un
// GUID en clair comme jeton).
public class DeviceSocketClient
{
    private readonly Uri _wsUri;
    private readonly RSA _rsa;
    private readonly ILogger _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TlsPinning? _tls;
    private ClientWebSocket? _ws;

    public event Action<DeviceDetail>? ConfigReceived;
    public event Action<string, JsonElement>? CommandReceived;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public DeviceSocketClient(string serverUrl, string deviceId, RSA rsa, ILogger logger, TlsPinning? tls = null)
    {
        _tls = tls;
        _rsa = rsa;
        _logger = logger;
        var wsScheme = serverUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var host = serverUrl.Replace("https://", "").Replace("http://", "").TrimEnd('/');
        _wsUri = new Uri($"{wsScheme}://{host}/ws/device?deviceId={Uri.EscapeDataString(deviceId)}");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ws = new ClientWebSocket();
                if (_tls is not null) _ws.Options.RemoteCertificateValidationCallback = _tls.Validate;
                await _ws.ConnectAsync(_wsUri, ct);
                await ReceiveLoopAsync(_ws, ct);
            }
            catch (OperationCanceledException)
            {
                // arret normal
            }
            catch (Exception ex)
            {
                _logger.LogWarning("WebSocket deconnecte ({Message}) - nouvelle tentative dans 5s", ex.Message);
            }
            finally
            {
                _ws?.Dispose();
                _ws = null;
            }

            if (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            await HandleMessageAsync(ws, Encoding.UTF8.GetString(ms.ToArray()));
        }
    }

    private async Task HandleMessageAsync(ClientWebSocket ws, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            if (type == "challenge")
            {
                var nonce = Convert.FromBase64String(root.GetProperty("nonce").GetString()!);
                var signature = DeviceKeyStore.Sign(_rsa, nonce);
                var authMsg = JsonSerializer.Serialize(new { type = "auth", signature = Convert.ToBase64String(signature) }, JsonOptions);
                await ws.SendAsync(Encoding.UTF8.GetBytes(authMsg), WebSocketMessageType.Text, true, CancellationToken.None);
                _logger.LogInformation("Defi d'authentification signe et envoye");
            }
            else if (type == "config")
            {
                var device = JsonSerializer.Deserialize<DeviceDetail>(root.GetProperty("payload").GetRawText(), JsonOptions);
                if (device is not null)
                {
                    _logger.LogInformation("Connecte et authentifie - config recue du serveur");
                    ConfigReceived?.Invoke(device);
                }
            }
            else if (type == "command")
            {
                var payload = root.GetProperty("payload");
                var cmdType = payload.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                CommandReceived?.Invoke(cmdType, payload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Message WebSocket illisible : {Message}", ex.Message);
        }
    }

    public async Task SendActivityAsync(int usedMinutes, IReadOnlyList<(string Site, int Count)>? topSites = null)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var sites = (topSites ?? Array.Empty<(string, int)>()).Select(s => new object[] { s.Site, s.Count });
        var json = JsonSerializer.Serialize(new { type = "activity", payload = new { usedMinutes, topSites = sites } }, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async Task SendInstalledAppsAsync(IReadOnlyList<InstalledApp> apps)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var json = JsonSerializer.Serialize(new { type = "apps", payload = new { apps } }, JsonOptions);
        await _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    // Demande envoyée par l'enfant (bonus temps ou accès à un site bloqué) — voir requests.js
    // côté serveur, affichée dans l'onglet Alertes du dashboard pour approbation par le parent.
    public async Task<bool> SendRequestAsync(string type, object payload)
    {
        if (_ws?.State != WebSocketState.Open) return false;
        var json = JsonSerializer.Serialize(new { type = "request", payload = new { type, payload } }, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        return true;
    }
}
