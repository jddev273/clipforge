namespace ClipForge.Core;

/// <summary>
/// A structurally parsed hotkey chord: the modifiers plus the key NAME as written
/// ("V", "F5", "Return"). Whether the name is a real key is the caller's call —
/// the app resolves it against WPF's <c>Key</c> enum, the only place that
/// vocabulary exists.
/// </summary>
public sealed record ParsedHotkeyChord(HotkeyModifiers Modifiers, string KeyName);
