using System.Windows.Input;

namespace ClipForge.App;

/// <summary>A global hotkey chord: modifiers + one key. Parses and formats "Ctrl+Shift+V".</summary>
public sealed class HotkeyChord
{
    public ModifierKeys Modifiers { get; }
    public Key Key { get; }

    public HotkeyChord(ModifierKeys modifiers, Key key)
    {
        if (modifiers == ModifierKeys.None)
            throw new ArgumentException("A global hotkey needs at least one modifier.", nameof(modifiers));
        if (IsModifierKey(key))
            throw new ArgumentException("The hotkey needs a non-modifier key.", nameof(key));
        Modifiers = modifiers;
        Key = key;
    }

    public static bool TryParse(string? s, out HotkeyChord? chord)
    {
        chord = null;
        if (string.IsNullOrWhiteSpace(s)) return false;
        string[] parts = s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false; // at least one modifier + a key
        ModifierKeys mods = ModifierKeys.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            ModifierKeys m = parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ModifierKeys.Control,
                "ALT" => ModifierKeys.Alt,
                "SHIFT" => ModifierKeys.Shift,
                "WIN" or "WINDOWS" => ModifierKeys.Windows,
                _ => (ModifierKeys)(-1), // sentinel: unknown modifier name
            };
            if (m == (ModifierKeys)(-1)) return false;
            mods |= m;
        }
        if (!Enum.TryParse(parts[^1], ignoreCase: true, out Key key)) return false;
        if (IsModifierKey(key)) return false;
        try
        {
            chord = new HotkeyChord(mods, key);
        }
        catch (ArgumentException)
        {
            return false;
        }
        return true;
    }

    /// <summary>Win32 MOD_* flags and virtual-key code for RegisterHotKey.</summary>
    public (uint modifiers, uint vk) ToWin32()
    {
        uint m = 0;
        if (Modifiers.HasFlag(ModifierKeys.Alt)) m |= 0x0001;     // MOD_ALT
        if (Modifiers.HasFlag(ModifierKeys.Control)) m |= 0x0002; // MOD_CONTROL
        if (Modifiers.HasFlag(ModifierKeys.Shift)) m |= 0x0004;   // MOD_SHIFT
        if (Modifiers.HasFlag(ModifierKeys.Windows)) m |= 0x0008; // MOD_WIN
        return (m, (uint)KeyInterop.VirtualKeyFromKey(Key));
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}
