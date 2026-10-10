using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ClipForge.Core.Native;
using static ClipForge.Core.Native.NativeMethods;

namespace ClipForge.App;

/// <summary>Global hotkey that summons the overlay from anywhere. The chord is rebindable via settings.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0xC10F; // arbitrary, unlikely to collide

    private readonly HwndSource _source;
    private readonly Hwnd _hwnd;
    private bool _disposed;

    public event EventHandler? Pressed;

    public HotkeyService(Window window, HotkeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(chord);
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _hwnd = Hwnd.FromIntPtr(helper.Handle);
        _source = HwndSource.FromHwnd(_hwnd.ToIntPtr()) ?? throw new InvalidOperationException("No HWND source.");
        _source.AddHook(WndProc);
        var (modifiers, vk) = chord.ToWin32();
        if (!RegisterHotKey(_hwnd.ToIntPtr(), HotkeyId, modifiers, vk))
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"RegisterHotKey failed for {chord} (error {err}). Another app may own it.");
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
        UnregisterHotKey(_hwnd.ToIntPtr(), HotkeyId);
    }
}
