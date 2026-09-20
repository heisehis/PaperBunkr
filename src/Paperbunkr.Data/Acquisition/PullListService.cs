using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Acquisition;

/// <summary>
/// The weekly pull list (docs/superpowers/specs/2026-09-20-weekly-pull-list-design.md): fetch every release in a window around today, cache it, learn which series
/// (and publishers) the releases belong to, and turn releases of followed series into Upcoming wants. Network work lives in <see cref="RefreshAsync"/>; everything else is
/// plain database work so the Releases tab and the tests can use it without a source.
/// </summary>
public static class PullListService
{
    /// <summary>Last week is kept so "what came out" is still visible; the horizon is how far ahead the list reaches.</summary>
    public const int DaysBack = 7;
    public const int DaysAhead = 28;

    /// <summary>Series looked up per refresh at most (one request each; the results are cached for good).</summary>
    public const int MaxSeriesLookupsPerRefresh = 150;

    public static readonly TimeSpan RefreshAge = TimeSpan.FromHours(12);

    /// <summary>A scheduled cycle refetches about twice a day; "Search now" may refetch after an hour.</summary>
    public static bool IsDue(PaperbunkrDbContext context, DateTime nowUtc, bool manual)
    {
        var last = context.GetOrCreateAcquisitionSettings().PullListRefreshedAt;
        return last is null || nowUtc - last.Value >= (manual ? TimeSpan.FromHours(1) : RefreshAge);
    }

    /// <summary>Fetches the window, stores it, and fills in series info (followed series' first). Returns the number of releases in the window.</summary>
    public static async Task<int> RefreshAsync(PaperbunkrDbContext context, IPullListSource source, DateTime today, CancellationToken cancellationToken,
        int maxSeriesLookups = MaxSeriesLookupsPerRefresh)
    {
        var entries = await source.GetReleasesAsync(today.Date.AddDays(-DaysBack), today.Date.AddDays(DaysAhead), cancellationToken).ConfigureAwait(false);
        Store(context, entries, DateTime.UtcNow);

        var followedNames = context.WatchedSeries.Where(w => w.WatchFutureReleases && !w.IsPaused).Select(w => w.Name).ToList();
        var known = context.MetronSeries.Select(m => m.SeriesId).ToHashSet();
        var unknown = entries
            .GroupBy(e => e.SeriesId)
            .Where(g => !known.Contains(g.Key))
            .Select(g => (Id: g.Key, Name: g.First().SeriesName))
            .OrderByDescending(s => followedNames.Any(n => SeriesNames.Same(n, s.Name)))
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxSeriesLookups)
            .ToList();

        foreach (var (id, name) in unknown)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PullListSeriesInfo? info;
            try
            {
                info = await source.GetSeriesInfoAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (ComicVineException ex) when (ex.ApiStatusCode == 107)
            {
                break; // rate limited: the rest is looked up on a later refresh
            }

            context.MetronSeries.Add(new MetronSeriesInfo
            {
                SeriesId = id,
                Name = info?.Name ?? name,
                Publisher = info?.Publisher,
                YearBegan = info?.YearBegan,
                ComicVineId = info?.ComicVineId,
                FetchedAt = DateTime.UtcNow,
            });
            context.SaveChanges();
        }

        context.GetOrCreateAcquisitionSettings().PullListRefreshedAt = DateTime.UtcNow;
        context.SaveChanges();
        return entries.Count;
    }

    /// <summary>Upserts the fetched releases and drops rows the source no longer returns (aged out of the window, or moved by the publisher).</summary>
    public static void Store(PaperbunkrDbContext context, IReadOnlyList<PullListEntry> entries, DateTime nowUtc)
    {
        var existing = context.PullListReleases.ToDictionary(r => r.ExternalIssueId);
        foreach (var entry in entries)
        {
            if (!existing.TryGetValue(entry.IssueId, out var row))
            {
                row = new PullListRelease { ExternalIssueId = entry.IssueId };
                context.PullListReleases.Add(row);
                existing[entry.IssueId] = row;
            }

            row.SeriesId = entry.SeriesId;
            row.SeriesName = entry.SeriesName;
            row.IssueNumber = entry.IssueNumber;
            row.StoreDate = entry.StoreDate;
            row.CoverDate = entry.CoverDate;
            row.CoverImageUrl = entry.ImageUrl;
            row.FetchedAt = nowUtc;
        }

        var fetched = entries.Select(e => e.IssueId).ToHashSet();
        foreach (var stale in existing.Values.Where(r => !fetched.Contains(r.ExternalIssueId)).ToList())
        {
            context.PullListReleases.Remove(stale);
        }

        context.SaveChanges();
    }

    /// <summary>The Metron series id a watched series corresponds to: its own id for a Metron series, or the one whose ComicVine id matches for a ComicVine series.</summary>
    public static int? MetronSeriesIdFor(PaperbunkrDbContext context, WatchedSeries watched)
    {
        if (watched.Provider == ComicProvider.Metron)
        {
            return watched.ExternalVolumeId;
        }

        return context.MetronSeries.Where(m => m.ComicVineId == watched.ExternalVolumeId).Select(m => (int?)m.SeriesId).FirstOrDefault();
    }

    /// <summary>
    /// Requests every release of a followed series that comes out today or later and isn't owned or already tracked, mirroring
    /// <see cref="WantedService.PromoteFollowedUpcoming"/> for the releases this list found. Returns how many were requested.
    /// </summary>
    public static int PromoteFollowedReleases(PaperbunkrDbContext context, DateTime today)
    {
        int requested = 0;
        var followed = context.WatchedSeries.Where(w => w.WatchFutureReleases && !w.IsPaused).ToList();
        var upcoming = context.PullListReleases.Where(r => r.StoreDate >= today.Date).ToList();

        foreach (var watched in followed)
        {
            if (MetronSeriesIdFor(context, watched) is not int seriesId)
            {
                continue;
            }

            foreach (var release in upcoming.Where(r => r.SeriesId == seriesId))
            {
                if (WantedService.RequestFromRelease(context, watched, release) is not null)
                {
                    requested++;
                }
            }
        }

        return requested;
    }
}
