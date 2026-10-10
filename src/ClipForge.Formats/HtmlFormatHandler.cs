using System.Runtime.Versioning;
using ClipForge.Core;
using ClipForge.Core.Native;

namespace ClipForge.Formats;

/// <summary>HTML Format (registered "HTML Format") handler. Priority 20.</summary>
[SupportedOSPlatform("windows")]
public sealed class HtmlFormatHandler : IClipFormatHandler
{
    public string Name => "Html";
    public int Priority => 20;

    public bool CanHandle() => WindowsClipboard.IsHtmlAvailable();

    public CapturedClip? Capture()
    {
        if (!WindowsClipboard.TryGetHtml(out var html) || string.IsNullOrWhiteSpace(html)) return null;
        // rule 7: never DateTimeOffset.Now as a time source.
        return new CapturedClip(ClipType.Html, html, null, null, ForegroundApp.GetProcessName(),
            null, null, DateTimeOffset.UtcNow);
    }
}
