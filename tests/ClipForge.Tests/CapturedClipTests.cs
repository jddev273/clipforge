using ClipForge.Core;

using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// CapturedClip behavior outside the RTF path: preview shaping, content
/// hashing (duplicate suppression), and the ToClip mapping into storage.
/// </summary>
public sealed class CapturedClipTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] ThreeFiles = { "/tmp/a.txt", "/tmp/b.txt", "/tmp/c.txt" };
    private static readonly string[] OneFile = { "/tmp/only.txt" };

    private static CapturedClip Text(string text) =>
        new(ClipType.Text, text, null, null, "notepad", null, null, T0);

    [Fact]
    public void TextClipToClipMapsAllFields()
    {
        var clip = Text("hello world").ToClip();

        Assert.Equal(ClipType.Text, clip.Type);
        Assert.Equal("hello world", clip.Text);
        Assert.Equal("hello world", clip.PreviewText);
        Assert.Null(clip.RtfText);
        Assert.Null(clip.ImageData);
        Assert.Equal("notepad", clip.SourceApp);
        Assert.Equal(T0, clip.CreatedAt);
        Assert.False(clip.Pinned);
        Assert.Equal(ClipId.None, clip.Id);
    }

    [Fact]
    public void PreviewCollapsesWhitespace()
    {
        Assert.Equal("a b c", Text("a  b\n\tc").PreviewText);
    }

    [Fact]
    public void PreviewTruncatesAt200Chars()
    {
        string preview = Text(new string('x', 250)).PreviewText;

        Assert.Equal(201, preview.Length);
        Assert.EndsWith("\u2026", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlPreviewStripsTags()
    {
        var cap = new CapturedClip(ClipType.Html, "<p>Hello <b>world</b></p>", null, null, "chrome", null, null, T0);

        Assert.Equal("Hello world", cap.PreviewText);
        // The scan text for non-RTF clips is the payload itself.
        Assert.Equal("<p>Hello <b>world</b></p>", cap.SecretScanText);
    }

    [Fact]
    public void FilesClipJoinsPathsAndPreviewsNames()
    {
        var cap = new CapturedClip(ClipType.Files, null, null,
            ThreeFiles, "explorer", null, null, T0);

        var clip = cap.ToClip();
        Assert.Equal("/tmp/a.txt\n/tmp/b.txt\n/tmp/c.txt", clip.Text);
        Assert.Equal("3 files: a.txt, b.txt, +1 more", clip.PreviewText);
        Assert.Equal(3, clip.FilePaths.Count);
    }

    [Fact]
    public void FilesClipSingleFilePreview()
    {
        var cap = new CapturedClip(ClipType.Files, null, null,
            OneFile, "explorer", null, null, T0);

        Assert.Equal("1 file: only.txt", cap.PreviewText);
    }

    [Fact]
    public void ImageClipPreviewShowsDimensions()
    {
        var png = new byte[] { 1, 2, 3 };
        var cap = new CapturedClip(ClipType.Image, null, png, null, "paint", 800, 600, T0);

        Assert.Equal("Image 800\u00d7600", cap.PreviewText);
        var clip = cap.ToClip();
        Assert.Null(clip.Text);
        Assert.Equal(png, clip.ImageData);
    }

    [Fact]
    public void ImageClipWithoutDimensionsPreviewsAsImage()
    {
        var cap = new CapturedClip(ClipType.Image, null, new byte[] { 1 }, null, "paint", null, null, T0);

        Assert.Equal("Image", cap.PreviewText);
    }

    [Fact]
    public void ContentHashIsDeterministicAndTypePrefixed()
    {
        var a = Text("same text");
        var b = Text("same text");
        var c = Text("other text");

        Assert.Equal(a.ContentHash, b.ContentHash, StringComparer.Ordinal);
        Assert.NotEqual(a.ContentHash, c.ContentHash, StringComparer.Ordinal);
        Assert.StartsWith("Text:", a.ContentHash, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentHashRtfHashesTheMarkupNotTheCompanion()
    {
        const string rtf1 = @"{\rtf1\ansi one}";
        const string rtf2 = @"{\rtf1\ansi two}";
        var a = new CapturedClip(ClipType.Rtf, "companion", null, null, "WordPad", null, null, T0, rtf1);
        var b = new CapturedClip(ClipType.Rtf, "companion", null, null, "WordPad", null, null, T0, rtf2);

        Assert.StartsWith("Rtf:", a.ContentHash, StringComparison.Ordinal);
        Assert.NotEqual(a.ContentHash, b.ContentHash, StringComparer.Ordinal);
    }
}
