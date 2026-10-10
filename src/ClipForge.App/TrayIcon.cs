using System.Runtime.InteropServices;
using System.Windows;
using ClipForge.Core.Native;
using static ClipForge.Core.Native.NativeMethods;

// CA2216: the message-only window must be torn down on the UI thread that pumps its
// messages (the WPF dispatcher). A finalizer would destroy it on the GC thread.
#pragma warning disable CA2216

namespace ClipForge.App;

/// <summary>
/// System-tray icon (pure Win32, no WinForms dependency): double-click summons the
/// overlay, right-click shows Show / Exit. Never nags — no balloons, ever.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint NIM_ADD = 0x0;
    private const uint NIM_DELETE = 0x2;
    private const uint NIF_MESSAGE = 0x1;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;
    private const uint WM_TRAY = 0x8001;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y,
        int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    private readonly WindowProc _wndProc;
    private readonly Action _showAction;
    private readonly Action _exitAction;
    private readonly Hwnd _hwnd;
    private readonly uint _taskbarCreatedMsg;
    private readonly System.Drawing.Icon _icon; // kept alive while the tray icon is registered
    private bool _disposed;

    public TrayIcon(Action showAction, Action exitAction)
    {
        ArgumentNullException.ThrowIfNull(showAction);
        ArgumentNullException.ThrowIfNull(exitAction);
        _showAction = showAction;
        _exitAction = exitAction;
        _wndProc = WndProc;
        _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");
        _icon = LoadAppIcon();

        // Message-only window on the UI thread; the WPF dispatcher pumps messages.
        _hwnd = Hwnd.FromIntPtr(CreateWindowExW(0, "STATIC", "ClipForgeTray", 0, 0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        SetWindowLongPtr(_hwnd.ToIntPtr(), GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));

        AddIcon();
    }

    /// <summary>
    /// Loads the branded icon from the embedded WPF resource. A missing resource is a
    /// packaging bug — fail fast rather than silently showing the default Windows icon.
    /// </summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        var resource = Application.GetResourceStream(
            new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute));
        if (resource?.Stream is null)
            throw new InvalidOperationException("ClipForge: embedded resource 'Resources/app.ico' is missing.");
        // Icon(Stream) reads the stream eagerly; the framework owns the resource stream lifetime.
        return new System.Drawing.Icon(resource.Stream);
    }

    private void AddIcon()
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd.ToIntPtr(),
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _icon.Handle, // branded icon; the Icon stays alive for the tray lifetime
            szTip = "ClipForge — Ctrl+Shift+V"
        };
        Shell_NotifyIconW(NIM_ADD, ref data);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreatedMsg)
        {
            AddIcon(); // explorer restarted — put the icon back
            return IntPtr.Zero;
        }
        if (msg == WM_TRAY)
        {
            int evt = lParam.ToInt32();
            if (evt == WM_LBUTTONDBLCLK) _showAction();
            else if (evt == WM_RBUTTONUP) ShowMenu();
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenuW(menu, 0, (UIntPtr)1, "Show ClipForge");
            AppendMenuW(menu, 0, (UIntPtr)2, "E&xit");
            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd.ToIntPtr()); // required so the menu dismisses correctly
            uint cmd = TrackPopupMenu(menu, 0x0100 | 0x0002, pt.X, pt.Y, 0, _hwnd.ToIntPtr(), IntPtr.Zero);
            if (cmd == 1) _showAction();
            else if (cmd == 2) _exitAction();
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd.ToIntPtr(),
            uID = 1
        };
        Shell_NotifyIconW(NIM_DELETE, ref data);
        DestroyWindow(_hwnd.ToIntPtr());
        _icon.Dispose();
    }
}

#pragma warning restore CA2216
