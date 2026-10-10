using System.Runtime.Versioning;
using ClipForge.Core;
using ClipForge.Core.Native;

namespace ClipForge.Formats;

/// <summary>
/// "Rich Text Format" (registered format) handler. Priority 15 — richer than HTML,
/// below image/files. Captures the RTF markup plus the plain-text companion when the
/// source also exposes CF_UNICODETEXT (Word et al. do), so preview, search and
/// paste-as-plain-text keep working.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RtfFormatHandler : IClipFormatHandler
{
    public string Name => "Rtf";
    public int Priority => 15;

    public bool CanHandle() => WindowsClipboard.IsRtfAvailable();

    public CapturedClip? Capture()
    {
        if (!WindowsClipboard.TryGetRtf(out var rtf) || string.IsNullOrEmpty(rtf)) return null;
        WindowsClipboard.TryGetText(out var text);
        // rule 7: never DateTimeOffset.Now as a time source.
        return new CapturedClip(ClipType.Rtf, text, null, null, ForegroundApp.GetProcessName(),
            null, null, DateTimeOffset.UtcNow, rtf);
    }
}
