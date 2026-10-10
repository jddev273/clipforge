namespace ClipForge.Core;

/// <summary>
/// Ditto-style substring matcher for instant as-you-type filtering (core MVP interaction).
/// Typing NARROWS the list: only clips containing the query as a contiguous,
/// case-insensitive substring stay visible. Matches are ranked by quality —
/// earlier position, word-boundary start and exact-case hits rank higher.
/// (Was subsequence matching; scattered subsequence hits read as "not filtering".)
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Scores a case-insensitive substring match. Returns null when
    /// <paramref name="query"/> is not a contiguous substring of
    /// <paramref name="target"/>. When several occurrences exist, the best one wins.
    /// </summary>
    public static int? Score(string query, string target)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrEmpty(query)) return 0;
        if (string.IsNullOrEmpty(target)) return null;

        int? best = null;
        int index = 0;
        while (true)
        {
            int at = target.IndexOf(query, index, StringComparison.OrdinalIgnoreCase);
            if (at < 0) break;
            int s = 100 - Math.Min(at, 90); // earlier position wins
            if (at == 0) s += 10;           // start of string
            else if (IsSeparator(target[at - 1])) s += 8; // start of word
            if (target.AsSpan(at, query.Length).SequenceEqual(query.AsSpan())) s += 5; // exact case
            s -= target.Length / 32; // shorter targets preferred, lightly
            if (!best.HasValue || s > best.Value) best = s;
            index = at + 1;
        }
        return best;
    }

    /// <summary>
    /// Filters clips to those containing the query (preview or source app), ranked by
    /// match quality. Empty query returns input order (recency).
    /// </summary>
    public static IReadOnlyList<Clip> FilterAndRank(IEnumerable<Clip> clips, string query, int limit = 100)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        if (string.IsNullOrWhiteSpace(query))
            return clips.Take(limit).ToList();

        query = query.Trim();
        var scored = new List<(Clip clip, int score)>();
        foreach (var clip in clips)
        {
            int? best = Score(query, clip.PreviewText);
            if (!string.IsNullOrEmpty(clip.SourceApp))
            {
                int? appScore = Score(query, clip.SourceApp);
                if (appScore.HasValue && (!best.HasValue || appScore.Value > best.Value))
                    best = appScore.Value;
            }
            if (best.HasValue) scored.Add((clip, best.Value));
        }
        return scored
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.clip.CreatedAt)
            .Take(limit)
            .Select(x => x.clip)
            .ToList();
    }

    private static bool IsSeparator(char c) =>
        c is ' ' or '\t' or '\n' or '_' or '-' or '.' or '/' or '\\' or ':' or '(' or '[';
}
