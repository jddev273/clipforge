using ClipForge.Core;
using ClipForge.Secrets;

using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// The RTF capture path end to end at the Core seam: what the secret filter
/// scans (SecretScanText) and what the detector does with it. The escaped
/// cases are regression tests for the 2026-10-12 human-test finding — RTF
/// editors that escape punctuation hid secrets from the scan while the
/// secret-bearing markup was stored verbatim.
/// </summary>
public sealed class CapturedClipRtfTests
{
    private static readonly SecretDetector Detector = new();
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    // WordPad-shaped RTF shell, mirroring real captures (see the fix probe).
    private const string Shell1 = @"{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang1033{\fonttbl{\f0\fnil\fcharset0 Calibri;}}";
    private const string Shell2 = @"{\colortbl ;\red0\green0\blue0;}{\*\generator Riched20 10.0.22621}\viewkind4\uc1 \pard\f0\fs22\lang9 ";
    private const string Shell3 = @"\par}";

    private static CapturedClip Rtf(string? companion, string rtf) =>
        new(ClipType.Rtf, companion, null, null, "WordPad", null, null, T0, rtf);

    private static string WordPadRtf(string body) => Shell1 + Shell2 + body + Shell3;

    [Fact]
    public void CompanionTextIsTheScanTextAndIsRefused()
    {
        var cap = Rtf("password: hunter2", WordPadRtf("password: hunter2"));

        Assert.Equal("password: hunter2", cap.SecretScanText);
        Assert.Equal(SecretKind.PasswordAssignment, Detector.Detect(cap.SecretScanText));
    }

    [Fact]
    public void LiteralTextRunsAreScannedAndRefused()
    {
        // No plain-text companion: the filter scans the markup's text runs.
        var cap = Rtf(null, WordPadRtf("password: hunter2"));

        Assert.Contains("password: hunter2", cap.SecretScanText, StringComparison.Ordinal);
        Assert.Equal(SecretKind.PasswordAssignment, Detector.Detect(cap.SecretScanText));
    }

    [Fact]
    public void HexEscapedColonIsDecodedBeforeScanningAndRefused()
    {
        // Regression (d7b5f8a): some editors escape the colon as \'3a. Before
        // the fix the scan saw "password\'3a hunter2", missed the assignment,
        // and the clip was stored with the secret inside its RTF payload.
        var cap = Rtf(null, WordPadRtf(@"password\'3a hunter2"));

        Assert.Contains("password: hunter2", cap.SecretScanText, StringComparison.Ordinal);
        Assert.Equal(SecretKind.PasswordAssignment, Detector.Detect(cap.SecretScanText));
    }

    [Fact]
    public void UnicodeEscapedColonIsDecodedBeforeScanningAndRefused()
    {
        // Regression (d7b5f8a): same escape trick, \u58 '?' fallback form.
        var cap = Rtf(null, WordPadRtf(@"password\u58?hunter2"));

        Assert.Contains("password:", cap.SecretScanText, StringComparison.Ordinal);
        Assert.Equal(SecretKind.PasswordAssignment, Detector.Detect(cap.SecretScanText));
    }

    [Fact]
    public void OrdinaryRtfIsNotRefused()
    {
        var cap = Rtf(null, WordPadRtf("Quarterly report draft"));

        Assert.Null(Detector.Detect(cap.SecretScanText));
        Assert.Contains("Quarterly report draft", cap.PreviewText, StringComparison.Ordinal);

        // The markup itself is the payload and is stored verbatim.
        var clip = cap.ToClip();
        Assert.Equal(cap.RtfText, clip.RtfText);
        Assert.Equal(ClipType.Rtf, clip.Type);
    }

    [Fact]
    public void HexEscapeIsDecodedInPreview()
    {
        var cap = Rtf(null, @"{\rtf1\ansi caf\'e9}");

        Assert.Equal("caf\u00e9", cap.PreviewText);
    }

    [Fact]
    public void UnicodeEscapeIsDecodedInPreview()
    {
        var cap = Rtf(null, @"{\rtf1\ansi caf\u233?}");

        Assert.Equal("caf\u00e9", cap.PreviewText);
    }

    [Fact]
    public void FormattedRunsPreviewIsPlainText()
    {
        var cap = Rtf(null, @"{\rtf1\ansi\b Bold\b0  and plain}");

        Assert.Equal("Bold and plain", cap.PreviewText);
        Assert.Null(Detector.Detect(cap.SecretScanText));
    }
}
