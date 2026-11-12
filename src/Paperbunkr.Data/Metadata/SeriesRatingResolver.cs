using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Computes <see cref="Series.Rating"/> from the series' own <see cref="Issue.Rating"/> values
/// (docs/superpowers/specs/2026-09-18-per-tracker-score-and-finish-date-design.md). <see cref="Recompute"/> is the
/// pure function; <see cref="Apply"/> writes it, and <c>PaperbunkrDbContext</c> calls that after any save that
/// changes an issue's rating, so no writer has to remember to.
/// </summary>
public static class SeriesRatingResolver
{
    /// <summary>Average of every rated issue's <see cref="Issue.Rating"/>. Rated means above 0 (CE's 0 is "no rating"):
    /// unrated issues are excluded entirely, never treated as 0. Null when no issue in the series has a rating yet.</summary>
    public static float? Recompute(Series series)
    {
        var rated = series.Issues.Where(i => i.Rating is > 0).Select(i => i.Rating!.Value).ToList();
        return rated.Count > 0 ? rated.Average() : null;
    }

    /// <summary>Sets <see cref="Series.Rating"/> on each of <paramref name="seriesIds"/> from the database's issue ratings and saves.
    /// Series that no longer exist are skipped. Returns how many series changed.</summary>
    public static int Apply(PaperbunkrDbContext context, IReadOnlyCollection<int> seriesIds)
    {
        if (seriesIds.Count == 0)
        {
            return 0;
        }

        var ids = seriesIds.ToList();
        var averages = context.Issues
            .Where(i => ids.Contains(i.SeriesId) && i.Rating != null && i.Rating > 0)
            .GroupBy(i => i.SeriesId)
            .Select(g => new { SeriesId = g.Key, Average = g.Average(i => i.Rating!.Value) })
            .ToDictionary(x => x.SeriesId, x => (float?)x.Average);

        int changed = 0;
        foreach (var series in context.Series.Where(s => ids.Contains(s.Id)).ToList())
        {
            float? rating = averages.TryGetValue(series.Id, out var average) ? average : null;
            if (series.Rating != rating)
            {
                series.Rating = rating;
                changed++;
            }
        }

        if (changed > 0)
        {
            context.SaveChanges();
        }

        return changed;
    }
}
