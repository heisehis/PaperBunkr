using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>What one <see cref="MetronAssociationFetch.RunAsync"/> did.</summary>
public sealed record MetronAssociationFetchResult(int Resolved, int Checked, int IdsFilled, int Failed);

/// <summary>
/// The series connector's network half (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.1, §5.1), three passes:
/// <list type="number">
/// <item>Give series with no Metron id one, found by their ComicVine id, then by their Grand Comics Database id (exactly one hit only).</item>
/// <item>Fetch Metron's associated series for every series with a Metron id that's due a check (never, or over 30 days ago).</item>
/// <item>For associations that don't name a series you have by Metron id, read that series' ComicVine and GCD ids off its Metron record,
/// so <see cref="MetronAssociationMapping"/> can match it another way.</item>
/// </list>
/// Each lookup is remembered for 30 days (a miss too), so the first run is the long one and later runs ask about little.
/// A failed call leaves its stamp unset and is retried next run. Cancellation stops between series.
/// </summary>
public static class MetronAssociationFetch
{
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(30);

    public static async Task<MetronAssociationFetchResult> RunAsync(
        Func<PaperbunkrDbContext> contextFactory,
        IMetronSeriesLinks metron,
        DateTime nowUtc,
        CancellationToken cancellationToken,
        IProgress<(int Done, int Total)>? progress = null)
    {
        int resolved = 0, @checked = 0, filled = 0, failed = 0;
        DateTime due = nowUtc - RecheckAfter;

        // Pass 1: Metron ids for series that only have a ComicVine or GCD id.
        List<(int SeriesId, int? ComicVine, int? Gcd)> unresolved;
        using (var context = contextFactory())
        {
            var metronIds = MetronAssociationMapping.SeriesIdsByExternalId(context, ComicProvider.Metron).Values.SelectMany(v => v).ToHashSet();
            var comicVineBySeries = MetronAssociationMapping.SeriesIdsByExternalId(context, ComicProvider.ComicVine)
                .SelectMany(kv => kv.Value.Select(id => (SeriesId: id, ExternalId: kv.Key)))
                .GroupBy(x => x.SeriesId)
                .ToDictionary(g => g.Key, g => int.TryParse(g.First().ExternalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cv) ? cv : (int?)null);
            unresolved = context.Series.AsNoTracking()
                .Where(s => s.MetronAssociationsCheckedUtc == null || s.MetronAssociationsCheckedUtc < due)
                .Select(s => new { s.Id, s.GcdSeriesId })
                .AsEnumerable()
                .Where(s => !metronIds.Contains(s.Id))
                .Select(s => (s.Id, comicVineBySeries.GetValueOrDefault(s.Id), s.GcdSeriesId))
                .Where(s => s.Item2 is not null || s.GcdSeriesId is not null)
                .ToList();
        }

        // Pass 2's list is only known after pass 1, so progress counts pass 1 + the series already holding a Metron id.
        int total = unresolved.Count;
        int done = 0;
        foreach (var (seriesId, comicVine, gcd) in unresolved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                int? metronId = null;
                if (comicVine is int cv)
                {
                    metronId = Single(await metron.FindSeriesByComicVineIdAsync(cv, cancellationToken).ConfigureAwait(false));
                }

                if (metronId is null && gcd is int g)
                {
                    metronId = Single(await metron.FindSeriesByGcdIdAsync(g, cancellationToken).ConfigureAwait(false));
                }

                using var context = contextFactory();
                string? key = metronId?.ToString(CultureInfo.InvariantCulture);
                if (key is not null && !context.ComicMetadataExternalIds.Any(x => x.EntityKind == ComicMetadataEntityKind.Series && x.Provider == ComicProvider.Metron && x.ExternalId == key))
                {
                    context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId
                    {
                        EntityKind = ComicMetadataEntityKind.Series, EntityId = seriesId, Provider = ComicProvider.Metron, ExternalId = key,
                    });
                    resolved++;
                }
                else if (context.Series.Find(seriesId) is { } series)
                {
                    // Nothing found (or the Metron id already belongs to another series): don't ask again for 30 days.
                    series.MetronAssociationsCheckedUtc = nowUtc;
                }

                context.SaveChanges();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
            }

            progress?.Report((++done, total));
        }

        // Pass 2: associated series for every series with a Metron id that's due.
        List<(int SeriesId, int MetronId)> dueSeries;
        using (var context = contextFactory())
        {
            var checkedAt = context.Series.AsNoTracking().Select(s => new { s.Id, s.MetronAssociationsCheckedUtc }).ToDictionary(s => s.Id, s => s.MetronAssociationsCheckedUtc);
            dueSeries = MetronAssociationMapping.SeriesIdsByExternalId(context, ComicProvider.Metron)
                .SelectMany(kv => kv.Value.Select(id => (SeriesId: id, Key: kv.Key)))
                .Where(x => checkedAt.TryGetValue(x.SeriesId, out var at) && (at is null || at < due))
                .Select(x => (x.SeriesId, MetronId: int.TryParse(x.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) ? m : 0))
                .Where(x => x.MetronId > 0)
                .ToList();
        }

        total += dueSeries.Count;
        foreach (var (seriesId, metronId) in dueSeries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var associated = await metron.GetAssociatedSeriesAsync(metronId, cancellationToken).ConfigureAwait(false);
                using var context = contextFactory();
                SeriesAssociationSync.Sync(context, seriesId, associated);
                if (context.Series.Find(seriesId) is { } series)
                {
                    series.MetronAssociationsCheckedUtc = nowUtc;
                    context.SaveChanges();
                }

                @checked++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
            }

            progress?.Report((++done, total));
        }

        // Pass 3: ComicVine/GCD ids for associations that name a Metron series you don't have under that id.
        List<int> toFill;
        using (var context = contextFactory())
        {
            var localMetron = MetronAssociationMapping.SeriesIdsByExternalId(context, ComicProvider.Metron).Keys.ToHashSet();
            toFill = context.SeriesAssociations.AsNoTracking()
                .Where(a => a.Provider == ComicProvider.Metron && (a.IdsCheckedUtc == null || a.IdsCheckedUtc < due))
                .Select(a => new { a.Id, a.ExternalSeriesId })
                .AsEnumerable()
                .Where(a => !localMetron.Contains(a.ExternalSeriesId))
                .Select(a => a.Id)
                .ToList();
        }

        // One Metron call per associated series, even when several of yours name it.
        var infoCache = new Dictionary<int, PullListSeriesInfo?>();
        foreach (int associationId in toFill)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var context = contextFactory();
                var row = context.SeriesAssociations.Find(associationId);
                if (row is null || !int.TryParse(row.ExternalSeriesId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int metronId))
                {
                    continue;
                }

                if (!infoCache.TryGetValue(metronId, out var info))
                {
                    info = await metron.GetSeriesInfoAsync(metronId, cancellationToken).ConfigureAwait(false);
                    infoCache[metronId] = info;
                }

                row.ComicVineSeriesId = info?.ComicVineId;
                row.GcdSeriesId = info?.GcdId;
                row.IdsCheckedUtc = nowUtc;
                context.SaveChanges();
                filled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
            }
        }

        return new MetronAssociationFetchResult(resolved, @checked, filled, failed);
    }

    private static int? Single(IReadOnlyList<ComicSeriesHit> hits)
    {
        var ids = hits.Select(h => h.SeriesId).Distinct().ToList();
        return ids.Count == 1 ? ids[0] : null;
    }
}
