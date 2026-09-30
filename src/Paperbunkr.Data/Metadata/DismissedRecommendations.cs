using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One hidden recommendation, joined to its series name for the Preferences list.</summary>
public sealed record DismissedRecommendationRow(int SeriesId, string SeriesName, DateTime DismissedUtc);

/// <summary>
/// Read/write for Home's "Not interested" list (docs/superpowers/specs/2026-09-28-home-improvements-design.md I5). Every method
/// saves its own change; callers pass a context they own.
/// </summary>
public static class DismissedRecommendations
{
    /// <summary>Hides <paramref name="seriesId"/> from Because-You-Read. Idempotent - a second dismissal keeps the first date.</summary>
    public static void Dismiss(PaperbunkrDbContext context, int seriesId, DateTime nowUtc)
    {
        if (context.DismissedRecommendations.Any(d => d.SeriesId == seriesId))
        {
            return;
        }

        context.DismissedRecommendations.Add(new DismissedRecommendation { SeriesId = seriesId, DismissedUtc = nowUtc });
        context.SaveChanges();
    }

    /// <summary>Undo / Unhide. A no-op when the series was never dismissed.</summary>
    public static void Restore(PaperbunkrDbContext context, int seriesId)
    {
        var rows = context.DismissedRecommendations.Where(d => d.SeriesId == seriesId).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        context.DismissedRecommendations.RemoveRange(rows);
        context.SaveChanges();
    }

    public static HashSet<int> GetIds(PaperbunkrDbContext context)
        => context.DismissedRecommendations.Select(d => d.SeriesId).ToHashSet();

    /// <summary>Newest first, with series names. Rows whose series no longer resolves are skipped.</summary>
    public static IReadOnlyList<DismissedRecommendationRow> List(PaperbunkrDbContext context)
        => context.DismissedRecommendations
            .Join(context.Series, d => d.SeriesId, s => s.Id, (d, s) => new { d.SeriesId, s.Name, d.DismissedUtc })
            .ToList()
            .OrderByDescending(x => x.DismissedUtc)
            .Select(x => new DismissedRecommendationRow(x.SeriesId, x.Name, x.DismissedUtc))
            .ToList();
}
