using System.Runtime.InteropServices;

// Interop-surface suppressions (deliberate, documented):
// CA1401: this class IS the centralized Win32 interop surface, consumed by ClipForge.App — visibility is by design.
// CA1707: constant identifiers mirror Win32 API names (WM_*, CF_*, GWLP_*) so they map 1:1 to platform docs.
// CA1034: interop structs nest in the native-methods class by convention; they form no public API hierarchy.
// CA1815: interop structs are marshaled by layout and never compared; synthesizing value equality is noise.
#pragma warning disable CA1401
#pragma warning disable CA1707
#pragma warning disable CA1034
#pragma warning disable CA1815

namespace ClipForge.Core.Native;

/// <summary>Raw Win32 P/Invoke surface. Windows-only at runtime; compiles anywhere.</summary>
public static class NativeMethods
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_QUIT = 0x0012;
    public const int WM_DESTROY = 0x0002;
    public const int GWLP_WNDPROC = -4;

    public const uint CF_UNICODETEXT = 13;
    public const uint CF_BITMAP = 2;
    public const uint CF_DIB = 8;
    public const uint CF_DIBV5 = 17;
    public const uint CF_HDROP = 15;

    public const uint GMEM_MOVEABLE = 0x0002;

    public static readonly IntPtr HWND_MESSAGE = new(-3);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    // --- window / message loop ---

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    public static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    // --- clipboard listener ---

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    // --- clipboard data ---

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseClipboard();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EmptyClipboard();

    // Bumps every time clipboard content changes. Lets paste-restore verify that the
    // clipboard still holds what we staged before putting the previous content back.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern uint GetClipboardSequenceNumber();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetClipboardData(uint uFormat);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterClipboardFormatW(string lpszFormat);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, [In, Out] char[] lpszFile, uint cch);

    // --- global memory ---

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalFree(IntPtr hMem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll")]
    public static extern UIntPtr GlobalSize(IntPtr hMem);

    // --- foreground app ---

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // --- hotkey ---

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // --- synthetic paste ---

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeInput
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg; public ushort wParamL; public ushort wParamH;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, NativeInput[] pInputs, int cbSize);

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_V = 0x56;
}

#pragma warning restore CA1815
#pragma warning restore CA1034
#pragma warning restore CA1707
#pragma warning restore CA1401
