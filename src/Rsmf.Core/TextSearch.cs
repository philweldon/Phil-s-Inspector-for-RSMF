namespace Rsmf.Core;

/// <summary>Case-insensitive plain-text search used by the viewer (no patterns, no wildcards).</summary>
public static class TextSearch
{
    /// <summary>Finds non-overlapping occurrences of <paramref name="term"/>, in order, up to <paramref name="max"/>.</summary>
    public static List<(int Start, int Length)> FindMatches(string? text, string? term, int max = 500)
    {
        var hits = new List<(int, int)>();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term)) return hits;
        var at = 0;
        while (hits.Count < max)
        {
            var i = text.IndexOf(term, at, StringComparison.OrdinalIgnoreCase);
            if (i < 0) break;
            hits.Add((i, term.Length));
            at = i + term.Length;
        }
        return hits;
    }

    public static int Count(string? text, string? term) => FindMatches(text, term, int.MaxValue).Count;
}
