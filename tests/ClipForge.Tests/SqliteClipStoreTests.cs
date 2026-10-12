using System.Globalization;

using ClipForge.Core;
using ClipForge.Storage.Sqlite;

using Microsoft.Data.Sqlite;

using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// SqliteClipStore against real temp-file databases: round-trips, ordering,
/// search, delete/pin, both prunes, and persistence across store instances.
/// Every test gets its own database; all timestamps are explicit (the
/// age-prune test uses day-scale margins, so it cannot flake on timing).
/// </summary>
public sealed class SqliteClipStoreTests : IDisposable
{
    private const long BaseMs = 1_700_000_000_000;
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private async Task<SqliteClipStore> CreateStoreAsync()
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "clipforge-tests-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        _tempDirs.Add(dir);
        var store = new SqliteClipStore(Path.Combine(dir, "clips.db"));
        await store.InitializeAsync().ConfigureAwait(false);
        return store;
    }

    private static Clip MakeClip(string text, long createdMs, bool pinned = false) => new()
    {
        Type = ClipType.Text,
        Text = text,
        PreviewText = text,
        SourceApp = "tests",
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(createdMs),
        Pinned = pinned,
        ContentHash = "Text:h-" + text
    };

    [Fact]
    public async Task SaveAssignsIdAndGetByIdRoundTripsAsync()
    {
        var store = await CreateStoreAsync().ConfigureAwait(true);
        var clip = new Clip
        {
            Type = ClipType.Rtf,
            Text = "companion text",
            RtfText = @"{\rtf1\ansi companion text}",
            PreviewText = "companion text",
            SourceApp = "WordPad",
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(BaseMs),
            Pinned = true,
            ContentHash = "Rtf:abc"
        };

        var id = await store.SaveAsync(clip).ConfigureAwait(true);

        Assert.False(id.IsNone);
        Assert.Equal(id, clip.Id);
        var loaded = await store.GetByIdAsync(id).ConfigureAwait(true);
        Assert.NotNull(loaded);
        Assert.Equal(ClipType.Rtf, loaded.Type);
        Assert.Equal("companion text", loaded.Text);
        Assert.Equal(clip.RtfText, loaded.RtfText);
        Assert.Equal("companion text", loaded.PreviewText);
        Assert.Equal("WordPad", loaded.SourceApp);
        Assert.Equal(clip.CreatedAt, loaded.CreatedAt);
        Assert.True(loaded.Pinned);
        Assert.Equal("Rtf:abc", loaded.ContentHash);
    }

    [Fact]
    public async Task GetByIdMissingReturnsNullAndNoneThrowsAsync()
    {
        var store = await CreateStoreAsync().ConfigureAwait(true);

        Assert.Null(await store.GetByIdAsync(new ClipId(999)).ConfigureAwait(true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.GetByIdAsync(ClipId.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task GetRecentPinnedFirstThenNewestFirstAsync()
    {
        var store = await CreateStoreAsync().ConfigureAwait(true);
        await store.SaveAsync(MakeClip("old", BaseMs)).ConfigureAwait(true);
        await store.SaveAsync(MakeClip("newest", BaseMs + 2000)).ConfigureAwait(true);
        await store.SaveAsync(MakeClip("pinned-old", BaseMs - 2000, pinned: true)).ConfigureAwait(true);

        var recent = await store.GetRecentAsync().ConfigureAwait(true);

        Assert.Equal(3, recent.Count);
        Assert.Equal("pinned-old", recent[0].Text);
        Assert.Equal("newest", recent[1].Text);
        Assert.Equal("old", recent[2].Text);
    }

    [Fact]
    public async Task GetRecentRespectsLimitAndRejectsZeroAsync()
    {
        var store = await CreateStoreAsync().ConfigureAwait(true);
        for (int i = 0; i < 3; i++)
            await store.SaveAsync(MakeClip("clip-" + i, BaseMs + i)).ConfigureAwait(true);

        var two = await store.GetRecentAsync(limit: 2).ConfigureAwait(true);
        Assert.Equal(2, two.Count);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.GetRecentAsync(limit: 0)).ConfigureAwait(true);
    }

    [Fact]
    public async Task SearchMatchesPreviewAndTextCaseInsensitivelyAsync()
    {
        var store = await CreateStoreAsync().ConfigureAwait(true);
        await store.SaveAsync(MakeClip("Quarterly Report", BaseMs)).ConfigureAwait(true);
        // This clip matches on text_content only; its preview lacks the needle.
        var bodyHit = new Clip
        {
            Type = ClipType.Text,
            Text = "the annual report body",
            PreviewText = "the annual\u2026",
            SourceApp = "tests",
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(BaseMs + 1),
            ContentHash = "Text:h-bodyhit"
        };
        await store.SaveAsync(bodyHit).ConfigureAwait(true);
        await store.SaveAsync(MakeClip("unrelated", BaseMs + 2)).ConfigureAwait(true);

        var hits = await store.SearchAsync("REPORT").ConfigureAwait(true);

        // Newest first (search orders by created_at DESC).
        Assert.Equal(2, hits.Count);
        Assert.Equal("the annual\u2026", hits[0].PreviewText);
        Assert.Equal("Quarterly Report", hits[1].PreviewText);
    }

    [Fact]
    public async Task SearchEscapesLikeWildcardsAsync()
    {
        var store = await CreateStoreAsync().ConfigureAwait(true);
        await store.SaveAsync(MakeClip("100% sure", BaseMs)).ConfigureAwait(true);
        await store.SaveAsync(MakeClip("plain", BaseMs + 1)).ConfigureAwait(true);
        await store.SaveAsync(MakeClip("under_score", BaseMs + 2)).ConfigureAwait(true);

        // Unescaped, "%" and "_" would match every row.
        var pct = await store.SearchAsync("%").ConfigureAwait(true);
        Assert.Single(pct);
        Assert.Equal("100% sure", pct[0].Text);

        var underscore = await store.SearchAsync("_").ConfigureAwait(true);
        Assert.Single(underscore);
        Assert.Equal("under_score", underscore[0].Text);
    }

    [Fact]
    public async Task DeleteRemovesTheClipAndNoneThrowsAsync()
    {
        var store = await CreateStoreAsync().ConfigureAwait(true);
        var id = await store.SaveAsync(MakeClip("gone", BaseMs)).ConfigureAwait(true);

        await store.DeleteAsync(id).ConfigureAwait(true);

        Assert.Null(await store.GetByIdAsync(id).ConfigureAwait(true));
        Assert.Equal(0, await store.CountAsync().ConfigureAwait(true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.DeleteAsync(ClipId.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task SetPinnedTogglesTheFlagAsync()
    {
        var store = await CreateStoreAsync().ConfigureAwait(true);
        var id = await store.SaveAsync(MakeClip("pin me", BaseMs)).ConfigureAwait(true);

        await store.SetPinnedAsync(id, true).ConfigureAwait(true);
        var pinned = await store.GetByIdAsync(id).ConfigureAwait(true);
        Assert.NotNull(pinned);
        Assert.True(pinned.Pinned);

        await store.SetPinnedAsync(id, false).ConfigureAwait(true);
        var unpinned = await store.GetByIdAsync(id).ConfigureAwait(true);
        Assert.NotNull(unpinned);
        Assert.False(unpinned.Pinned);
    }

    [Fact]
    public async Task PruneByCountKeepsNewestUnpinnedAndAllPinnedAsync()
    {
        var store = await CreateStoreAsync().ConfigureAwait(true);
        for (int i = 0; i < 5; i++)
            await store.SaveAsync(MakeClip("clip-" + i, BaseMs + (i * 60_000))).ConfigureAwait(true);
        await store.SaveAsync(MakeClip("pinned-a", BaseMs - 60_000, pinned: true)).ConfigureAwait(true);
        await store.SaveAsync(MakeClip("pinned-b", BaseMs - 120_000, pinned: true)).ConfigureAwait(true);

        int removed = await store.PruneAsync(2).ConfigureAwait(true);

        Assert.Equal(3, removed);
        Assert.Equal(4, await store.CountAsync().ConfigureAwait(true));
        var recent = await store.GetRecentAsync().ConfigureAwait(true);
        Assert.Equal("pinned-a", recent[0].Text);
        Assert.Equal("pinned-b", recent[1].Text);
        Assert.Equal("clip-4", recent[2].Text);
        Assert.Equal("clip-3", recent[3].Text);
    }

    [Fact]
    public async Task PruneByCountZeroRemovesAllUnpinnedNegativeThrowsAsync()
    {
        var store = await CreateStoreAsync().ConfigureAwait(true);
        await store.SaveAsync(MakeClip("a", BaseMs)).ConfigureAwait(true);
        await store.SaveAsync(MakeClip("b", BaseMs + 1, pinned: true)).ConfigureAwait(true);

        int removed = await store.PruneAsync(0).ConfigureAwait(true);

        Assert.Equal(1, removed);
        Assert.Equal(1, await store.CountAsync().ConfigureAwait(true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.PruneAsync(-1)).ConfigureAwait(true);
    }

    [Fact]
    public async Task PruneByAgeRemovesOnlyOldUnpinnedAsync()
    {
        var store = await CreateStoreAsync().ConfigureAwait(true);
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await store.SaveAsync(MakeClip("ancient", nowMs - (200L * 24 * 60 * 60 * 1000))).ConfigureAwait(true);
        await store.SaveAsync(MakeClip("recent", nowMs - (10L * 24 * 60 * 60 * 1000))).ConfigureAwait(true);
        await store.SaveAsync(MakeClip("ancient-pinned", nowMs - (200L * 24 * 60 * 60 * 1000), pinned: true)).ConfigureAwait(true);

        int removed = await store.PruneByAgeAsync(TimeSpan.FromDays(90)).ConfigureAwait(true);

        Assert.Equal(1, removed);
        Assert.Equal(2, await store.CountAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task PersistenceClipsSurviveANewStoreInstanceAsync()
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "clipforge-tests-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        _tempDirs.Add(dir);
        string dbPath = Path.Combine(dir, "clips.db");

        var first = new SqliteClipStore(dbPath);
        await first.InitializeAsync().ConfigureAwait(true);
        var id = await first.SaveAsync(MakeClip("durable", BaseMs)).ConfigureAwait(true);

        var second = new SqliteClipStore(dbPath);
        await second.InitializeAsync().ConfigureAwait(true); // also: init is idempotent
        var loaded = await second.GetByIdAsync(id).ConfigureAwait(true);

        Assert.NotNull(loaded);
        Assert.Equal("durable", loaded.Text);
        Assert.Equal(1, await second.CountAsync().ConfigureAwait(true));
    }
}
