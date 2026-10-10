using System.Runtime.Versioning;
using ClipForge.Core;
using ClipForge.Core.Native;

namespace ClipForge.Formats;

/// <summary>CF_HDROP (file list) handler. Priority 0 — files win over all other formats.</summary>
[SupportedOSPlatform("windows")]
public sealed class FilesFormatHandler : IClipFormatHandler
{
    public string Name => "Files";
    public int Priority => 0;

    public bool CanHandle() => WindowsClipboard.IsFilesAvailable();

    public CapturedClip? Capture()
    {
        if (!WindowsClipboard.TryGetFileList(out var paths)) return null;
        // rule 7: never DateTimeOffset.Now as a time source.
        return new CapturedClip(ClipType.Files, null, null, paths, ForegroundApp.GetProcessName(),
            null, null, DateTimeOffset.UtcNow);
    }
}
