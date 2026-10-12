using ClipForge.Core;

using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// HotkeyChordParser — the framework-free half of chord handling extracted
/// from the WPF app. The parser decides structure and modifiers only; the
/// app remains the authority on whether a key name is a real key.
/// (Post-extraction API: this file is excluded from the pre-fix regression run.)
/// </summary>
public sealed class HotkeyChordParserTests
{
    [Fact]
    public void TryParseCanonicalChord()
    {
        Assert.True(HotkeyChordParser.TryParse("Ctrl+Shift+V", out var chord));
        Assert.NotNull(chord);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, chord.Modifiers);
        Assert.Equal("V", chord.KeyName);
    }

    [Fact]
    public void TryParseModifierSynonymsAndCase()
    {
        Assert.True(HotkeyChordParser.TryParse("CONTROL+SHIFT+V", out var upper));
        Assert.True(HotkeyChordParser.TryParse("ctrl+shift+v", out var lower));
        Assert.NotNull(upper);
        Assert.NotNull(lower);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, upper.Modifiers);
        Assert.Equal(upper.Modifiers, lower.Modifiers);
        // The key name is preserved as written; case-insensitive comparison
        // is AreEquivalent's job (WPF's Key parse is case-insensitive too).
        Assert.Equal("v", lower.KeyName);
    }

    [Theory]
    [InlineData("Win+R", HotkeyModifiers.Windows, "R")]
    [InlineData("Windows+R", HotkeyModifiers.Windows, "R")]
    [InlineData("Control+Alt+Delete", HotkeyModifiers.Control | HotkeyModifiers.Alt, "Delete")]
    [InlineData("Alt+F5", HotkeyModifiers.Alt, "F5")]
    public void TryParseVariants(string text, HotkeyModifiers modifiers, string keyName)
    {
        Assert.True(HotkeyChordParser.TryParse(text, out var chord));
        Assert.NotNull(chord);
        Assert.Equal(modifiers, chord.Modifiers);
        Assert.Equal(keyName, chord.KeyName);
    }

    [Fact]
    public void TryParseToleratesSpacesAndModifierOrder()
    {
        Assert.True(HotkeyChordParser.TryParse("Ctrl + Shift + V", out var spaced));
        Assert.True(HotkeyChordParser.TryParse("Shift+Ctrl+V", out var reordered));
        Assert.NotNull(spaced);
        Assert.NotNull(reordered);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, spaced.Modifiers);
        Assert.Equal(spaced.Modifiers, reordered.Modifiers);
        Assert.Equal(spaced.KeyName, reordered.KeyName);
    }

    [Fact]
    public void TryParseDuplicateModifierCollapses()
    {
        Assert.True(HotkeyChordParser.TryParse("Ctrl+Ctrl+V", out var chord));
        Assert.NotNull(chord);
        Assert.Equal(HotkeyModifiers.Control, chord.Modifiers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("V")]        // no modifier
    [InlineData("Ctrl")]     // no key
    [InlineData("Ctrl+")]    // dangling separator leaves one segment
    [InlineData("+V")]       // same from the other side
    [InlineData("Hyper+V")]  // unknown modifier name
    [InlineData("LeftCtrl+V")] // a modifier KEY name is not a modifier word
    public void TryParseRejectsMalformedChords(string? text)
    {
        Assert.False(HotkeyChordParser.TryParse(text, out var chord));
        Assert.Null(chord);
    }

    [Theory]
    [InlineData("Ctrl+LeftCtrl")]
    [InlineData("Ctrl+RightShift")]
    [InlineData("Ctrl+LWin")]
    public void TryParseRejectsModifierKeysAsTheKey(string text)
    {
        // A chord needs a non-modifier key; the app rejects these too.
        Assert.False(HotkeyChordParser.TryParse(text, out _));
    }

    [Theory]
    [InlineData("CONTROL+SHIFT+V", "Ctrl+Shift+V")] // text drift: synonyms + case
    [InlineData("Ctrl+Shift+V", "Ctrl+Shift+v")]    // key-name case only
    [InlineData("Shift+Ctrl+V", "Ctrl+Shift+V")]    // segment order
    [InlineData("Ctrl+Ctrl+V", "Ctrl+V")]           // duplicated modifier
    public void AreEquivalentTextDriftParsesToTheSameChord(string a, string b)
    {
        // This is the app's ChordsEqual semantic at the parse level: the
        // settings-save path asks "did the chord actually change?"
        Assert.True(HotkeyChordParser.AreEquivalent(a, b));
    }

    [Theory]
    [InlineData("Ctrl+V", "Ctrl+Alt+V")]
    [InlineData("Ctrl+V", "Alt+V")]
    [InlineData("Ctrl+V", "Ctrl+X")]
    [InlineData(null, "Ctrl+V")]
    [InlineData("garbage", "garbage")] // unparseable is not equivalent, even to itself
    public void AreEquivalentDifferentOrInvalidChords(string? a, string? b)
    {
        Assert.False(HotkeyChordParser.AreEquivalent(a, b));
    }
}
