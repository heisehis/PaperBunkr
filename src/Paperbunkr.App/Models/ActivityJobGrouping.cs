using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>A section heading inserted between groups of running jobs in the Activity drawer's flat list.</summary>
public sealed record ActivityGroupHeader(string Title, int Count);

/// <summary>
/// Groups running jobs by what kind of work they are (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #21). Pure so the
/// mapping and the "only show headings when they help" rule are unit-tested. The list stays FLAT - headings are just extra items
/// interleaved among the jobs - so the row template's commands keep binding to the Activity Center view model through the one
/// <c>ItemsControl</c> (a nested list per group would re-parent them to the group object and break Cancel / Follow link).
/// </summary>
public static class ActivityJobGrouping
{
    /// <summary>The section a job kind belongs to.</summary>
    public static string CategoryFor(ActivityJobKind kind) => kind switch
    {
        ActivityJobKind.LibraryScan or ActivityJobKind.LibraryVerify or ActivityJobKind.BookScan or ActivityJobKind.Migration => "Library",
        ActivityJobKind.GenerateCovers or ActivityJobKind.SyncMetadata or ActivityJobKind.TrackerFetch or ActivityJobKind.Scrape => "Covers and metadata",
        ActivityJobKind.Acquisition or ActivityJobKind.Import => "Downloads and imports",
        ActivityJobKind.RemoteSync or ActivityJobKind.Sharing => "Sharing",
        ActivityJobKind.Plugin => "Plugins",
        _ => "Other",
    };

    private static readonly string[] Order = { "Library", "Covers and metadata", "Downloads and imports", "Sharing", "Plugins", "Other" };

    /// <summary>
    /// The jobs in section order (jobs keep their relative order within a section), each section preceded by its heading - but only when the running
    /// jobs span two or more sections. One section (or none) is returned as the plain job list, since a lone heading is noise.
    /// </summary>
    public static IReadOnlyList<object> Group(IReadOnlyList<ActivityJob> jobs)
    {
        var byCategory = jobs
            .GroupBy(j => CategoryFor(j.Kind))
            .OrderBy(g => System.Array.IndexOf(Order, g.Key))
            .ToList();

        if (byCategory.Count < 2)
        {
            return jobs.Cast<object>().ToList();
        }

        var result = new List<object>(jobs.Count + byCategory.Count);
        foreach (var group in byCategory)
        {
            result.Add(new ActivityGroupHeader(group.Key, group.Count()));
            result.AddRange(group);
        }

        return result;
    }
}
