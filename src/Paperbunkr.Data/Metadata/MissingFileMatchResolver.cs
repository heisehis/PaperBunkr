using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

public enum MissingFileMatchTier
{
    /// <summary>Same file size and page count: the same file under a new path.</summary>
    Exact,

    /// <summary>Same format, issue number and series name: very likely the same issue, possibly a different scan.</summary>
    Probable,
}

/// <summary>A library entry whose file is present and looks like the one a missing entry lost.</summary>
public sealed record MissingFileMatch(int MissingIssueId, int CandidateIssueId, string CandidatePath, MissingFileMatchTier Tier);

/// <summary>
/// Finds, for issues whose file is missing, another entry in the library that is probably the same file
/// (docs/superpowers/specs/2026-10-06-smart-features-design.md §5.1). The scanner does not relocate: a file moved inside a library
/// folder is imported as a new entry at its new path while the old entry stays marked missing, so the match for a missing file is
/// usually sitting in the library already. Pure; it reads nothing from disk.
/// </summary>
public static class MissingFileMatchResolver
{
    /// <summary>
    /// <paramref name="missing"/> and <paramref name="present"/> need <see cref="Issue.Series"/> loaded (for the probable tier's name
    /// match). A present entry is offered to at most one missing entry; exact matches are assigned before probable ones.
    /// </summary>
    public static Dictionary<int, MissingFileMatch> Find(IReadOnlyCollection<Issue> missing, IReadOnlyCollection<Issue> present)
    {
        var usable = present.Where(p => !p.FileIsMissing && !p.IsPlaceholder && !string.IsNullOrEmpty(p.FilePath)).ToList();
        var taken = new HashSet<int>();
        var matches = new Dictionary<int, MissingFileMatch>();

        void Assign(Issue lost, Issue found, MissingFileMatchTier tier)
        {
            taken.Add(found.Id);
            matches[lost.Id] = new MissingFileMatch(lost.Id, found.Id, found.FilePath!, tier);
        }

        var bySizeAndPages = usable
            .Where(p => p.FileSize is > 0 && p.PageCount is > 0)
            .GroupBy(p => (p.FileSize!.Value, p.PageCount!.Value))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var lost in missing)
        {
            if (lost.FileSize is > 0 && lost.PageCount is > 0
                && bySizeAndPages.TryGetValue((lost.FileSize.Value, lost.PageCount.Value), out var same))
            {
                // Among identical files, prefer the one filed under the same series name.
                var found = same.Where(p => p.Id != lost.Id && !taken.Contains(p.Id))
                    .OrderByDescending(p => SeriesNamesMatch(lost, p))
                    .ThenBy(p => p.Id)
                    .FirstOrDefault();
                if (found is not null)
                {
                    Assign(lost, found, MissingFileMatchTier.Exact);
                }
            }
        }

        foreach (var lost in missing.Where(m => !matches.ContainsKey(m.Id)))
        {
            string format = Extension(lost.FilePath);
            var found = usable
                .Where(p => p.Id != lost.Id && !taken.Contains(p.Id))
                .Where(p => format.Length > 0 && string.Equals(Extension(p.FilePath), format, StringComparison.OrdinalIgnoreCase))
                .Where(p => IssueNumbers.Equal(lost.EffectiveNumber(), p.EffectiveNumber()))
                .Where(p => SeriesNamesMatch(lost, p))
                .OrderBy(p => p.Id)
                .FirstOrDefault();
            if (found is not null)
            {
                Assign(lost, found, MissingFileMatchTier.Probable);
            }
        }

        return matches;
    }

    private static bool SeriesNamesMatch(Issue a, Issue b) =>
        a.SeriesId == b.SeriesId
        || (a.Series?.Name is { Length: > 0 } x && b.Series?.Name is { Length: > 0 } y && TitleNormalizer.NamesMatch(x, y));

    private static string Extension(string? path) =>
        string.IsNullOrEmpty(path) ? string.Empty : Path.GetExtension(path).TrimStart('.');
}
