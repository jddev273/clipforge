namespace ClipForge.Core;

/// <summary>
/// Structural parsing of hotkey chord text ("Ctrl+Shift+V") — the part of chord
/// handling that needs no UI framework: split on '+', every segment but the last
/// must name a modifier (synonyms included), the last segment is the key name.
/// </summary>
public static class HotkeyChordParser
{
    // Key names that can never be the key half of a chord: they name modifier
    // keys, and a chord needs a non-modifier key (the app rejects them too,
    // via its IsModifierKey check on the parsed WPF Key).
    private static readonly string[] ModifierKeyNames =
    {
        "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt",
        "LeftShift", "RightShift", "LWin", "RWin"
    };

    public static bool TryParse(string? text, out ParsedHotkeyChord? chord)
    {
        chord = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false; // at least one modifier + a key

        var modifiers = HotkeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            HotkeyModifiers? m = parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => null, // unknown modifier name
            };
            if (m is null) return false;
            modifiers |= m.Value;
        }

        string keyName = parts[^1];
        foreach (var name in ModifierKeyNames)
        {
            if (string.Equals(keyName, name, StringComparison.OrdinalIgnoreCase)) return false;
        }

        chord = new ParsedHotkeyChord(modifiers, keyName);
        return true;
    }

    /// <summary>
    /// Parse-level equivalence of two chord texts: same modifiers and the same
    /// key name up to case. This is the semantic the settings-save path needs
    /// ("did the chord actually change?") — text drift like "CONTROL+SHIFT+V"
    /// vs "Ctrl+Shift+V" parses to the same chord. Registration identity (the
    /// Win32 virtual-key code) is resolved by the app, not here.
    /// </summary>
    public static bool AreEquivalent(string? a, string? b) =>
        TryParse(a, out var chordA) && chordA is not null &&
        TryParse(b, out var chordB) && chordB is not null &&
        chordA.Modifiers == chordB.Modifiers &&
        string.Equals(chordA.KeyName, chordB.KeyName, StringComparison.OrdinalIgnoreCase);
}
