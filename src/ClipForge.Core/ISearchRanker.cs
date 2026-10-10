namespace ClipForge.Core;

/// <summary>
/// Search seam: filters history to genuine matches, ranked by quality.
/// Implement this interface and drop the DLL in plugins/ to replace the ranker.
/// </summary>
public interface ISearchRanker
{
    /// <summary>
    /// Scores a case-insensitive substring match. Returns null when
    /// <paramref name="query"/> is not a contiguous substring of
    /// <paramref name="target"/>; the best occurrence wins when several exist.
    /// </summary>
    int? Score(string query, string target);

    /// <summary>
    /// Filters clips to those containing the query (preview or source app),
    /// ranked by match quality. Empty query returns input order (recency).
    /// </summary>
    IReadOnlyList<Clip> FilterAndRank(IEnumerable<Clip> clips, string query, int limit = 100);
}
