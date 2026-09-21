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

    /// <summary>ComicVine's budget is 200 requests an hour for everything, so its series lookups (publisher names) are capped far lower; they fill in over a few refreshes.</summary>
    public const int MaxComicVineSeriesLookupsPerRefresh = 40;

    public static readonly TimeSpan RefreshAge = TimeSpan.FromHours(12);

    /// <summary>A scheduled cycle refetches about twice a day; "Search now" may refetch after an hour.</summary>
    public static bool IsDue(PaperbunkrDbContext context, DateTime nowUtc, bool manual)
    {
        var last = context.GetOrCreateAcquisitionSettings().PullListRefreshedAt;
        return last is null || nowUtc - last.Value >= (manual ? TimeSpan.FromHours(1) : RefreshAge);
    }

    /// <summary>Fetches the window, stores it, and fills in series info (followed series' first). Returns the number of releases in the window.</summary>
    public static async Task<int> RefreshAsync(PaperbunkrDbContext context, IPullListSource source, DateTime today, CancellationToken cancellationToken,
        int? maxSeriesLookups = null)
    {
        var provider = source.Kind;
        int lookupCap = maxSeriesLookups ?? (provider == ComicProvider.ComicVine ? MaxComicVineSeriesLookupsPerRefresh : MaxSeriesLookupsPerRefresh);
        var entries = await source.GetReleasesAsync(today.Date.AddDays(-DaysBack), today.Date.AddDays(DaysAhead), cancellationToken).ConfigureAwait(false);
        Store(context, provider, entries, DateTime.UtcNow);
        await LookUpSeriesAsync(context, source, entries, lookupCap, cancellationToken).ConfigureAwait(false);

        context.GetOrCreateAcquisitionSettings().PullListRefreshedAt = DateTime.UtcNow;
        context.SaveChanges();
        return entries.Count;
    }

    /// <summary>Series looked up when a single week is fetched on demand; kept small so browsing the calendar can't spend the request budget.</summary>
    public const int MaxSeriesLookupsPerWeekFetch = 40;

    /// <summary>
    /// Fetches one date range the user navigated to (outside the cached window) and stores it without disturbing the rest of the cache. The next scheduled
    /// refresh replaces the whole cache again. Returns the number of releases found.
    /// </summary>
    public static async Task<int> FetchRangeAsync(PaperbunkrDbContext context, IPullListSource source, DateTime from, DateTime to, CancellationToken cancellationToken,
        int maxSeriesLookups = MaxSeriesLookupsPerWeekFetch)
    {
        var entries = await source.GetReleasesAsync(from.Date, to.Date, cancellationToken).ConfigureAwait(false);
        Store(context, source.Kind, entries, DateTime.UtcNow, from, to);
        await LookUpSeriesAsync(context, source, entries, maxSeriesLookups, cancellationToken).ConfigureAwait(false);
        return entries.Count;
    }

    private static async Task LookUpSeriesAsync(PaperbunkrDbContext context, IPullListSource source, IReadOnlyList<PullListEntry> entries, int lookupCap, CancellationToken cancellationToken)
    {
        var provider = source.Kind;
        var followedNames = context.WatchedSeries.Where(w => w.WatchFutureReleases && !w.IsPaused).Select(w => w.Name).ToList();
        var known = context.ReleaseSeries.Where(m => m.Provider == provider).Select(m => m.SeriesId).ToHashSet();
        var unknown = entries
            .GroupBy(e => e.SeriesId)
            .Where(g => !known.Contains(g.Key))
            .Select(g => (Id: g.Key, Name: g.First().SeriesName))
            .OrderByDescending(s => followedNames.Any(n => SeriesNames.Same(n, s.Name)))
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(lookupCap)
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

            context.ReleaseSeries.Add(new ReleaseSeriesInfo
            {
                Provider = provider,
                SeriesId = id,
                Name = info?.Name ?? name,
                Publisher = info?.Publisher,
                YearBegan = info?.YearBegan,
                ComicVineId = info?.ComicVineId,
                FetchedAt = DateTime.UtcNow,
            });
            context.SaveChanges();
        }
    }

    /// <summary>
    /// Upserts the fetched releases and drops every row the source did not return (aged out of the window, moved by the publisher, or left over from the other source: the
    /// list comes from one source at a time, so switching replaces it).
    /// </summary>
    public static void Store(PaperbunkrDbContext context, ComicProvider provider, IReadOnlyList<PullListEntry> entries, DateTime nowUtc,
        DateTime? scopeFrom = null, DateTime? scopeTo = null)
    {
        var existing = context.PullListReleases.ToDictionary(r => (r.Provider, r.ExternalIssueId));
        foreach (var entry in entries)
        {
            if (!existing.TryGetValue((provider, entry.IssueId), out var row))
            {
                row = new PullListRelease { Provider = provider, ExternalIssueId = entry.IssueId };
                context.PullListReleases.Add(row);
                existing[(provider, entry.IssueId)] = row;
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

        // A scoped store (one week fetched on demand) only replaces what lies inside its own dates; the rest of the cache stays.
        bool InScope(PullListRelease r) => scopeFrom is null || scopeTo is null || (r.StoreDate >= scopeFrom.Value.Date && r.StoreDate <= scopeTo.Value.Date);
        foreach (var stale in existing.Values.Where(r => r.Provider != provider || (!fetched.Contains(r.ExternalIssueId) && InScope(r))).ToList())
        {
            context.PullListReleases.Remove(stale);
        }

        context.SaveChanges();
    }

    /// <summary>Which source the cached list came from (Metron when there is none, as for the rows saved before ComicVine was an option).</summary>
    public static ComicProvider ListProvider(PaperbunkrDbContext context) =>
        context.PullListReleases.Select(r => (ComicProvider?)r.Provider).FirstOrDefault() ?? ComicProvider.Metron;

    /// <summary>
    /// The newest cached release cover for each of the given Metron series (a Metron series has no cover of its own, so search results borrow one from the weekly list
    /// when the series appears in it; a series that isn't in the window simply gets none - no request is spent looking).
    /// </summary>
    public static IReadOnlyDictionary<int, string> CachedCovers(PaperbunkrDbContext context, IEnumerable<int> seriesIds)
    {
        var ids = seriesIds.Distinct().ToList();
        return context.PullListReleases
            .Where(r => r.Provider == ComicProvider.Metron && ids.Contains(r.SeriesId) && r.CoverImageUrl != null)
            .OrderByDescending(r => r.StoreDate)
            .AsEnumerable()
            .GroupBy(r => r.SeriesId)
            .ToDictionary(g => g.Key, g => g.First().CoverImageUrl!);
    }

    /// <summary>
    /// The id, in the list's own source, of the series a watched series corresponds to: its own id when it is tracked on that source; for a ComicVine series and a Metron list,
    /// the Metron series whose ComicVine id matches. A Metron series against a ComicVine list has no such link (Metron's cross-reference is only cached for the series a Metron
    /// list contained), so it is not matched.
    /// </summary>
    public static int? ListSeriesIdFor(PaperbunkrDbContext context, WatchedSeries watched, ComicProvider listProvider)
    {
        if (watched.Provider == listProvider)
        {
            return watched.ExternalVolumeId;
        }

        if (listProvider == ComicProvider.Metron && watched.Provider == ComicProvider.ComicVine)
        {
            return context.ReleaseSeries.Where(m => m.Provider == ComicProvider.Metron && m.ComicVineId == watched.ExternalVolumeId).Select(m => (int?)m.SeriesId).FirstOrDefault();
        }

        return null;
    }

    /// <summary>
    /// Requests every release of a followed series that comes out today or later and isn't owned or already tracked, mirroring
    /// <see cref="WantedService.PromoteFollowedUpcoming"/> for the releases this list found. Returns how many were requested.
    /// </summary>
    public static int PromoteFollowedReleases(PaperbunkrDbContext context, DateTime today) => PromoteFollowedReleasesDetailed(context, today).Count;

    /// <summary>Like <see cref="PromoteFollowedReleases"/>, but returns the wants it made (with their series loaded) so the caller can announce them.</summary>
    public static IReadOnlyList<WantedIssue> PromoteFollowedReleasesDetailed(PaperbunkrDbContext context, DateTime today)
    {
        var created = new List<WantedIssue>();
        var followed = context.WatchedSeries.Where(w => w.WatchFutureReleases && !w.IsPaused).ToList();
        var upcoming = context.PullListReleases.Where(r => r.StoreDate >= today.Date && !r.IsHidden).ToList();
        var listProvider = ListProvider(context);

        foreach (var watched in followed)
        {
            if (ListSeriesIdFor(context, watched, listProvider) is not int seriesId)
            {
                continue;
            }

            foreach (var release in upcoming.Where(r => r.SeriesId == seriesId))
            {
                if (WantedService.RequestFromRelease(context, watched, release) is { } wanted)
                {
                    wanted.WatchedSeries = watched;
                    created.Add(wanted);
                }
            }
        }

        return created;
    }
}
