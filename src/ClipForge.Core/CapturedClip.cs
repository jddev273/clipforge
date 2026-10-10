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
    byte[]? ImagePng,
    string[]? FilePaths,
    string? SourceApp,
    int? ImageWidth,
    int? ImageHeight,
    DateTimeOffset CapturedAt)
{
    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);

    public string PreviewText => Type switch
    {
        ClipType.Text => Truncate(CollapseWhitespace(Text ?? string.Empty)),
        ClipType.Html => Truncate(CollapseWhitespace(HtmlTag.Replace(Text ?? string.Empty, " "))),
        ClipType.Files => FilePreview(FilePaths),
        ClipType.Image => ImageWidth.HasValue ? $"Image {ImageWidth}\u00d7{ImageHeight}" : "Image",
        _ => string.Empty
    };

    public string ContentHash
    {
        get
        {
            byte[] payload = Type switch
            {
                ClipType.Image => ImagePng ?? Array.Empty<byte>(),
                ClipType.Files => Encoding.UTF8.GetBytes(string.Join('\n', FilePaths ?? Array.Empty<string>())),
                _ => Encoding.UTF8.GetBytes(Text ?? string.Empty)
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
            _ => Text
        },
        ImageData = Type == ClipType.Image ? ImagePng : null,
        PreviewText = PreviewText,
        SourceApp = SourceApp,
        CreatedAt = CapturedAt,
        Pinned = false,
        ContentHash = ContentHash
    };

    private static string FilePreview(string[]? paths)
    {
        if (paths is null || paths.Length == 0) return "Files";
        var names = paths.Select(Path.GetFileName).Take(2);
        var more = paths.Length > 2 ? $", +{paths.Length - 2} more" : string.Empty;
        return $"{paths.Length} file{(paths.Length == 1 ? string.Empty : "s")}: {string.Join(", ", names)}{more}";
    }

    private static string CollapseWhitespace(string s) =>
        Regex.Replace(s, @"\s+", " ").Trim();

    private static string Truncate(string s, int max = 200) =>
        s.Length <= max ? s : s[..max].TrimEnd() + "\u2026";
}
