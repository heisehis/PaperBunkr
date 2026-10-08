using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>The copy to keep out of a group of duplicates, and the one reason it beat the runner-up.</summary>
public sealed record DuplicateKeeper(int IssueId, string Reason);

/// <summary>
/// Recommends which of several duplicate copies to keep (docs/superpowers/specs/2026-10-06-smart-features-design.md §5.2). Advice
/// only: nothing here selects, deletes or changes anything. Criteria, in order: the file is there and readable, more pages (the more
/// complete copy), more bytes per page (the better scan), the more open format, then the copy that has been in the library longest.
/// An issue carries no pixel dimensions, so resolution cannot be compared directly; bytes per page stands in for it.
/// </summary>
public static class DuplicateKeeperRanker
{
    /// <summary>Null for fewer than two copies - there is nothing to choose between.</summary>
    public static DuplicateKeeper? Recommend(IReadOnlyCollection<Issue> copies)
    {
        if (copies.Count < 2)
        {
            return null;
        }

        var ranked = copies
            .OrderByDescending(Usable)
            .ThenByDescending(c => c.PageCount ?? 0)
            .ThenByDescending(BytesPerPage)
            .ThenBy(FormatRank)
            .ThenBy(c => c.AddedTime ?? DateTime.MaxValue)
            .ThenBy(c => c.Id)
            .ToList();

        return new DuplicateKeeper(ranked[0].Id, Reason(ranked[0], ranked[1]));
    }

    /// <summary>The first criterion on which the winner actually differs from the runner-up.</summary>
    private static string Reason(Issue best, Issue next)
    {
        if (Usable(best) != Usable(next))
        {
            return next.FileIsMissing || string.IsNullOrEmpty(next.FilePath) ? "The other copy's file is missing" : "The other copy cannot be read";
        }

        int pages = best.PageCount ?? 0;
        int otherPages = next.PageCount ?? 0;
        if (pages != otherPages)
        {
            return $"Most pages: {pages} vs {otherPages}";
        }

        double perPage = BytesPerPage(best);
        double otherPerPage = BytesPerPage(next);
        if (perPage > 0 && otherPerPage > 0 && Math.Abs(perPage - otherPerPage) / otherPerPage >= 0.05)
        {
            return $"Larger pages: {Megabytes(best.FileSize)} vs {Megabytes(next.FileSize)}";
        }

        if (FormatRank(best) != FormatRank(next))
        {
            return $"{Extension(best).ToUpperInvariant()} over {Extension(next).ToUpperInvariant()}";
        }

        return "In your library the longest";
    }

    private static bool Usable(Issue copy) => !copy.FileIsMissing && !copy.IsContentEmpty && !copy.IsPlaceholder && !string.IsNullOrEmpty(copy.FilePath);

    private static double BytesPerPage(Issue copy) =>
        copy.FileSize is long size and > 0 && copy.PageCount is int pages and > 0 ? (double)size / pages : 0;

    private static int FormatRank(Issue copy) => Extension(copy).ToLowerInvariant() switch
    {
        "cbz" or "zip" => 0,
        "cbr" or "rar" => 1,
        "cb7" or "7z" => 2,
        "pdf" => 3,
        _ => 4,
    };

    private static string Extension(Issue copy) =>
        string.IsNullOrEmpty(copy.FilePath) ? string.Empty : Path.GetExtension(copy.FilePath).TrimStart('.');

    private static string Megabytes(long? bytes) => bytes is long b ? $"{b / 1024d / 1024d:0.#} MB" : "unknown size";
}
