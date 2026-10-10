using System.Diagnostics;

namespace ClipForge.Core.Native;

/// <summary>Resolves which app owned the foreground window at capture time (for source_app).</summary>
internal static class ForegroundApp
{
    public static string? GetProcessName()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
