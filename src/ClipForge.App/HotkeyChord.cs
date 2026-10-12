using System.Windows.Input;

using ClipForge.Core;

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
        // Structure + modifiers are parsed in Core (HotkeyChordParser —
        // framework-free, unit-tested). This class stays the authority on
        // which key NAMES are real WPF keys, so accept/reject behavior is
        // unchanged: everything the parser passes still faces the same
        // Key parse and modifier-key check below.
        if (!HotkeyChordParser.TryParse(s, out var parsed) || parsed is null) return false;
        if (!Enum.TryParse(parsed.KeyName, ignoreCase: true, out Key key)) return false;
        if (IsModifierKey(key)) return false;
        try
        {
            chord = new HotkeyChord(ToModifierKeys(parsed.Modifiers), key);
        }
        catch (ArgumentException)
        {
            return false;
        }
        return true;
    }

    private static ModifierKeys ToModifierKeys(HotkeyModifiers modifiers)
    {
        ModifierKeys m = ModifierKeys.None;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) m |= ModifierKeys.Alt;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) m |= ModifierKeys.Control;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) m |= ModifierKeys.Shift;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) m |= ModifierKeys.Windows;
        return m;
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
