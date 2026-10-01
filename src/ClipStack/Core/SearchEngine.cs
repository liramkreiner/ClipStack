namespace ClipStack.Core;

/// <summary>
/// Ranks history items against a query: exact > prefix > word-start > substring > all-words > fuzzy,
/// with usage and recency as tie-breakers. Runs synchronously; fast enough for tens of thousands of items.
/// </summary>
public static class SearchEngine
{
    private const int FuzzyWindow = 4000;

    public static List<ClipItem> Search(IReadOnlyList<ClipItem> ordered, string? query, bool useUsage = true)
    {
        var q = (query ?? "").Trim().ToLowerInvariant();
        if (q.Length == 0) return ordered.ToList();

        var tokens = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var scored = new List<(ClipItem item, int score)>();
        foreach (var item in ordered)
        {
            int s = Score(item.SearchText, q, tokens);
            if (s <= 0) continue;
            if (useUsage) s += Math.Min(item.UseCount, 20) * 2;
            if (item.IsPinned) s += 5;
            scored.Add((item, s));
        }

        return scored
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.item.UpdatedAt)
            .Select(x => x.item)
            .ToList();
    }

    internal static int Score(string text, string q, string[] tokens)
    {
        if (text.Length == 0) return 0;
        var trimmed = text.AsSpan().Trim();
        if (trimmed.SequenceEqual(q)) return 1000;
        if (trimmed.StartsWith(q, StringComparison.Ordinal)) return 800;

        int idx = text.IndexOf(q, StringComparison.Ordinal);
        if (idx >= 0)
            return IsWordStart(text, idx) ? 650 : 550;

        if (tokens.Length > 1 && tokens.All(t => text.Contains(t, StringComparison.Ordinal)))
            return 450;

        if (q.Length >= 2 && FuzzyScore(text, q) is var f and > 0)
            return 100 + f;

        return 0;
    }

    private static bool IsWordStart(string text, int idx) =>
        idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);

    /// <summary>
    /// Subsequence match (characters in order, gaps allowed). Returns 1..99, higher for tighter matches;
    /// 0 if the query isn't a subsequence. Only the first part of long texts is examined.
    /// </summary>
    internal static int FuzzyScore(string text, string q)
    {
        int limit = Math.Min(text.Length, FuzzyWindow);
        int best = 0;
        // Try a few starting positions of the first character to find a compact match.
        int start = text.IndexOf(q[0], 0, limit);
        for (int attempts = 0; start >= 0 && attempts < 8; attempts++)
        {
            // Matches must begin at a word start ("gl" → "git log", but "git" ↛ "meetinG Is aT").
            if (!IsWordStart(text, start))
            {
                attempts--;
                start = start + 1 < limit ? text.IndexOf(q[0], start + 1, limit - start - 1) : -1;
                continue;
            }
            int ti = start, qi = 0, gaps = 0, last = start - 1;
            while (ti < limit && qi < q.Length)
            {
                if (text[ti] == q[qi])
                {
                    if (ti != last + 1) gaps++;
                    last = ti;
                    qi++;
                }
                ti++;
            }
            if (qi < q.Length) break; // no match from here means none further either
            int span = last - start + 1;
            int score = Math.Max(1, 99 - (span - q.Length) - gaps * 6);
            best = Math.Max(best, score);
            start = start + 1 < limit ? text.IndexOf(q[0], start + 1, limit - start - 1) : -1;
        }
        // Require the match to be reasonably tight; long scattered matches are noise.
        return best >= 25 ? best : 0;
    }
}
