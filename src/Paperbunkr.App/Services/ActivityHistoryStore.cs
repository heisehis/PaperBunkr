using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Filter for the Activity Center's History tab (docs/superpowers/specs/2026-09-03-activity-center-
/// design.md). All fields optional - the default value is "no filter".
/// </summary>
public sealed record ActivityHistoryFilter(
    string? Search = null,
    ActivityJobKind? Kind = null,
    TimeSpan? MaxAge = null,
    bool FailuresOnly = false);

/// <summary>
/// Reads, writes and prunes the persisted <see cref="ActivityRun"/> history. Every call opens a
/// fresh short-lived context (this app's convention - <c>PaperbunkrDb.CreateContext</c>). Static:
/// no state of its own, same shape as <c>PaperbunkrDb</c>.
/// </summary>
public static class ActivityHistoryStore
{
    /// <summary>Keep the newer of "this many rows" / "younger than <see cref="MaxRetentionAge"/>".</summary>
    public const int MinRowsRetained = 200;

    public static readonly TimeSpan MaxRetentionAge = TimeSpan.FromDays(30);

    /// <summary>Persist one settled job. Called from <see cref="ActivityService"/> on the terminal transition.</summary>
    public static void Record(ActivityRun run)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.ActivityRuns.Add(run);
        context.SaveChanges();
    }

    /// <summary>One page of history, newest first. <paramref name="skip"/>/<paramref name="take"/> page it.</summary>
    public static IReadOnlyList<ActivityRun> Query(ActivityHistoryFilter filter, int skip, int take)
    {
        using var context = PaperbunkrDb.CreateContext();
        IQueryable<ActivityRun> q = context.ActivityRuns.AsNoTracking().OrderByDescending(r => r.StartedUtc);

        if (filter.Kind is { } kind)
        {
            q = q.Where(r => r.Kind == kind);
        }

        if (filter.MaxAge is { } age)
        {
            var cutoff = DateTime.UtcNow - age;
            q = q.Where(r => r.StartedUtc >= cutoff);
        }

        if (filter.FailuresOnly)
        {
            q = q.Where(r => r.Status == ActivityRunStatus.Failed || (r.ItemsFailed != null && r.ItemsFailed > 0));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.Trim();
            q = q.Where(r => EF.Functions.Like(r.Title, $"%{term}%")
                             || (r.ResultSummary != null && EF.Functions.Like(r.ResultSummary, $"%{term}%")));
        }

        return q.Skip(skip).Take(take).ToList();
    }

    /// <summary>
    /// Startup prune: delete everything past the retention window. Best-effort - swallows its own
    /// failure the same way the auto-backup / content-type sweep triggers in <c>App.axaml.cs</c> do.
    /// </summary>
    public static void PruneOnStartup()
    {
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            int total = context.ActivityRuns.Count();
            if (total <= MinRowsRetained)
            {
                return;
            }

            var ageCutoff = DateTime.UtcNow - MaxRetentionAge;

            // The Nth-newest row's timestamp - nothing at or after it is ever pruned, so we keep at
            // least MinRowsRetained even when they are all older than the age cutoff.
            var nthNewestStarted = context.ActivityRuns
                .OrderByDescending(r => r.StartedUtc)
                .Skip(MinRowsRetained - 1)
                .Select(r => r.StartedUtc)
                .FirstOrDefault();

            var effectiveCutoff = ageCutoff < nthNewestStarted ? ageCutoff : nthNewestStarted;

            context.ActivityRuns
                .Where(r => r.StartedUtc < effectiveCutoff)
                .ExecuteDelete();
        }
        catch
        {
            // A prune failure must never block startup.
        }
    }
}
