using System.Runtime.InteropServices;

namespace NautilyouService;

public static class SessionLock
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    public static void Lock()
    {
        if (OperatingSystem.IsWindows())
        {
            LockWorkStation();
        }
    }
}
