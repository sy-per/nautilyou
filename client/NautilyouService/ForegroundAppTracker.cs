using System.Runtime.InteropServices;

namespace NautilyouService;

// Identifie l'application au premier plan pour accumuler un temps par app (affiché dans le
// systray). Suivi simple par nom de process Windows — ne tente pas de faire correspondre ce nom
// aux "apps" que le parent configure dans le dashboard (ex. "Instagram" vs le process "chrome"
// ou "ApplicationFrameHost") : cette correspondance reste un problème ouvert, voir dev-context.
public static class ForegroundAppTracker
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static string? GetForegroundProcessName()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == 0) return null;
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
