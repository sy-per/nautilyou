using System.Text;
using System.Text.Json;

namespace NautilyouShared;

// Envoie/reçoit des messages JSON ligne par ligne sur un Stream (NamedPipeServerStream ou
// NamedPipeClientStream des deux côtés) — même format {type, payload} que le WebSocket
// service<->serveur, pour rester cohérent dans tout le client.
public class PipeMessenger
{
    // Web defaults = camelCase + insensible à la casse : nécessaire aussi côté récepteur pour
    // désérialiser les payloads (ex. "minutes" en JSON -> propriété "Minutes" en C#), sans quoi
    // System.Text.Json reste sensible à la casse par défaut et les champs restent à leur valeur
    // par défaut silencieusement (bug trouvé le 2026-09-23 : une demande de temps arrivait à 0min
    // au lieu de la valeur demandée).
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Stream _stream;
    private readonly StreamReader _reader;

    public PipeMessenger(Stream stream)
    {
        _stream = stream;
        _reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
    }

    public async Task SendAsync(string type, object payload, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new { type, payload }, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await _stream.WriteAsync(bytes, ct);
        await _stream.FlushAsync(ct);
    }

    // Retourne null si le pipe est fermé (fin de flux).
    public async Task<(string Type, JsonElement Payload)?> ReceiveAsync(CancellationToken ct = default)
    {
        var line = await _reader.ReadLineAsync(ct);
        if (line is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString() ?? "";
            var payload = root.GetProperty("payload").Clone();
            return (type, payload);
        }
        catch
        {
            return ("", default);
        }
    }
}
