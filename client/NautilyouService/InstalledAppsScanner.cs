using System.Text.RegularExpressions;
using Microsoft.Win32;
using NautilyouShared;

namespace NautilyouService;

// Inventaire des applications installees, lu dans les memes cles de registre que la liste
// "Applications installees" de Windows (HKLM 64 et 32 bits, HKCU). Pour chaque application on
// retrouve ses executables (icone d'affichage, dossier d'installation) afin que le filtrage par app
// puisse relier le nom que voit le parent (ex. "Google Chrome") au process reel (chrome).
// Limites : les applications du Microsoft Store (UWP) ne sont pas dans ces cles et ne sont pas listees ;
// une application dont aucun executable n'est trouve (pilotes, runtimes) est ignoree car non limitable.
public static class InstalledAppsScanner
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const int MaxExesPerApp = 12;

    private static readonly Regex NoiseName = new(
        @"^(Update for|Security Update|Hotfix|Service Pack)|\bKB\d{6,}\b|Redistributable|Runtime|Driver|pilotes|\.NET (Host|SDK|Runtime)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NoiseExe = new(
        @"unins|uninstall|setup|install|update|crash|report|redist|vc_|dotnet|notification_helper|elevation",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<InstalledApp> Scan()
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return new();

        ScanHive(apps, RegistryHive.LocalMachine, RegistryView.Registry64);
        ScanHive(apps, RegistryHive.LocalMachine, RegistryView.Registry32);
        ScanHive(apps, RegistryHive.CurrentUser, RegistryView.Default);

        return apps.Values
            .Where(a => a.Exes.Count > 0)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ScanHive(Dictionary<string, InstalledApp> apps, RegistryHive hive, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(UninstallPath);
            if (root is null) return;

            foreach (var subName in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(subName);
                    if (key is null) continue;

                    var name = (key.GetValue("DisplayName") as string)?.Trim();
                    if (string.IsNullOrEmpty(name) || NoiseName.IsMatch(name)) continue;
                    if (key.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    if (key.GetValue("ParentKeyName") is not null) continue;

                    var exes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var directories = new List<string>();

                    var iconExe = ExtractExePath(key.GetValue("DisplayIcon") as string);
                    if (iconExe is not null)
                    {
                        AddExe(exes, iconExe);
                        var dir = Path.GetDirectoryName(iconExe);
                        if (dir is not null) directories.Add(dir);
                    }

                    var location = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(location) && Directory.Exists(location)) directories.Add(location);

                    foreach (var dir in directories.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (exes.Count >= MaxExesPerApp) break;
                        foreach (var file in SafeTopLevelExes(dir))
                        {
                            AddExe(exes, file);
                            if (exes.Count >= MaxExesPerApp) break;
                        }
                    }

                    if (exes.Count == 0) continue;

                    if (!apps.TryGetValue(name, out var app))
                    {
                        app = new InstalledApp { Name = name };
                        apps[name] = app;
                    }
                    foreach (var exe in exes)
                    {
                        if (!app.Exes.Contains(exe, StringComparer.OrdinalIgnoreCase)) app.Exes.Add(exe);
                    }
                }
                catch
                {
                    // entree illisible : on passe a la suivante
                }
            }
        }
        catch
        {
            // ruche inaccessible : on continue avec les autres
        }
    }

    private static string? ExtractExePath(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;
        var path = displayIcon.Trim();
        var comma = path.LastIndexOf(',');
        if (comma > 1 && int.TryParse(path[(comma + 1)..], out _)) path = path[..comma];
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    private static IEnumerable<string> SafeTopLevelExes(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly).Take(40).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    private static void AddExe(HashSet<string> exes, string path)
    {
        var file = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(file) || NoiseExe.IsMatch(file)) return;
        exes.Add(file);
    }
}
