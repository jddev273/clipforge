using System.Globalization;
using ClipForge.Core;

namespace ClipForge.App;

/// <summary>Presentation model for one row in the overlay list.</summary>
public sealed class ClipItem
{
    public Clip Clip { get; }
    public string Glyph { get; }
    public string Preview { get; }
    public string Meta { get; }

    public ClipItem(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        Clip = clip;
        Glyph = clip.Type switch
        {
            ClipType.Text => "\U0001F4C4",   // 📄
            ClipType.Html => "\U0001F310",   // 🌐
            ClipType.Rtf => "\U0001F4DD",    // 📝
            ClipType.Image => "\U0001F5BC",  // 🖼
            ClipType.Files => "\U0001F4C1",  // 📁
            // Mandatory discard (unnamed enum values); unreachable from valid producers — fail fast.
            _ => throw new InvalidOperationException($"Invalid ClipType value: {(int)clip.Type}.")
        };
        Preview = clip.PreviewText;
        Meta = $"{clip.SourceApp ?? "Unknown app"} · {Ago(clip.CreatedAt)}";
    }

    private static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.UtcNow - t; // rule 7: derive display time from UtcNow, never Now
        if (d < TimeSpan.FromMinutes(1)) return "just now";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes}m ago";
        if (d < TimeSpan.FromDays(1)) return $"{(int)d.TotalHours}h ago";
        if (d < TimeSpan.FromDays(7)) return $"{(int)d.TotalDays}d ago";
        // User-facing date: honor the user's locale for month abbreviations.
        return t.ToString("MMM d", CultureInfo.CurrentCulture);
    }
}
