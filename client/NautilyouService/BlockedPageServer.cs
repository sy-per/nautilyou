using System.Net;
using System.Text;

namespace NautilyouService;

// Sert une page "site bloqué" avec un bouton "Demander l'accès" pour les domaines redirigés par
// DnsFilterServer vers le sinkhole (127.0.0.2). Ne fonctionne visuellement que pour le trafic
// HTTP en clair — la plupart des sites modernes forcent HTTPS, pour lesquels le navigateur
// affichera son propre avertissement de sécurité avant cette page (pas de certificat de confiance
// pour le domaine bloqué). Limitation connue et assumée pour cette itération, voir dev-context.
public class BlockedPageServer
{
    private readonly ILogger _logger;
    private readonly Func<string, Task> _onRequestAccess;
    private readonly string _prefix;
    private HttpListener? _listener;

    public bool IsRunning => _listener is not null;

    public BlockedPageServer(ILogger logger, Func<string, Task> onRequestAccess, string? prefixOverride = null)
    {
        _logger = logger;
        _onRequestAccess = onRequestAccess;
        _prefix = prefixOverride ?? $"http://{DnsFilterServer.SinkholeIp}:80/";
    }

    public void Start(CancellationToken ct)
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(_prefix);
            _listener.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Page de blocage indisponible sur {Prefix} (droits admin requis ?) : {Message}",
                _prefix, ex.Message);
            _listener = null;
            return;
        }

        _logger.LogInformation("Page de blocage demarree sur {Prefix}", _prefix);
        _ = Task.Run(() => ListenLoopAsync(ct), ct);
    }

    public void Stop()
    {
        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener = null;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch
            {
                break; // arret (Stop()) ou service arrete
            }

            _ = HandleRequestAsync(ctx);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        try
        {
            var domain = ctx.Request.UserHostName?.Split(':')[0] ?? "ce site";

            if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/demander-acces")
            {
                await _onRequestAccess(domain);
                await WriteHtmlAsync(ctx, BuildPage(domain, requested: true));
                return;
            }

            await WriteHtmlAsync(ctx, BuildPage(domain, requested: false));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Erreur sur la page de blocage : {Message}", ex.Message);
            try { ctx.Response.Close(); } catch { /* ignore */ }
        }
    }

    private static async Task WriteHtmlAsync(HttpListenerContext ctx, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.OutputStream.Close();
    }

    private static string BuildPage(string domain, bool requested)
    {
        var body = requested
            ? "<p class=\"status\">✅ Demande envoyée. Un parent doit l'approuver depuis le dashboard.</p>"
            : "<form method=\"post\" action=\"/demander-acces\"><button type=\"submit\">Demander l'accès</button></form>";

        return $$"""
        <!DOCTYPE html>
        <html lang="fr">
        <head>
        <meta charset="utf-8" />
        <title>Site bloqué — Nautilyou</title>
        <style>
          body { font-family: -apple-system, "Segoe UI", sans-serif; background: #f1efe9; color: #2c2a26;
                 display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; }
          .card { background: white; border-radius: 18px; padding: 40px; max-width: 420px; text-align: center;
                   box-shadow: 0 12px 40px rgba(0,0,0,0.08); }
          .icon { font-size: 48px; margin-bottom: 8px; }
          h1 { font-size: 20px; margin: 0 0 8px; }
          .domain { color: #8a8578; font-size: 14px; margin-bottom: 24px; word-break: break-all; }
          button { background: #4f7cff; color: white; border: none; border-radius: 20px; padding: 12px 28px;
                    font-size: 14px; font-weight: 600; cursor: pointer; }
          button:hover { background: #3d68e8; }
          .status { color: #2fae7d; font-weight: 600; }
        </style>
        </head>
        <body>
          <div class="card">
            <div class="icon">🔒</div>
            <h1>Ce site est bloqué</h1>
            <div class="domain">{{domain}}</div>
            {{body}}
          </div>
        </body>
        </html>
        """;
    }
}
