using ClipForge.Core;
using ClipForge.Search;

using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// The substring ranker: deterministic cases for each scoring rule in
/// FuzzyMatcher.Score (position, start-of-string, word boundary, exact case,
/// length) and for FilterAndRank's ordering.
/// </summary>
public sealed class FuzzyMatcherTests
{
    private static readonly FuzzyMatcher Matcher = new();
    private const long BaseMs = 1_700_000_000_000;

    private static Clip Result(string preview, string? app, long createdMs) => new()
    {
        Type = ClipType.Text,
        Text = preview,
        PreviewText = preview,
        SourceApp = app,
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(createdMs),
        ContentHash = "Text:" + preview
    };

    [Fact]
    public void ScoreNoSubstringReturnsNull()
    {
        Assert.Null(Matcher.Score("zzz", "abc"));
        Assert.Null(Matcher.Score("abcd", "abc")); // query longer than target
        Assert.Null(Matcher.Score("abc", ""));
    }

    [Fact]
    public void ScoreEmptyQueryScoresZero()
    {
        Assert.Equal(0, Matcher.Score("", "anything"));
    }

    [Fact]
    public void ScoreNullArgumentsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => Matcher.Score(null!, "abc"));
        Assert.Throws<ArgumentNullException>(() => Matcher.Score("abc", null!));
    }

    [Fact]
    public void ScoreExactMatchAtStart()
    {
        // 100 base + 10 start-of-string + 5 exact case - 0 length = 115
        Assert.Equal(115, Matcher.Score("abc", "abc"));
    }

    [Fact]
    public void ScoreLaterPositionScoresLess()
    {
        // at index 5: 100 - 5 + 5 exact case = 100
        Assert.Equal(100, Matcher.Score("abc", "xxxxxabc"));
    }

    [Fact]
    public void ScoreWordBoundaryBonus()
    {
        // at index 2 after a space: 100 - 2 + 8 boundary + 5 exact case = 111
        Assert.Equal(111, Matcher.Score("abc", "x abc"));
    }

    [Fact]
    public void ScoreCaseMismatchLosesExactCaseBonus()
    {
        // matches case-insensitively, but the span is not exactly the query
        Assert.Equal(110, Matcher.Score("ABC", "abc"));
        Assert.Equal(115, Matcher.Score("ABC", "ABC"));
    }

    [Fact]
    public void ScoreBestOccurrenceWins()
    {
        // occurrences at 2 (103) and at 6 after a space (107)
        Assert.Equal(107, Matcher.Score("abc", "xxabc abc"));
    }

    [Fact]
    public void ScoreLongTargetsArePenalizedLightly()
    {
        // length 64: 115 - 64/32 = 113
        Assert.Equal(113, Matcher.Score("abc", "abc" + new string('z', 61)));
    }

    [Fact]
    public void FilterAndRankRanksByScoreThenRecency()
    {
        var a = Result("quarterly report", "notepad", BaseMs + 3000); // score 103
        var b = Result("report card", "notepad", BaseMs + 2000);      // score 115
        var c = Result("unrelated", "reportapp", BaseMs + 1000);      // app match, 115, older than b
        var d = Result("nothing here", "notepad", BaseMs + 4000);     // no match

        var ranked = Matcher.FilterAndRank(new[] { a, b, c, d }, "report");

        Assert.Equal(new[] { b, c, a }, ranked);
    }

    [Fact]
    public void FilterAndRankTrimsQueryAndRespectsLimit()
    {
        var a = Result("quarterly report", null, BaseMs + 3000);
        var b = Result("report card", null, BaseMs + 2000);
        var c = Result("unrelated", "reportapp", BaseMs + 1000);

        var ranked = Matcher.FilterAndRank(new[] { a, b, c }, "  report  ", limit: 2);

        Assert.Equal(new[] { b, c }, ranked);
    }

    [Fact]
    public void FilterAndRankEmptyQueryKeepsInputOrder()
    {
        var a = Result("one", null, BaseMs + 1000);
        var b = Result("two", null, BaseMs + 2000);

        Assert.Equal(new[] { a, b }, Matcher.FilterAndRank(new[] { a, b }, ""));
        Assert.Equal(new[] { a, b }, Matcher.FilterAndRank(new[] { a, b }, "   "));
    }

    [Fact]
    public void FilterAndRankNoMatchesReturnsEmpty()
    {
        var a = Result("one", null, BaseMs + 1000);

        Assert.Empty(Matcher.FilterAndRank(new[] { a }, "zzz"));
    }

    [Fact]
    public void FilterAndRankInvalidLimitThrows()
    {
        var a = Result("one", null, BaseMs + 1000);

        Assert.Throws<ArgumentOutOfRangeException>(() => Matcher.FilterAndRank(new[] { a }, "one", limit: 0));
    }
}
