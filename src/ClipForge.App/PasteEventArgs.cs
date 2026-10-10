using ClipForge.Core;

namespace ClipForge.App;

/// <summary>Event data for <see cref="OverlayWindow.PasteRequested"/>.</summary>
public sealed class PasteEventArgs(Clip clip, bool plainText = false) : EventArgs
{
    public Clip Clip { get; } = clip;

    /// <summary>True when the user asked for paste-as-plain-text (formatting stripped).</summary>
    public bool PlainText { get; } = plainText;
}
