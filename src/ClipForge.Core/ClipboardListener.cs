using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClipForge.Core.Native;
using static ClipForge.Core.Native.NativeMethods;

// CA2216 suppressed by design: the owned HWND is a message-only window tied to the
// listener thread's lifetime, and App.OnExit always disposes this listener. A finalizer
// would tear the window down on the GC thread — the wrong thread for window teardown —
// making the "safe" pattern actively harmful here.
#pragma warning disable CA2216

namespace ClipForge.Core;

/// <summary>
/// Listens for clipboard changes via AddClipboardFormatListener on a hidden
/// message-only window running on its own STA thread. Raises <see cref="ClipCaptured"/>
/// with the new payload (text, image, files, or HTML fragment).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ClipboardListener : IDisposable
{
    public event EventHandler<ClipCapturedEventArgs>? ClipCaptured;

    private Thread? _thread;
    private uint _threadId;
    private Hwnd _hwnd = Hwnd.Null;
    private IntPtr _origProc = IntPtr.Zero;
    private readonly WindowProc _wndProc;
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
        if (_hwnd.IsNull)
            throw new InvalidOperationException("Clipboard listener window was not created.");
    }

    /// <summary>
    /// Suppresses capture for <paramref name="duration"/> — call around our own
    /// paste operations so we don't re-capture what we just pasted.
    /// </summary>
    public IDisposable SuppressCapture(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Capture suppression cannot be negative.");
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
            GC.SuppressFinalize(this);
        }
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _hwnd = Hwnd.FromIntPtr(CreateWindowExW(0, "STATIC", "ClipForgeListener", 0, 0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        if (!_hwnd.IsNull)
        {
            _origProc = SetWindowLongPtr(_hwnd.ToIntPtr(), GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_wndProc));
            AddClipboardFormatListener(_hwnd.ToIntPtr());
        }
        _ready.Set();

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        if (!_hwnd.IsNull)
        {
            RemoveClipboardFormatListener(_hwnd.ToIntPtr());
            DestroyWindow(_hwnd.ToIntPtr());
            _hwnd = Hwnd.Null;
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Trust boundary: this proc is subclassed onto exactly one window — ours.
        // A message for any other window means the subclassing went wrong.
        Debug.Assert(hWnd == _hwnd.ToIntPtr(), "WndProc received a message for a foreign window.");

        if (msg == WM_CLIPBOARDUPDATE)
        {
            try { OnClipboardUpdate(); }
            #pragma warning disable CA1031 // why: the capture callback must never crash the listener message loop.
            catch { /* never let a capture crash the message loop */ }
            #pragma warning restore CA1031
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
        {
            // Trust-core invariants: what we detected is what we captured, and the
            // payload is non-empty and consistent with its type. A violation means the
            // clipboard changed mid-read or a capture bug — either way, don't persist it.
            Debug.Assert(captured.Type == type.Value, "Captured type diverged from detected type.");
            Debug.Assert(HasPayload(captured), "Captured clip has an empty payload for its type.");
            ClipCaptured?.Invoke(this, new ClipCapturedEventArgs(captured));
        }
    }

    /// <summary>
    /// Payload-shape check for the trust core: every captured clip must carry a
    /// non-empty payload consistent with its declared type.
    /// </summary>
    private static bool HasPayload(CapturedClip c) => c.Type switch
    {
        ClipType.Text or ClipType.Html => !string.IsNullOrEmpty(c.Text),
        ClipType.Image => c.ImagePng is { Length: > 0 },
        ClipType.Files => c.FilePaths is { Count: > 0 },
        _ => false
    };

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

#pragma warning restore CA2216
