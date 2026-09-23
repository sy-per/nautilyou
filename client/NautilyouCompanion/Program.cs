using Microsoft.Extensions.Logging;

namespace NautilyouCompanion;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("NautilyouCompanion");

        var status = new ClientStatus();
        var pipeClient = new PipeClient(logger);
        var sessionLockState = new SessionLockState();

        var cts = new CancellationTokenSource();
        _ = pipeClient.RunAsync(cts.Token);

        // TrayApp.Run() appelle Application.Run() et bloque ce thread STA jusqu'à Application.Exit().
        TrayApp.Run(status, pipeClient, sessionLockState);

        cts.Cancel();
    }
}
