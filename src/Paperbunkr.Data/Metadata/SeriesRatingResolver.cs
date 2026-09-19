using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Computes <see cref="Series.Rating"/> from the series' own <see cref="Issue.Rating"/> values
/// (docs/superpowers/specs/2026-09-18-per-tracker-score-and-finish-date-design.md) - pure function,
/// caller's own responsibility to assign the result and <c>SaveChanges()</c>, same "static, no
/// context" shape as <see cref="ProviderRelationTypeMapper"/>.
/// </summary>
public static class SeriesRatingResolver
{
    /// <summary>Average of every rated issue's <see cref="Issue.Rating"/> - unrated issues
    /// (<see langword="null"/>) are excluded entirely, never treated as 0. Null when no issue in
    /// the series has a rating yet, not 0.</summary>
    public static float? Recompute(Series series)
    {
        var rated = series.Issues.Where(i => i.Rating.HasValue).Select(i => i.Rating!.Value).ToList();
        return rated.Count > 0 ? rated.Average() : null;
    }
}
