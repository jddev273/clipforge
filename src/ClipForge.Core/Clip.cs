namespace ClipForge.Core;

/// <summary>
/// A single clipboard history entry as stored in SQLite.
/// Text/Html/Files payloads live in <see cref="Text"/> (files = newline-joined paths);
/// image payloads live in <see cref="ImageData"/> as PNG bytes.
/// Mandatory state is <c>required</c> + <c>init</c>: a Clip cannot exist half-built.
/// <see cref="Id"/> is the deliberate exception — it is assigned by the store on insert,
/// so it stays settable (and is <see cref="ClipId.None"/> until then).
/// </summary>
public sealed class Clip
{
    public ClipId Id { get; set; } = ClipId.None;
    public required ClipType Type { get; init; }
    public string? Text { get; init; }
    // CA1819 suppressed by design: the payload is an immutable snapshot — captured once,
    // never mutated afterwards. Callers must treat it as read-only.
#pragma warning disable CA1819
    public byte[]? ImageData { get; init; }
#pragma warning restore CA1819
    public required string PreviewText { get; init; }
    public string? SourceApp { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public bool Pinned { get; init; }
    /// <summary>SHA-256 of the payload, used for duplicate suppression.</summary>
    public required string ContentHash { get; init; }

    /// <summary>File paths for <see cref="ClipType.Files"/> clips.</summary>
    public IReadOnlyList<string> FilePaths =>
        Type == ClipType.Files && Text is not null
            ? Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
}
