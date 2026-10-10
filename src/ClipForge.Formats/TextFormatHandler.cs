using System.Runtime.Versioning;
using ClipForge.Core;
using ClipForge.Core.Native;

namespace ClipForge.Formats;

/// <summary>CF_UNICODETEXT handler. Priority 30 — the fallback when nothing richer is present.</summary>
[SupportedOSPlatform("windows")]
public sealed class TextFormatHandler : IClipFormatHandler
{
    public string Name => "Text";
    public int Priority => 30;

    public bool CanHandle() => WindowsClipboard.IsTextAvailable();

    public CapturedClip? Capture()
    {
        if (!WindowsClipboard.TryGetText(out var text) || string.IsNullOrEmpty(text)) return null;
        // rule 7: never DateTimeOffset.Now as a time source.
        return new CapturedClip(ClipType.Text, text, null, null, ForegroundApp.GetProcessName(),
            null, null, DateTimeOffset.UtcNow);
    }
}
