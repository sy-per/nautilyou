using NautilyouShared;

namespace NautilyouService;

// Applique les limites par app configurées dans le dashboard (AppsConfig.Limits) et le blocage
// total de certaines apps (AppsConfig.BlockedApps). Le suivi du temps par app se fait par nom de
// process Windows (ForegroundAppTracker) ; la correspondance avec les "apps" telles que le parent
// les nomme dans le dashboard (ex. "Instagram") reste approximative — comparaison insensible à la
// casse, égalité ou inclusion partielle du nom de process. Un parent qui tape "chrome" bloquera le
// process "chrome" ; un parent qui tape "Instagram" ne bloquera rien de spécifique sur desktop
// (pas de process natif "Instagram.exe") — limitation connue, voir dev-context.
//
// Action d'enforcement V1 : terminaison forcée du process (Process.Kill()) quand une limite est
// dépassée ou qu'une app est dans la liste des apps totalement bloquées. Pas de fermeture "douce"
// (WM_CLOSE) pour rester simple et fiable sur tous les types de fenêtres — l'enfant perd son
// travail non sauvegardé dans cette app, c'est un compromis assumé pour cette V1.
public static class AppLimitEnforcer
{
    // Inventaire (nom affiche -> noms de process) : quand un nom configure vient de la liste des applications
    // installees, on compare aux executables exacts de l'application ; sinon (nom saisi a la main) on
    // garde la comparaison approximative historique.
    public static void Enforce(AppsConfig config, Dictionary<string, int> appSecondsToday,
        IReadOnlyDictionary<string, string[]> inventory, ILogger logger)
    {
        if (!config.SupervisionOn) return;

        foreach (var blockedApp in config.BlockedApps)
        {
            KillMatchingProcesses(blockedApp, inventory, logger, reason: "app bloquee");
        }

        foreach (var limit in config.Limits)
        {
            var usedSeconds = limit.Apps
                .SelectMany(appName => appSecondsToday.Where(kv => Matches(kv.Key, appName, inventory)))
                .Sum(kv => kv.Value);

            if (usedSeconds / 60 >= limit.MinutesPerDay)
            {
                foreach (var appName in limit.Apps)
                {
                    KillMatchingProcesses(appName, inventory, logger, reason: $"limite de {limit.MinutesPerDay}min/jour depassee");
                }
            }
        }
    }

    private static void KillMatchingProcesses(string appName, IReadOnlyDictionary<string, string[]> inventory, ILogger logger, string reason)
    {
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (!Matches(process.ProcessName, appName, inventory)) continue;
                logger.LogInformation("Fermeture de {Process} ({Reason})", process.ProcessName, reason);
                process.Kill();
            }
            catch
            {
                // process deja termine, ou pas les droits (process systeme) - on continue
            }
        }
    }

    private static bool Matches(string processName, string configuredAppName, IReadOnlyDictionary<string, string[]> inventory)
    {
        var p = processName.Trim();
        if (inventory.TryGetValue(configuredAppName.Trim(), out var exes))
        {
            return exes.Any(e => e.Equals(p, StringComparison.OrdinalIgnoreCase));
        }

        var a = configuredAppName.Trim();
        return p.Equals(a, StringComparison.OrdinalIgnoreCase)
               || p.Contains(a, StringComparison.OrdinalIgnoreCase)
               || a.Contains(p, StringComparison.OrdinalIgnoreCase);
    }
}
