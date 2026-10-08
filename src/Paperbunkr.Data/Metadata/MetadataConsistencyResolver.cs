using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

public enum ConsistencyCheck
{
    /// <summary>An issue's year is far from the rest of its series' run.</summary>
    Year,

    /// <summary>An issue names a different publisher from most of its series.</summary>
    Publisher,

    /// <summary>An issue carries a different age rating from most of its series.</summary>
    AgeRating,
}

/// <summary>One issue that disagrees with the rest of its series.</summary>
public sealed record ConsistencyOutlier(int IssueId, string Number, string Value);

/// <summary>
/// One series + check that has outliers. Dismissed per series and check (<see cref="DismissalKey"/>), so "This is intended" covers
/// every outlier the check finds in that series - a reprint line or an imprint change is a property of the series, not of one issue.
/// </summary>
/// <param name="Expected">What most of the series has: the median year, or the majority publisher / rating.</param>
public sealed record ConsistencyFinding(int SeriesId, string SeriesName, ConsistencyCheck Check, string Expected, IReadOnlyList<ConsistencyOutlier> Outliers)
{
    public string DismissalKey => $"{SeriesId}:{Check}";
}

/// <summary>
/// Flags issues whose year, publisher or age rating disagrees with the rest of their series (docs/superpowers/specs/
/// 2026-10-06-smart-features-design.md §3.4). Report-only: nothing is changed and no proposal is created - a metadata proposal only fills
/// a field that is empty (the stored value wins in the <c>Effective*</c> resolvers), so it cannot correct a value that is already set.
/// "Duplicate issue numbers" is deliberately not a check; Library Health's Duplicates section already groups by series and number.
/// </summary>
public static class MetadataConsistencyResolver
{
    /// <summary>A series needs this many issues with a value before the value can be called the series' norm.</summary>
    public const int MinIssuesWithValue = 4;

    /// <summary>...and this share of them must agree.</summary>
    public const double AgreementFloor = 0.70;

    /// <summary>A year is an outlier when it is more than this many years from the series' median.</summary>
    public const int YearTolerance = 10;

    /// <summary>Local, owned issues only: placeholders have no file to be wrong about, and a remote library's metadata is not yours to fix.</summary>
    public static IReadOnlyList<ConsistencyFinding> Scan(PaperbunkrDbContext context)
    {
        var issues = context.Issues.AsNoTrackingWithIdentityResolution()
            .Include(i => i.Series)
            .Include(i => i.MetadataProposals)
            .Where(i => !i.IsPlaceholder)
            .AsSplitQuery()
            .ToList();
        return Scan(issues);
    }

    /// <summary><paramref name="issues"/> must have <see cref="Issue.Series"/> loaded. Ordered by series name, then check.</summary>
    public static IReadOnlyList<ConsistencyFinding> Scan(IEnumerable<Issue> issues)
    {
        var findings = new List<ConsistencyFinding>();
        foreach (var group in issues.Where(i => i.Series != null).GroupBy(i => i.Series!))
        {
            var series = group.ToList();
            AddIfAny(findings, YearOutliers(group.Key, series));
            AddIfAny(findings, MajorityOutliers(group.Key, series, ConsistencyCheck.Publisher, i => i.Publisher));
            AddIfAny(findings, MajorityOutliers(group.Key, series, ConsistencyCheck.AgeRating, i => i.AgeRating));
        }

        return findings
            .OrderBy(f => f.SeriesName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Check)
            .ToList();
    }

    private static void AddIfAny(List<ConsistencyFinding> findings, ConsistencyFinding? finding)
    {
        if (finding is not null)
        {
            findings.Add(finding);
        }
    }

    private static ConsistencyFinding? YearOutliers(Series series, List<Issue> issues)
    {
        var dated = issues.Select(i => (Issue: i, Year: i.EffectiveYear())).Where(x => x.Year is > 0).Select(x => (x.Issue, Year: x.Year!.Value)).ToList();
        if (dated.Count < MinIssuesWithValue)
        {
            return null;
        }

        var sorted = dated.Select(d => d.Year).OrderBy(y => y).ToList();
        int median = sorted[(sorted.Count - 1) / 2];
        var outliers = dated.Where(d => Math.Abs(d.Year - median) > YearTolerance).ToList();

        // The rest of the run has to actually cluster around the median, or there is no norm to be an outlier from (an anthology
        // reprinting fifty years of stories has no "right" year).
        if (outliers.Count == 0 || (double)(dated.Count - outliers.Count) / dated.Count < AgreementFloor)
        {
            return null;
        }

        return new ConsistencyFinding(series.Id, series.Name, ConsistencyCheck.Year, median.ToString(), ToOutliers(outliers.Select(o => (o.Issue, o.Year.ToString()))));
    }

    private static ConsistencyFinding? MajorityOutliers(Series series, List<Issue> issues, ConsistencyCheck check, Func<Issue, string?> selector)
    {
        var valued = issues.Select(i => (Issue: i, Value: selector(i)?.Trim())).Where(x => !string.IsNullOrEmpty(x.Value)).Select(x => (x.Issue, Value: x.Value!)).ToList();
        if (valued.Count < MinIssuesWithValue)
        {
            return null;
        }

        var majority = valued.GroupBy(v => v.Value, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).First();
        if ((double)majority.Count() / valued.Count < AgreementFloor)
        {
            return null;
        }

        var outliers = valued.Where(v => !string.Equals(v.Value, majority.Key, StringComparison.OrdinalIgnoreCase)).ToList();
        return outliers.Count == 0
            ? null
            : new ConsistencyFinding(series.Id, series.Name, check, majority.First().Value, ToOutliers(outliers.Select(o => (o.Issue, o.Value))));
    }

    private static IReadOnlyList<ConsistencyOutlier> ToOutliers(IEnumerable<(Issue Issue, string Value)> outliers) =>
        outliers
            .OrderBy(o => o.Issue.NumberSortKey() ?? float.MaxValue)
            .ThenBy(o => o.Issue.Id)
            .Select(o => new ConsistencyOutlier(o.Issue.Id, o.Issue.EffectiveNumber() ?? string.Empty, o.Value))
            .ToList();
}
