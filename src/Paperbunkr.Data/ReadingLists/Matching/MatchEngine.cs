using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists.Matching;

/// <summary>
/// Decides which comic in the library a <see cref="ComicReference"/> means (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md
/// §5.5). Pure: it reads its two arguments and writes nothing, so a preview never changes the library.
/// <para>Tier 1, provider ids: an id the source supplied that exactly one comic carries is <see cref="MatchClass.Verified"/>.</para>
/// <para>Tier 2, metadata: the same number in <em>any</em> series row with an equivalent name. One comic with nothing against it is
/// <see cref="MatchClass.Strong"/>; a different year or volume makes it <see cref="MatchClass.Possible"/> (decision Q4); several comics
/// the source cannot tell apart are <see cref="MatchClass.Ambiguous"/> (decision Q3) - never "the first one found".</para>
/// </summary>
public static class MatchEngine
{
    /// <summary>An entry's year and a comic's year agree within this many years (cover date and store date straddle a new year).</summary>
    private const int YearTolerance = 1;

    public static MatchResult Match(ComicReference reference, LibraryIndex library)
    {
        var byComicVine = library.WithProviderId(ComicProvider.ComicVine, reference.ComicVineIssueId);
        var byMetron = library.WithProviderId(ComicProvider.Metron, reference.MetronIssueId);
        if (byComicVine.Count > 0 || byMetron.Count > 0)
        {
            return MatchById(reference, byComicVine, byMetron);
        }

        return MatchByMetadata(reference, library);
    }

    private static MatchResult MatchById(ComicReference reference, IReadOnlyList<LibraryIssue> byComicVine, IReadOnlyList<LibraryIssue> byMetron)
    {
        var both = byComicVine.Intersect(byMetron).ToList();
        bool disagree = byComicVine.Count > 0 && byMetron.Count > 0 && both.Count == 0;
        var found = both.Count > 0 ? both : byComicVine.Concat(byMetron).Distinct().ToList();

        var comics = Copies(found)
            .Select(copy => new MatchCandidate(copy, [new MatchEvidence(EvidenceKind.Agrees, byComicVine.Contains(copy) ? "ComicVine id" : "Metron id")]))
            .ToList();

        // Two different comics under one id, or the two providers' ids pointing at different comics: someone has to look.
        return new MatchResult(reference, disagree || comics.Count > 1 ? MatchClass.Conflict : MatchClass.Verified, comics, ById: true);
    }

    private static MatchResult MatchByMetadata(ComicReference reference, LibraryIndex library)
    {
        string numberKey = reference.NumberKey;
        if (numberKey.Length == 0)
        {
            return new MatchResult(reference, MatchClass.Unresolved, [], ById: false);
        }

        var candidates = Copies(library.InSeries(reference.SeriesKey).Where(i => i.NumberKey == numberKey))
            .Select(issue => new MatchCandidate(issue, Weigh(reference, issue)))
            .OrderBy(c => c.HasConflict)
            .ThenByDescending(c => c.Evidence.Count(e => e.Kind == EvidenceKind.Agrees))
            .ThenBy(c => c.Issue.FileIsMissing)
            .ThenBy(c => c.Issue.Id)
            .ToList();

        if (candidates.Count == 0)
        {
            return new MatchResult(reference, MatchClass.Unresolved, [], ById: false);
        }

        if (candidates.Count == 1)
        {
            return new MatchResult(reference, candidates[0].HasConflict ? MatchClass.Possible : MatchClass.Strong, candidates, ById: false);
        }

        var fitting = candidates.Where(c => !c.HasConflict).ToList();
        var confirmed = fitting.Where(c => c.Agrees("year") || c.Agrees("volume")).ToList();
        if (confirmed.Count == 1)
        {
            // Put the one the source singles out first; the rest stay listed for the user who disagrees.
            var ranked = confirmed.Concat(candidates.Except(confirmed)).ToList();
            return new MatchResult(reference, MatchClass.Strong, ranked, ById: false);
        }

        return new MatchResult(reference, fitting.Count > 1 ? MatchClass.Ambiguous : MatchClass.Possible, candidates, ById: false);
    }

    private static List<MatchEvidence> Weigh(ComicReference reference, LibraryIssue issue)
    {
        var evidence = new List<MatchEvidence>
        {
            new(EvidenceKind.Agrees, "series"),
            new(EvidenceKind.Agrees, "number"),
        };

        if (reference.Year is int wanted && issue.Year is int actual)
        {
            evidence.Add(new MatchEvidence(Math.Abs(wanted - actual) <= YearTolerance ? EvidenceKind.Agrees : EvidenceKind.Conflicts, "year"));
        }
        else
        {
            evidence.Add(new MatchEvidence(EvidenceKind.Unknown, "year"));
        }

        evidence.Add(new MatchEvidence(CompareVolume(reference.Volume, issue.Volume), "volume"));

        // A format never disqualifies: sources and files name the same thing differently ("TPB", "Trade Paperback", blank).
        if (!string.IsNullOrWhiteSpace(reference.Format) && string.Equals(reference.Format.Trim(), issue.Format?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add(new MatchEvidence(EvidenceKind.Agrees, "format"));
        }

        return evidence;
    }

    /// <summary>
    /// A volume is written two ways - an ordinal ("3") or the series' start year ("2016") - and a source and a file often use different
    /// ones for the same series. They are only compared when both are the same kind; otherwise the volume says nothing.
    /// </summary>
    private static EvidenceKind CompareVolume(string? wanted, string? actual)
    {
        if (!int.TryParse(wanted?.Trim(), out int w) || !int.TryParse(actual?.Trim(), out int a) || w <= 0 || a <= 0 || (w >= 1000) != (a >= 1000))
        {
            return EvidenceKind.Unknown;
        }

        return w == a ? EvidenceKind.Agrees : EvidenceKind.Conflicts;
    }

    /// <summary>Several files of one comic (same series row, number, volume and year) are one candidate: the copy that is present and most complete.</summary>
    private static IEnumerable<LibraryIssue> Copies(IEnumerable<LibraryIssue> issues) =>
        issues.GroupBy(i => (i.SeriesId, i.NumberKey, i.Volume, i.Year))
            .Select(copies => copies
                .OrderBy(c => c.FileIsMissing)
                .ThenByDescending(c => c.PageCount)
                .ThenByDescending(c => c.FileSize)
                .ThenBy(c => c.Id)
                .First());
}
