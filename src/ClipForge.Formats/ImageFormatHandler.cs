using System.Runtime.Versioning;
using ClipForge.Core;
using ClipForge.Core.Native;

namespace ClipForge.Formats;

/// <summary>CF_DIB/CF_DIBV5 (image) handler. Priority 10.</summary>
[SupportedOSPlatform("windows")]
public sealed class ImageFormatHandler : IClipFormatHandler
{
    public string Name => "Image";
    public int Priority => 10;

    public bool CanHandle() => WindowsClipboard.IsImageAvailable();

    public CapturedClip? Capture()
    {
        if (!WindowsClipboard.TryGetImage(out var png, out int w, out int h)) return null;
        // rule 7: never DateTimeOffset.Now as a time source.
        return new CapturedClip(ClipType.Image, null, png, null, ForegroundApp.GetProcessName(),
            w, h, DateTimeOffset.UtcNow);
    }
}
