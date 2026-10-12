namespace ClipForge.Core;

/// <summary>
/// Picks a capture from the format handlers: tried in Priority order, the
/// first handler that both claims the clipboard (CanHandle) and produces a
/// clip (Capture non-null) wins. A handler that claims but returns null does
/// NOT end the attempt — the clipboard usually offers several formats at
/// once, and one format failing to read must not lose the copy when a
/// lower-priority format would have read fine. Returns null only when no
/// handler produced anything; the caller may then retry the whole capture
/// once (a transiently locked clipboard is the usual cause).
/// </summary>
public static class FormatHandlerChain
{
    public static CapturedClip? CaptureFirst(IReadOnlyList<IClipFormatHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        foreach (var handler in handlers.OrderBy(static h => h.Priority))
        {
            if (!handler.CanHandle()) continue;
            var captured = handler.Capture();
            if (captured is not null) return captured;
            // Claimed but produced nothing: fall through to the next handler.
        }
        return null;
    }
}
