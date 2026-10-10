using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipForge.Core;

/// <summary>
/// Raw payload produced by <see cref="ClipboardListener"/> before persistence.
/// </summary>
public sealed record CapturedClip(
    ClipType Type,
    string? Text,
    // CA1819 suppressed by design: immutable payload snapshot, never mutated after capture.
#pragma warning disable CA1819
    byte[]? ImagePng,
#pragma warning restore CA1819
    IReadOnlyList<string>? FilePaths,
    string? SourceApp,
    int? ImageWidth,
    int? ImageHeight,
    DateTimeOffset CapturedAt)
{
    private static readonly Regex HtmlTag =
        // MA0009: match timeout bounds catastrophic backtracking on hostile input.
        new("<[^>]+>", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Corruption canary for the switch discard arms. The compiler forces us to handle
    /// unnamed enum values; every producer only emits named members, so this is unreachable
    /// unless a value was corrupted (e.g. a bad cast from storage). Generic so the discard
    /// arms stay expression-shaped without a <c>throw</c> directly in a property getter.
    /// </summary>
    private T UnknownClipType<T>() =>
        throw new InvalidOperationException($"Invalid ClipType value: {(int)Type}.");

    public string PreviewText => Type switch
    {
        ClipType.Text => Truncate(CollapseWhitespace(Text ?? string.Empty)),
        ClipType.Html => Truncate(CollapseWhitespace(HtmlTag.Replace(Text ?? string.Empty, " "))),
        ClipType.Files => FilePreview(FilePaths),
        ClipType.Image => ImageWidth.HasValue ? $"Image {ImageWidth}\u00d7{ImageHeight}" : "Image",
        // The discard arm is mandatory: the compiler must account for unnamed enum values
        // ((ClipType)4). All producers are internal and only emit named members, so reaching
        // it means corruption — fail fast instead of silently misbehaving.
        _ => UnknownClipType<string>()
    };

    public string ContentHash
    {
        get
        {
            byte[] payload = Type switch
            {
                ClipType.Image => ImagePng ?? Array.Empty<byte>(),
                ClipType.Files => Encoding.UTF8.GetBytes(string.Join('\n', FilePaths ?? Array.Empty<string>())),
                ClipType.Text or ClipType.Html => Encoding.UTF8.GetBytes(Text ?? string.Empty),
                _ => UnknownClipType<byte[]>() // see note on PreviewText: corruption canary
            };
            var hash = SHA256.HashData(payload);
            return $"{Type}:{Convert.ToHexString(hash)}";
        }
    }

    public Clip ToClip() => new()
    {
        Type = Type,
        Text = Type switch
        {
            ClipType.Files => string.Join('\n', FilePaths ?? Array.Empty<string>()),
            ClipType.Image => null,
            ClipType.Text or ClipType.Html => Text,
            _ => UnknownClipType<string?>() // see note on PreviewText: corruption canary
        },
        ImageData = Type == ClipType.Image ? ImagePng : null,
        PreviewText = PreviewText,
        SourceApp = SourceApp,
        CreatedAt = CapturedAt,
        Pinned = false,
        ContentHash = ContentHash
    };

    private static string FilePreview(IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count == 0) return "Files";
        var names = paths.Select(Path.GetFileName).Take(2);
        var more = paths.Count > 2 ? $", +{paths.Count - 2} more" : string.Empty;
        return $"{paths.Count} file{(paths.Count == 1 ? string.Empty : "s")}: {string.Join(", ", names)}{more}";
    }

    private static string CollapseWhitespace(string s) =>
        // MA0009: match timeout on the static overload too.
        Regex.Replace(s, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();

    private static string Truncate(string s, int max = 200) =>
        s.Length <= max ? s : s[..max].TrimEnd() + "\u2026";
}
