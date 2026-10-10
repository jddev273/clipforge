namespace ClipForge.Core;

/// <summary>Event data for <see cref="ClipboardListener.ClipCaptured"/>.</summary>
public sealed class ClipCapturedEventArgs(CapturedClip captured) : EventArgs
{
    public CapturedClip Captured { get; } = captured;
}
