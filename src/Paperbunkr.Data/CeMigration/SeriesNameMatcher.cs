using System.Text;

namespace Paperbunkr.Data.CeMigration;

/// <summary>
/// One candidate near-duplicate series-name pair, surfaced for a merge-or-keep-separate decision
/// (docs/onboarding.md §14 step 3, docs/superpowers/specs/2026-08-06-migration-ux-design.md §A).
/// </summary>
public record SeriesConflictCandidate(string IncomingName, string MatchedName, double Similarity);

/// <summary>
/// Fuzzy series-name matching for CE migration. Exact case-insensitive matches are never reported
/// here - <see cref="CeLibraryMigrator"/>'s <c>GroupBySeries</c> already collapses those into one
/// group, and its idempotent commit path already handles exact matches against the existing
/// library, so this class only surfaces the "close but not identical" cases that need a human
/// decision.
/// </summary>
public static class SeriesNameMatcher
{
    /// <summary>Minimum similarity (0.0-1.0) for two names to be reported as a candidate conflict.</summary>
    public const double SimilarityThreshold = 0.82;

    /// <summary>
    /// Blended similarity: 90% normalized Levenshtein-ratio (character-level) + 10% Jaccard index
    /// over normalized word sets (word-level). Character-only similarity scores a long shared
    /// prefix deceptively high regardless of what differs after it (e.g. "Wonder Woman" / "Wonder
    /// Man" at 83%, above threshold, despite being entirely different series) - blending in
    /// word-level agreement corrects that while still catching real near-duplicates.
    /// </summary>
    /// <remarks>
    /// docs/superpowers/specs/2026-08-06-migration-ux-polish-design.md §1 originally specified a
    /// 70/30 blend. Verified numerically against its own worked examples before implementing:
    /// 70/30 does reject "Wonder Woman"/"Wonder Man" (≈0.68), but also drops "World War
    /// Hulks"/"World War Hulk" to ≈0.80 - below threshold, contradicting the spec's own "must
    /// still flag" requirement for that pair (its table's ≈0.85 was an arithmetic error) - and
    /// drops the pre-existing "Silver City"/"Silver Citi" one-character-typo regression test to
    /// ≈0.74. Word-level Jaccard is harsh on short multi-word names: a single-character change
    /// inside one word zeroes that word's contribution entirely, since Jaccard treats "hulks" and
    /// "hulk" as wholly unrelated tokens. A 90/10 blend keeps comfortable margins on both sides:
    /// Wonder Woman/Man ≈0.78 (rejected), World War Hulks/Hulk ≈0.89 (flagged), Silver
    /// City/Citi ≈0.85 (flagged).
    /// </remarks>
    public static double Similarity(string a, string b)
    {
        string normA = Normalize(a);
        string normB = Normalize(b);
        if (normA.Length == 0 && normB.Length == 0)
        {
            return 1.0;
        }

        double charSimilarity = CharSimilarity(normA, normB);
        double wordJaccard = WordJaccard(normA, normB);
        return (0.9 * charSimilarity) + (0.1 * wordJaccard);
    }

    private static double CharSimilarity(string normA, string normB)
    {
        int maxLen = Math.Max(normA.Length, normB.Length);
        if (maxLen == 0)
        {
            return 1.0;
        }

        int distance = LevenshteinDistance(normA, normB);
        return 1.0 - (double)distance / maxLen;
    }

    private static double WordJaccard(string normA, string normB)
    {
        var wordsA = new HashSet<string>(normA.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var wordsB = new HashSet<string>(normB.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (wordsA.Count == 0 && wordsB.Count == 0)
        {
            return 1.0;
        }

        int intersection = wordsA.Intersect(wordsB).Count();
        int union = wordsA.Union(wordsB).Count();
        return union == 0 ? 1.0 : (double)intersection / union;
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (char c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Intra-import pass: near-duplicate pairs within one set of distinct incoming series names.
    /// Skips pairs whose raw (case-insensitive) forms are already identical - those are already
    /// one <c>GroupBySeries</c> group, not a conflict.
    /// </summary>
    public static List<SeriesConflictCandidate> FindIntraGroupCandidates(IReadOnlyList<string> names)
    {
        var results = new List<SeriesConflictCandidate>();
        for (int i = 0; i < names.Count; i++)
        {
            for (int j = i + 1; j < names.Count; j++)
            {
                if (string.Equals(names[i], names[j], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double similarity = Similarity(names[i], names[j]);
                if (similarity >= SimilarityThreshold)
                {
                    results.Add(new SeriesConflictCandidate(names[i], names[j], similarity));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Against-existing pass: for each incoming name, the best fuzzy match among series already in
    /// the target library. An incoming name with an exact (case-insensitive) match among
    /// <paramref name="existingNames"/> is skipped entirely - that's the idempotent re-run path in
    /// <see cref="CeLibraryMigrator.Migrate"/>, not a conflict.
    /// </summary>
    public static List<SeriesConflictCandidate> FindAgainstExistingCandidates(
        IReadOnlyList<string> incomingNames, IReadOnlyList<string> existingNames)
    {
        var results = new List<SeriesConflictCandidate>();
        foreach (string incoming in incomingNames)
        {
            SeriesConflictCandidate? best = null;
            foreach (string existing in existingNames)
            {
                if (string.Equals(incoming, existing, StringComparison.OrdinalIgnoreCase))
                {
                    best = null;
                    break;
                }

                double similarity = Similarity(incoming, existing);
                if (similarity >= SimilarityThreshold && (best is null || similarity > best.Similarity))
                {
                    best = new SeriesConflictCandidate(incoming, existing, similarity);
                }
            }

            if (best is not null)
            {
                results.Add(best);
            }
        }

        return results;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }
}
