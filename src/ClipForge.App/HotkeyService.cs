using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static ClipForge.Core.Native.NativeMethods;

namespace ClipForge.App;

/// <summary>Global hotkey (Ctrl+Shift+V) that summons the overlay from anywhere.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0xC10F; // arbitrary, unlikely to collide
    private const uint Modifiers = 0x0002 | 0x0004; // MOD_CONTROL | MOD_SHIFT
    private const uint VkV = 0x56;

    private readonly HwndSource _source;
    private readonly IntPtr _hwnd;
    private bool _disposed;

    public event EventHandler? Pressed;

    public HotkeyService(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _hwnd = helper.Handle;
        _source = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("No HWND source.");
        _source.AddHook(WndProc);
        if (!RegisterHotKey(_hwnd, HotkeyId, Modifiers, VkV))
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"RegisterHotKey failed (error {err}). Another app may own Ctrl+Shift+V.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam == (IntPtr)HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.RemoveHook(WndProc);
        UnregisterHotKey(_hwnd, HotkeyId);
    }
}
