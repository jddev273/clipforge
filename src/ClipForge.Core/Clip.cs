namespace ClipForge.Core;

/// <summary>
/// A single clipboard history entry as stored in SQLite.
/// Text/Html/Files payloads live in <see cref="Text"/> (files = newline-joined paths);
/// image payloads live in <see cref="ImageData"/> as PNG bytes.
/// </summary>
public sealed class Clip
{
    public long Id { get; set; }
    public ClipType Type { get; set; }
    public string? Text { get; set; }
    public byte[]? ImageData { get; set; }
    public string PreviewText { get; set; } = string.Empty;
    public string? SourceApp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool Pinned { get; set; }
    /// <summary>SHA-256 of the payload, used for duplicate suppression.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>File paths for <see cref="ClipType.Files"/> clips.</summary>
    public string[] FilePaths =>
        Type == ClipType.Files && Text is not null
            ? Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
}
