using ClipForge.Core;

using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// IsEmptyTextCapture — the gate added by d7b5f8a after the human test found
/// blank history rows: a text-shaped capture with no readable text must be
/// refused at the capture boundary instead of stored as an empty row.
/// (Post-fix API: this file is excluded from the pre-fix regression run.)
/// </summary>
public sealed class CapturedClipEmptyGateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] OneFile = { "/tmp/a.txt" };

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n ")]
    public void WhitespaceOnlyTextIsEmpty(string text)
    {
        var cap = new CapturedClip(ClipType.Text, text, null, null, "notepad", null, null, T0);

        Assert.True(cap.IsEmptyTextCapture);
    }

    [Fact]
    public void RealTextIsNotEmpty()
    {
        var cap = new CapturedClip(ClipType.Text, "a", null, null, "notepad", null, null, T0);

        Assert.False(cap.IsEmptyTextCapture);
    }

    [Fact]
    public void RtfEmptyParagraphIsEmpty()
    {
        // The exact capture shape behind the blank-entry finding.
        var cap = new CapturedClip(ClipType.Rtf, null, null, null, "WordPad", null, null, T0,
            @"{\rtf1\ansi\pard\par}");

        Assert.True(cap.IsEmptyTextCapture);
    }

    [Fact]
    public void RtfMarkupOnlyIsEmpty()
    {
        var cap = new CapturedClip(ClipType.Rtf, null, null, null, "WordPad", null, null, T0,
            @"{\rtf1\ansi\deff0}");

        Assert.True(cap.IsEmptyTextCapture);
    }

    [Fact]
    public void RtfWhitespaceCompanionIsEmpty()
    {
        // A whitespace companion wins over real markup for the preview, so the
        // capture reads as empty and is refused.
        var cap = new CapturedClip(ClipType.Rtf, " ", null, null, "WordPad", null, null, T0,
            @"{\rtf1\ansi some text}");

        Assert.True(cap.IsEmptyTextCapture);
    }

    [Fact]
    public void RtfWithTextIsNotEmpty()
    {
        var cap = new CapturedClip(ClipType.Rtf, null, null, null, "WordPad", null, null, T0,
            @"{\rtf1\ansi x}");

        Assert.False(cap.IsEmptyTextCapture);
    }

    [Theory]
    [InlineData("<p> </p>", true)]
    [InlineData("<p>hi</p>", false)]
    public void HtmlEmptinessFollowsVisibleText(string html, bool expected)
    {
        var cap = new CapturedClip(ClipType.Html, html, null, null, "chrome", null, null, T0);

        Assert.Equal(expected, cap.IsEmptyTextCapture);
    }

    [Fact]
    public void NonTextTypesAreNeverEmptyCaptures()
    {
        var image = new CapturedClip(ClipType.Image, null, new byte[] { 1 }, null, "paint", 4, 4, T0);
        var files = new CapturedClip(ClipType.Files, null, null, OneFile, "explorer", null, null, T0);

        Assert.False(image.IsEmptyTextCapture);
        Assert.False(files.IsEmptyTextCapture);
    }
}
