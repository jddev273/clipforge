using ClipForge.Core;

namespace ClipForge.App;

/// <summary>Event data for <see cref="OverlayWindow.PasteRequested"/>.</summary>
public sealed class PasteEventArgs(Clip clip) : EventArgs
{
    public Clip Clip { get; } = clip;
}
