using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using NautilyouShared;

namespace NautilyouService;

// Canal IPC local entre le service (Session 0, sans UI) et l'appli compagnon (systray, dans la
// session utilisateur). Voir dev-context pour pourquoi cette séparation est nécessaire (un
// service Windows en Session 0 ne peut pas afficher d'icône systray).
//
// Securise le 2026-09-23 : PipeSecurity explicite (ACL par defaut de .NET peut refuser la
// connexion d'un utilisateur interactif normal a un pipe cree par un service tournant en SYSTEM/
// Session 0 - necessaire une fois installe en vrai service Windows, pas juste en dev via
// `dotnet run` ou service et Companion partagent la meme session). SYSTEM et les administrateurs
// ont controle total (creation/gestion du pipe), "INTERACTIVE" (toute session de connexion
// interactive, pas un SID d'utilisateur specifique car on ne sait pas a l'avance qui sera
// connecte) a un acces lecture/ecriture suffisant pour dialoguer avec le Service. Delibérement
// PAS "Tout le monde" (Everyone) : un service reseau ou un autre utilisateur de la machine ne
// doit pas pouvoir se faire passer pour le Companion et envoyer des commandes (pair-request,
// time-request...).
public class PipeServer
{
    private readonly ILogger _logger;
    private readonly List<PipeMessenger> _clients = new();
    private readonly object _lock = new();

    public event Action<int>? TimeRequested;
    public event Action<bool>? SessionLockChanged;
    public event Action<string>? AccessRequested;

    public PipeServer(ILogger logger)
    {
        _logger = logger;
    }

    public void Start(CancellationToken ct)
    {
        _ = Task.Run(() => AcceptLoopAsync(ct), ct);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(
                PipeProtocol.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }

        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeProtocol.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var server = CreatePipe();

            try
            {
                await server.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Pipe local : erreur d'acceptation : {Message}", ex.Message);
                server.Dispose();
                continue;
            }

            _logger.LogInformation("Appli compagnon (systray) connectee au pipe local");
            var messenger = new PipeMessenger(server);
            lock (_lock) _clients.Add(messenger);
            _ = HandleClientAsync(server, messenger, ct);
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, PipeMessenger messenger, CancellationToken ct)
    {
        try
        {
            while (server.IsConnected && !ct.IsCancellationRequested)
            {
                var msg = await messenger.ReceiveAsync(ct);
                if (msg is null) break;
                var (type, payload) = msg.Value;

                if (type == "time-request")
                {
                    var req = JsonSerializer.Deserialize<TimeRequestMessage>(payload.GetRawText(), PipeMessenger.JsonOptions);
                    if (req is not null) TimeRequested?.Invoke(req.Minutes);
                }
                else if (type == "session-lock")
                {
                    var lockMsg = JsonSerializer.Deserialize<SessionLockMessage>(payload.GetRawText(), PipeMessenger.JsonOptions);
                    if (lockMsg is not null) SessionLockChanged?.Invoke(lockMsg.Locked);
                }
                else if (type == "access-request")
                {
                    var req = JsonSerializer.Deserialize<AccessRequestMessage>(payload.GetRawText(), PipeMessenger.JsonOptions);
                    if (req is not null && !string.IsNullOrWhiteSpace(req.Domain)) AccessRequested?.Invoke(req.Domain);
                }
                else if (type == "pair-request")
                {
                    var req = JsonSerializer.Deserialize<PairRequestMessage>(payload.GetRawText(), PipeMessenger.JsonOptions);
                    if (req is not null)
                    {
                        var (success, message) = await PairingService.TryPairAsync(req.ServerUrl, req.Code);
                        await messenger.SendAsync("pair-result", new PairResultMessage { Success = success, Message = message }, ct);
                    }
                }
            }
        }
        catch
        {
            // companion deconnecte brutalement, rien a faire de plus
        }
        finally
        {
            lock (_lock) _clients.Remove(messenger);
            server.Dispose();
            _logger.LogInformation("Appli compagnon deconnectee du pipe local");
        }
    }

    public Task BroadcastStatusAsync(StatusMessage status) => BroadcastAsync("status", status);

    public Task BroadcastBalloonAsync(BalloonMessage balloon) => BroadcastAsync("balloon", balloon);

    public Task BroadcastBlockedSiteAsync(string domain) => BroadcastAsync("blocked-site", new BlockedSiteMessage { Domain = domain });

    public Task BroadcastPairingNeededAsync() => BroadcastAsync("pairing-needed", new PairingNeededMessage());

    private async Task BroadcastAsync(string type, object payload)
    {
        List<PipeMessenger> snapshot;
        lock (_lock) snapshot = new List<PipeMessenger>(_clients);

        foreach (var client in snapshot)
        {
            try
            {
                await client.SendAsync(type, payload);
            }
            catch
            {
                // sera retire de la liste au prochain cycle de HandleClientAsync qui detectera la deconnexion
            }
        }
    }
}
