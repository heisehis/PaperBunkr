using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>Scans the local library for series whose identified Metron issues name a universe. No membership or metadata is written during discovery.</summary>
public static class ContinuityMetronSuggestionResolver
{
    public static async Task<IReadOnlyList<ContinuitySuggestion>> GetSuggestionsAsync(
        PaperbunkrDbContext context,
        IComicProvider provider,
        CancellationToken cancellationToken,
        IProgress<(int Done, int Total, string Series)>? progress = null)
    {
        var series = context.Series.AsNoTracking().OrderBy(s => s.Id).ToList();
        var alreadyLinked = context.ContinuityMemberships.AsNoTracking().Select(m => m.SeriesId).ToHashSet();
        var dismissed = context.ContinuityMetronSuggestionDismissals.AsNoTracking()
            .Select(d => new { d.SeriesId, d.MetronUniverseId }).ToList();
        var suggestions = new List<ContinuitySuggestion>();

        for (int index = 0; index < series.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = series[index];
            if (!alreadyLinked.Contains(item.Id))
            {
                var scan = await ContinuityMetronSeriesLookup.FindUniversesAsync(context, item.Id, provider, cancellationToken).ConfigureAwait(false);
                foreach (var universe in scan.Universes)
                {
                    if (dismissed.Any(d => d.SeriesId == item.Id && d.MetronUniverseId == universe.Id)) continue;
                    suggestions.Add(new ContinuitySuggestion(item, null, null, universe.Name, null, FormatSignalStrength.Strong,
                        "Metron lists this universe for an issue matched by a stored provider identity.", universe.Id, universe.Designation, universe.Publisher));
                }
            }

            progress?.Report((index + 1, series.Count, item.Name));
        }

        return suggestions;
    }

    public static void Dismiss(PaperbunkrDbContext context, int seriesId, int metronUniverseId)
    {
        if (context.ContinuityMetronSuggestionDismissals.Any(d => d.SeriesId == seriesId && d.MetronUniverseId == metronUniverseId)) return;
        context.ContinuityMetronSuggestionDismissals.Add(new ContinuityMetronSuggestionDismissal
        {
            SeriesId = seriesId,
            MetronUniverseId = metronUniverseId,
            DismissedAt = DateTime.UtcNow,
        });
        context.SaveChanges();
    }
}
