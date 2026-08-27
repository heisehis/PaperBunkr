using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.Data.Metadata;

/// <summary>Confidence tier a <see cref="TitleMatchScorer"/> score falls into.</summary>
public enum MatchTier
{
    /// <summary>Score &gt;= <see cref="TitleMatchScorer.AutoThreshold"/> - safe to pre-select as the likely match.</summary>
    Auto,

    /// <summary>Score &gt;= <see cref="TitleMatchScorer.ReviewThreshold"/> but below <see cref="Auto"/> - plausible, needs a human look.</summary>
    NeedsReview,

    /// <summary>Below <see cref="TitleMatchScorer.ReviewThreshold"/> - unlikely to be the right match.</summary>
    Reject,
}

/// <summary>
/// Title-similarity confidence scoring for metadata-provider search results (docs/superpowers/specs/
/// 2026-08-19-metadata-model-anilist-search-and-link-design.md) - the concrete, adoptable half of the
/// source architecture review's match-confidence-threshold recommendation (&gt;=0.95 auto,
/// 0.75-0.949 needs review, below reject). Deliberately just normalized edit-distance similarity
/// against known titles, not the fuller multi-signal scorer (ISBN/creator overlap/publication date)
/// the review's own source document sketched - those signals don't exist yet for a manga-only
/// provider search, and this is intentionally the boring version, not a preemptive general-purpose
/// matcher nothing calls for the extra signals yet.
/// </summary>
public static class TitleMatchScorer
{
    public const double AutoThreshold = 0.95;
    public const double ReviewThreshold = 0.75;

    /// <summary>0.0-1.0 normalized similarity between two titles, insensitive to case/whitespace/punctuation (so "One Piece" and "one-piece" score 1.0).</summary>
    public static double Score(string a, string b)
    {
        string na = Normalize(a);
        string nb = Normalize(b);
        if (na.Length == 0 && nb.Length == 0)
        {
            return 1.0;
        }

        if (na.Length == 0 || nb.Length == 0)
        {
            return 0.0;
        }

        int distance = LevenshteinDistance(na, nb);
        int maxLen = Math.Max(na.Length, nb.Length);
        return 1.0 - (double)distance / maxLen;
    }

    /// <summary>The best score of <paramref name="candidateTitle"/> against any of a series' known titles (primary name + alternates) - a match against any one of them counts, e.g. a native-script alternate title matching a native-script search result even when the series' primary name is the localized title.</summary>
    public static double BestScore(IEnumerable<string> knownTitles, string candidateTitle) =>
        knownTitles.Select(t => Score(t, candidateTitle)).DefaultIfEmpty(0.0).Max();

    public static MatchTier Tier(double score) =>
        score >= AutoThreshold ? MatchTier.Auto :
        score >= ReviewThreshold ? MatchTier.NeedsReview :
        MatchTier.Reject;

    private static string Normalize(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Classic dynamic-programming edit distance - no external dependency for a comparison this small (title-length strings, not documents).</summary>
    private static int LevenshteinDistance(string a, string b)
    {
        var distances = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            distances[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            distances[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[a.Length, b.Length];
    }
}
