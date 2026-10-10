namespace ClipForge.Core;

/// <summary>
/// Persistence seam for clips. Days 3+ workers build search/pinning/snippets on top of this API.
/// </summary>
public interface IClipStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<long> SaveAsync(Clip clip, CancellationToken ct = default);
    Task<IReadOnlyList<Clip>> GetRecentAsync(int limit = 100, CancellationToken ct = default);
    Task<Clip?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>Substring fallback search; the overlay ranks in-memory with <see cref="FuzzyMatcher"/> for instant filtering.</summary>
    Task<IReadOnlyList<Clip>> SearchAsync(string query, int limit = 100, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
    Task SetPinnedAsync(long id, bool pinned, CancellationToken ct = default);

    /// <summary>Deletes unpinned clips beyond the newest <paramref name="maxUnpinned"/>; returns rows removed.</summary>
    Task<int> PruneAsync(int maxUnpinned, CancellationToken ct = default);

    /// <summary>Deletes unpinned clips older than <paramref name="maxAge"/>; returns rows removed.</summary>
    Task<int> PruneByAgeAsync(TimeSpan maxAge, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);
}
