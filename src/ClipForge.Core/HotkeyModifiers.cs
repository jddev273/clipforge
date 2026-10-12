namespace ClipForge.Core;

/// <summary>
/// Modifier half of a global hotkey chord, free of any UI framework.
/// Values match the Win32 MOD_* flags (and WPF's ModifierKeys) so the app can
/// map them 1:1 at the registration boundary.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,     // MOD_ALT
    Control = 2, // MOD_CONTROL
    Shift = 4,   // MOD_SHIFT
    Windows = 8  // MOD_WIN
}
