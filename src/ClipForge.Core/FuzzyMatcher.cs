namespace ClipForge.Core;

/// <summary>
/// Subsequence fuzzy matcher for instant as-you-type filtering (core MVP interaction).
/// Scores reward: match at string start, match after a separator, consecutive runs,
/// exact-case hits. Returns null when the query is not a subsequence of the target.
/// </summary>
public static class FuzzyMatcher
{
    public static int? Score(string query, string target)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        if (string.IsNullOrEmpty(target)) return null;

        int score = 0;
        int ti = 0;
        int consecutive = 0;

        foreach (char qc in query)
        {
            bool found = false;
            while (ti < target.Length)
            {
                char tc = target[ti];
                if (char.ToLowerInvariant(tc) == char.ToLowerInvariant(qc))
                {
                    found = true;
                    int bonus = 1;
                    if (ti == 0) bonus += 10;                                // start of string
                    else if (IsSeparator(target[ti - 1])) bonus += 8;        // start of word
                    if (consecutive > 0) bonus += 5 * Math.Min(consecutive, 4); // run bonus
                    if (tc == qc) bonus += 1;                               // exact case
                    score += bonus;
                    consecutive++;
                    ti++;
                    break;
                }
                consecutive = 0;
                ti++;
            }
            if (!found) return null;
        }

        // Prefer shorter targets: penalize unmatched tail length lightly.
        score -= (target.Length - query.Length) / 8;
        return score;
    }

    /// <summary>Filters and ranks clips by fuzzy score over preview + source app. Empty query returns input order.</summary>
    public static IReadOnlyList<Clip> FilterAndRank(IEnumerable<Clip> clips, string query, int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(query))
            return clips.Take(limit).ToList();

        query = query.Trim();
        var scored = new List<(Clip clip, int score)>();
        foreach (var clip in clips)
        {
            var target = string.IsNullOrEmpty(clip.SourceApp)
                ? clip.PreviewText
                : $"{clip.PreviewText} {clip.SourceApp}";
            var s = Score(query, target);
            if (s.HasValue) scored.Add((clip, s.Value));
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
