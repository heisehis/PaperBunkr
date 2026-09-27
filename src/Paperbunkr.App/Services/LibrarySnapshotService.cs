using System;
using System.Linq;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>
/// Writes today's <see cref="LibrarySnapshot"/> row for the Insights screen's Backlog burn-down chart
/// (docs/superpowers/specs/2026-09-22-insights-backlog-burndown-design.md) - the body of the
/// <c>library-snapshot</c> scheduled task (<see cref="Services.Scheduling.ScheduledTaskCatalog"/>).
/// </summary>
public class LibrarySnapshotService
{
    /// <summary>Counts local comics and their backlog (never-opened) subset, and upserts today's
    /// <see cref="LibrarySnapshot"/> row by <see cref="LibrarySnapshot.SnapshotDate"/> - overwrites rather
    /// than appends, since a manual "Run now" from the Automation tab can trigger a second capture on a
    /// day the scheduler already ran for.</summary>
    public (int TotalOwned, int Backlog) Capture(PaperbunkrDbContext context)
    {
        // IsUnread() isn't EF-translatable, so materialize local issues first - same pattern
        // StatsResolver.Build already uses for its own issue lists.
        var local = context.Issues.Where(i => i.RemoteSourceId == null).ToList();
        int total = local.Count;
        int backlog = local.Count(i => i.IsUnread());

        var today = DateOnly.FromDateTime(DateTime.Now);
        var row = context.LibrarySnapshots.FirstOrDefault(s => s.SnapshotDate == today);
        if (row is null)
        {
            row = new LibrarySnapshot { SnapshotDate = today };
            context.LibrarySnapshots.Add(row);
        }

        row.TotalOwnedComics = total;
        row.BacklogComics = backlog;
        context.SaveChanges();

        return (total, backlog);
    }
}
