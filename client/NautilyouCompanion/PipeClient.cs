using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NautilyouShared;

namespace NautilyouCompanion;

// Se connecte au pipe local exposé par NautilyouService (voir dev-context pour pourquoi le
// systray est un processus séparé du service). Reconnexion automatique si le service redémarre.
public class PipeClient
{
    private readonly ILogger _logger;
    private NamedPipeClientStream? _pipe;
    private PipeMessenger? _messenger;

    public event Action<StatusMessage>? StatusReceived;
    public event Action<BalloonMessage>? BalloonReceived;
    public event Action<string>? BlockedSiteReceived;
    public event Action? PairingNeeded;
    public event Action<PairResultMessage>? PairResultReceived;

    public bool IsConnected => _pipe?.IsConnected == true;

    public PipeClient(ILogger logger)
    {
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await _pipe.ConnectAsync(3000, ct);
                _messenger = new PipeMessenger(_pipe);
                _logger.LogInformation("Connecte au service Nautilyou (pipe local)");
                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Service Nautilyou injoignable ({Message}) - nouvelle tentative dans 1.5s", ex.Message);
            }
            finally
            {
                _pipe?.Dispose();
                _pipe = null;
                _messenger = null;
            }

            if (!ct.IsCancellationRequested)
            {
                // Delai court : le formulaire de pairing (et l'ecran de statut) doivent apparaitre
                // vite apres le demarrage du Service, sinon ca donne l'impression que le Companion
                // est bloque (remarque utilisateur du 2026-09-23 : "c'est long").
                await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (_pipe?.IsConnected == true && !ct.IsCancellationRequested)
        {
            var msg = await _messenger!.ReceiveAsync(ct);
            if (msg is null) break;
            var (type, payload) = msg.Value;

            if (type == "status")
            {
                var status = JsonSerializer.Deserialize<StatusMessage>(payload.GetRawText(), PipeMessenger.JsonOptions);
                if (status is not null) StatusReceived?.Invoke(status);
            }
            else if (type == "balloon")
            {
                var balloon = JsonSerializer.Deserialize<BalloonMessage>(payload.GetRawText(), PipeMessenger.JsonOptions);
                if (balloon is not null) BalloonReceived?.Invoke(balloon);
            }
            else if (type == "blocked-site")
            {
                var blocked = JsonSerializer.Deserialize<BlockedSiteMessage>(payload.GetRawText(), PipeMessenger.JsonOptions);
                if (blocked is not null && blocked.Domain.Length > 0) BlockedSiteReceived?.Invoke(blocked.Domain);
            }
            else if (type == "pairing-needed")
            {
                PairingNeeded?.Invoke();
            }
            else if (type == "pair-result")
            {
                var result = JsonSerializer.Deserialize<PairResultMessage>(payload.GetRawText(), PipeMessenger.JsonOptions);
                if (result is not null) PairResultReceived?.Invoke(result);
            }
        }
    }

    public async Task SendPairRequestAsync(string serverUrl, string code)
    {
        if (_messenger is null) return;
        try { await _messenger.SendAsync("pair-request", new PairRequestMessage { ServerUrl = serverUrl, Code = code }); }
        catch { /* le formulaire restera affiche, l'utilisateur peut reessayer */ }
    }

    public async Task SendAccessRequestAsync(string domain)
    {
        if (_messenger is null) return;
        try { await _messenger.SendAsync("access-request", new AccessRequestMessage { Domain = domain }); }
        catch { /* l'enfant pourra recliquer */ }
    }

    public async Task SendTimeRequestAsync(int minutes)
    {
        if (_messenger is null) return;
        try { await _messenger.SendAsync("time-request", new TimeRequestMessage { Minutes = minutes }); }
        catch { /* le service reconnectera cote pipe au prochain cycle */ }
    }

    public async Task SendSessionLockAsync(bool locked)
    {
        if (_messenger is null) return;
        try { await _messenger.SendAsync("session-lock", new SessionLockMessage { Locked = locked }); }
        catch { /* pas grave, le prochain changement d'etat sera renvoye */ }
    }
}
