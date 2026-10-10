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
            // A failed lookup leaves pid undefined — bail explicitly instead of probing PID 0.
            if (NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid) == 0) return null;
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        #pragma warning disable CA1031 // why: best-effort Win32 interop — any fault here must degrade to "not available", never propagate to the caller.
        catch
        {
            return null;
        }
        #pragma warning restore CA1031
    }
}
