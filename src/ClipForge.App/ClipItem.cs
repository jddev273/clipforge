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
        Clip = clip;
        Glyph = clip.Type switch
        {
            ClipType.Text => "\U0001F4C4",   // 📄
            ClipType.Html => "\U0001F310",   // 🌐
            ClipType.Image => "\U0001F5BC",  // 🖼
            ClipType.Files => "\U0001F4C1",  // 📁
            _ => "\U0001F4C4"
        };
        Preview = clip.PreviewText;
        Meta = $"{clip.SourceApp ?? "Unknown app"} · {Ago(clip.CreatedAt)}";
    }

    private static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.Now - t;
        if (d < TimeSpan.FromMinutes(1)) return "just now";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes}m ago";
        if (d < TimeSpan.FromDays(1)) return $"{(int)d.TotalHours}h ago";
        if (d < TimeSpan.FromDays(7)) return $"{(int)d.TotalDays}d ago";
        return t.ToString("MMM d");
    }
}
