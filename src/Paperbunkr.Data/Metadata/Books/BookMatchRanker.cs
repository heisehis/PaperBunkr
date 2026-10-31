using System.Text.RegularExpressions;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.Data.Metadata.Books;

public readonly record struct RankedBookMatch(BookProviderRecord Record, double Score);

/// <summary>Scores provider candidates against a book (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §5): ISBN first, then title similarity, author and language.</summary>
public static class BookMatchRanker
{
    /// <summary>At or above this a bulk run applies a single clear winner without asking.</summary>
    public const double ConfidentScore = 0.85;

    /// <summary>A runner-up must trail the winner by at least this much for the winner to count as clear.</summary>
    public const double ClearMargin = 0.15;

    private static readonly Regex NonAlnum = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    public static IReadOnlyList<RankedBookMatch> Rank(IEnumerable<BookProviderRecord> candidates, string title, string? author, string? isbn, string? languageIso)
    {
        string? wantIsbn = BookMetadataParsing.ExtractIsbn(isbn);
        return candidates
            .Select(c => new RankedBookMatch(c, Score(c, title, author, wantIsbn, languageIso)))
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    public static double Score(BookProviderRecord c, string title, string? author, string? wantIsbn, string? languageIso)
    {
        if (wantIsbn is not null && string.Equals(c.Isbn, wantIsbn, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }

        double titleScore = Math.Max(TokenSimilarity(title, c.Title), TokenSimilarity(title, string.IsNullOrWhiteSpace(c.Subtitle) ? c.Title : c.Title + " " + c.Subtitle));

        double authorScore = 0.5; // unknown on either side: neutral, not a penalty
        if (!string.IsNullOrWhiteSpace(author) && c.Authors.Count > 0)
        {
            string lastName = LastName(author);
            authorScore = c.Authors.Any(a => Normalize(a).Contains(lastName, StringComparison.Ordinal)) ? 1.0 : 0.0;
        }

        double score = (titleScore * 0.7) + (authorScore * 0.3);

        if (!string.IsNullOrWhiteSpace(languageIso) && !string.IsNullOrWhiteSpace(c.LanguageISO)
            && !c.LanguageISO.StartsWith(languageIso[..Math.Min(2, languageIso.Length)], StringComparison.OrdinalIgnoreCase))
        {
            score *= 0.8;
        }

        return Math.Round(score, 4);
    }

    /// <summary>The single candidate a bulk run may apply without asking: confident and clearly ahead of the rest. Null otherwise.</summary>
    public static BookProviderRecord? ClearWinner(IReadOnlyList<RankedBookMatch> ranked)
    {
        if (ranked.Count == 0 || ranked[0].Score < ConfidentScore)
        {
            return null;
        }

        return ranked.Count == 1 || ranked[0].Score - ranked[1].Score >= ClearMargin ? ranked[0].Record : null;
    }

    private static double TokenSimilarity(string a, string b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0)
        {
            return 0;
        }

        int common = ta.Intersect(tb).Count();
        return (double)common / ta.Union(tb).Count();
    }

    private static HashSet<string> Tokens(string text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t is not ("the" or "a" or "an" or "of"))
            .ToHashSet(StringComparer.Ordinal);

    private static string Normalize(string text) => NonAlnum.Replace(text.ToLowerInvariant(), " ").Trim();

    private static string LastName(string author)
    {
        // "Herbert, Frank" (sort form) or "Frank Herbert".
        string a = author.Trim();
        int comma = a.IndexOf(',');
        string last = comma > 0 ? a[..comma] : a.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? a;
        return Normalize(last);
    }
}
