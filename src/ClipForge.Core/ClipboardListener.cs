using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClipForge.Core.Native;
using static ClipForge.Core.Native.NativeMethods;

namespace ClipForge.Core;

/// <summary>
/// Listens for clipboard changes via AddClipboardFormatListener on a hidden
/// message-only window running on its own STA thread. Raises <see cref="ClipCaptured"/>
/// with the new payload (text, image, files, or HTML fragment).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ClipboardListener : IDisposable
{
    public event EventHandler<CapturedClip>? ClipCaptured;

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _origProc = IntPtr.Zero;
    private readonly WndProcDelegate _wndProc;
    private readonly ManualResetEventSlim _ready = new(false);
    private DateTime _suppressUntilUtc = DateTime.MinValue;
    private bool _disposed;

    public ClipboardListener()
    {
        _wndProc = WndProc;
    }

    public bool IsRunning => _thread is { IsAlive: true };

    public void Start()
    {
        if (IsRunning) return;
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "ClipForge Clipboard Listener"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Clipboard listener thread did not start.");
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Clipboard listener window was not created.");
    }

    /// <summary>
    /// Suppresses capture for <paramref name="duration"/> — call around our own
    /// paste operations so we don't re-capture what we just pasted.
    /// </summary>
    public IDisposable SuppressCapture(TimeSpan duration)
    {
        _suppressUntilUtc = DateTime.UtcNow + duration;
        return new Suppressor(this, duration);
    }

    private sealed class Suppressor : IDisposable
    {
        private readonly ClipboardListener _owner;
        private readonly TimeSpan _duration;
        private readonly DateTime _until;
        public Suppressor(ClipboardListener owner, TimeSpan duration)
        {
            _owner = owner;
            _duration = duration;
            _until = DateTime.UtcNow + duration;
        }
        public void Dispose()
        {
            // Only clear if nobody extended the window since.
            if (_owner._suppressUntilUtc <= _until + TimeSpan.FromMilliseconds(50))
                _owner._suppressUntilUtc = DateTime.MinValue;
        }
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _hwnd = CreateWindowExW(0, "STATIC", "ClipForgeListener", 0, 0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd != IntPtr.Zero)
        {
            _origProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_wndProc));
            AddClipboardFormatListener(_hwnd);
        }
        _ready.Set();

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        if (_hwnd != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_hwnd);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            try { OnClipboardUpdate(); }
            catch { /* never let a capture crash the message loop */ }
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Reads whatever is on the clipboard right now. Used at startup (so the
    /// pre-existing clipboard content becomes the first history entry) and by tests.
    /// </summary>
    public static CapturedClip? CaptureCurrent()
    {
        var type = WindowsClipboard.AvailableType();
        return type is null ? null : TryCapture(type.Value);
    }

    private void OnClipboardUpdate()
    {
        if (DateTime.UtcNow < _suppressUntilUtc) return;

        var type = WindowsClipboard.AvailableType();
        if (type is null) return;

        var captured = TryCapture(type.Value);
        if (captured is null)
        {
            // Trust engineering: a transiently locked clipboard must not silently
            // eat a copy. One delayed retry before giving up on this update.
            Thread.Sleep(150);
            captured = TryCapture(type.Value);
        }

        if (captured is not null)
            ClipCaptured?.Invoke(this, captured);
    }

    private static CapturedClip? TryCapture(ClipType type)
    {
        var app = ForegroundApp.GetProcessName();
        var now = DateTimeOffset.Now;
        return type switch
        {
            ClipType.Files when WindowsClipboard.TryGetFileList(out var paths) =>
                new CapturedClip(ClipType.Files, null, null, paths, app, null, null, now),
            ClipType.Image when WindowsClipboard.TryGetImage(out var png, out int w, out int h) =>
                new CapturedClip(ClipType.Image, null, png, null, app, w, h, now),
            ClipType.Html when WindowsClipboard.TryGetHtml(out var html) && !string.IsNullOrWhiteSpace(html) =>
                new CapturedClip(ClipType.Html, html, null, null, app, null, null, now),
            ClipType.Text when WindowsClipboard.TryGetText(out var text) && !string.IsNullOrEmpty(text) =>
                new CapturedClip(ClipType.Text, text, null, null, app, null, null, now),
            _ => null
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_thread is { IsAlive: true })
        {
            PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(5));
        }
        _ready.Dispose();
        GC.SuppressFinalize(this);
    }
}
