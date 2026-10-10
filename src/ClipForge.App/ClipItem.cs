using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipForge.Core;

namespace ClipForge.App;

/// <summary>Presentation model for one row in the overlay list.</summary>
public sealed class ClipItem
{
    // Thumbnail cache: rows are rebuilt on every keystroke, so without this each
    // keystroke would re-decode every visible PNG. Keyed by content hash, capped.
    private static readonly ConcurrentDictionary<string, ImageSource> ThumbnailCache =
        new(StringComparer.Ordinal);
    private const int ThumbnailCacheCap = 200;
    private const int ThumbnailWidth = 160;

    public Clip Clip { get; }
    public string Glyph { get; }
    public string Preview { get; }
    public string Meta { get; }

    /// <summary>Decoded thumbnail for image clips; null for everything else (or corrupt PNGs).</summary>
    public ImageSource? Thumbnail { get; }

    public System.Windows.Visibility GlyphVisibility =>
        Thumbnail is null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility ThumbnailVisibility =>
        Thumbnail is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    /// <summary>Group key for the overlay list: pinned clips form their own section on top.</summary>
    public int PinnedGroup => Clip.Pinned ? 0 : 1;

    /// <summary>Pin button: full opacity when pinned, dimmed when not.</summary>
    public double PinOpacity => Clip.Pinned ? 1.0 : 0.3;

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
        Thumbnail = clip.Type == ClipType.Image ? GetThumbnail(clip) : null;
    }

    private static ImageSource? GetThumbnail(Clip clip)
    {
        if (clip.ImageData is not { Length: > 0 }) return null;
        if (ThumbnailCache.TryGetValue(clip.ContentHash, out var cached)) return cached;
        var thumb = DecodeThumbnail(clip.ImageData);
        if (thumb is null) return null;
        if (ThumbnailCache.Count >= ThumbnailCacheCap) ThumbnailCache.Clear();
        ThumbnailCache[clip.ContentHash] = thumb;
        return thumb;
    }

    /// <summary>Decodes a PNG to a frozen thumbnail. Corrupt data degrades to the glyph.</summary>
    private static BitmapImage? DecodeThumbnail(byte[] png)
    {
        try
        {
            var image = new BitmapImage();
            using var ms = new MemoryStream(png);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // decode now; the stream can close
            image.DecodePixelWidth = ThumbnailWidth;      // decode at thumbnail size, not full-res
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze(); // cross-thread safe (built on the UI thread today, frozen for safety)
            return image;
        }
        // CA1031: a corrupt PNG must degrade to the glyph, never crash the overlay.
        #pragma warning disable CA1031
        catch (Exception ex)
        {
            Debug.WriteLine($"ClipForge: thumbnail decode failed: {ex.Message}");
            return null;
        }
        #pragma warning restore CA1031
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
