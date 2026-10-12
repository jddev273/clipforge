using ClipForge.Core;

using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// Privacy policy: password-manager exclusion by foreground process name.
/// Matching is a case-insensitive substring check, exactly as implemented.
/// </summary>
public sealed class PrivacyTests
{
    [Theory]
    [InlineData("1Password")]
    [InlineData("Bitwarden")]
    [InlineData("KeePass")]
    [InlineData("KeePassXC")]
    [InlineData("keepassdx")]
    [InlineData("Dashlane")]
    [InlineData("LastPass")]
    [InlineData("RoboForm")]
    [InlineData("NordPass")]
    [InlineData("Keeper")]
    [InlineData("Enpass")]
    [InlineData("Authy")]
    public void KnownPasswordManagersAreSensitive(string processName)
    {
        Assert.True(Privacy.IsSensitiveApp(processName));
    }

    [Theory]
    [InlineData("LASTPASS")]
    [InlineData("lastpass")]
    [InlineData("1password-helper")] // substring semantics: helpers of a listed app match too
    public void MatchingIsCaseInsensitiveSubstring(string processName)
    {
        Assert.True(Privacy.IsSensitiveApp(processName));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("msedge")]
    [InlineData("firefox")]
    [InlineData("brave")]
    public void BrowsersAreNotSensitiveKnownLimitation(string processName)
    {
        // Expected behavior, documented — not a silent gap: a copy from a
        // password manager's browser extension or web vault is attributed
        // to the browser process, so process-name exclusion cannot see it
        // (2026-10-12 human-test finding #1). The desktop apps ARE excluded
        // (above); browser-hosted vaults are the boundary of this control.
        Assert.False(Privacy.IsSensitiveApp(processName));
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("WordPad")]
    [InlineData("Code")]
    [InlineData("explorer")]
    [InlineData("WindowsTerminal")]
    public void OrdinaryAppsAreNotSensitive(string processName)
    {
        Assert.False(Privacy.IsSensitiveApp(processName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmptyIsNotSensitive(string? processName)
    {
        Assert.False(Privacy.IsSensitiveApp(processName));
    }

    [Fact]
    public void RetentionDefaultsAreTheDocumentedOnes()
    {
        // Privacy-by-default: history is NOT indefinite.
        Assert.Equal(TimeSpan.FromDays(90), Privacy.DefaultRetention);
        Assert.Equal(5000, Privacy.MaxUnpinnedClips);
    }
}
