using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Acquisition;

/// <summary>An arc/list entry that couldn't be turned into a request, with why (shown to the user, never silently dropped).</summary>
public sealed record UnresolvedRequest(string Series, string Number, string Reason);

public sealed record ArcRequestResult(int Requested, int AlreadyTracked, IReadOnlyList<UnresolvedRequest> Unresolved);

/// <summary>
/// "Request missing" for reading lists (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §8). A list's
/// placeholder <see cref="Issue"/>s are its missing entries. Arc sources hand back only a series name, number and year - no
/// ComicVine ids - so each series is matched to a ComicVine volume here, and each placeholder to that volume's issue by number.
/// The match is deliberately conservative: an ambiguous volume is reported as unresolved rather than guessed, because a wrong
/// guess would download the wrong series.
/// <para>
/// A placeholder always already has a local <see cref="Series"/> (the reading-list matcher creates one), so no series is created
/// here. A <see cref="WatchedSeries"/> is created with <c>WatchFutureReleases = false</c>: requesting one crossover issue must never
/// silently subscribe the user to the whole series.
/// </para>
/// </summary>
public static class ArcRequestService
{
    /// <summary>How old a cached catalog may be before a number that isn't in it triggers one refresh (new issues appear over time).</summary>
    private static readonly TimeSpan StaleCatalog = TimeSpan.FromHours(6);

    /// <summary>Bulk action for an arc-linked list (<c>Source</c> + <c>ArcId</c> set).</summary>
    public static async Task<ArcRequestResult> RequestMissingAsync(PaperbunkrDbContext context, int readingListId, IComicVineClient comicVine, CancellationToken cancellationToken)
    {
        var list = context.ReadingLists
            .Include(l => l.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.Series)
            .First(l => l.Id == readingListId);

        if (string.IsNullOrEmpty(list.Source) || string.IsNullOrEmpty(list.ArcId))
        {
            throw new InvalidOperationException("Bulk \"Request missing\" is only available on lists built from a story arc. Request individual items instead.");
        }

        var placeholders = list.Items.Select(i => i.Issue).OfType<Issue>().Where(i => i.IsPlaceholder).ToList();
        return await RequestPlaceholdersAsync(context, placeholders, comicVine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests the given placeholder issues (one, for a per-item Request on any list; many, for a bulk one).</summary>
    public static async Task<ArcRequestResult> RequestPlaceholdersAsync(PaperbunkrDbContext context, IReadOnlyList<Issue> placeholders, IComicVineClient comicVine, CancellationToken cancellationToken)
    {
        int requested = 0, already = 0;
        var unresolved = new List<UnresolvedRequest>();

        foreach (var group in placeholders.Where(p => p.IsPlaceholder).GroupBy(p => p.SeriesId))
        {
            var series = group.First().Series ?? context.Series.First(s => s.Id == group.Key);
            var items = group.ToList();

            WatchedSeries? watched;
            try
            {
                watched = await ResolveWatchedSeriesAsync(context, series, items, comicVine, unresolved, cancellationToken).ConfigureAwait(false);
            }
            catch (ComicVineException ex) when (ex.ApiStatusCode is not (100 or 107))
            {
                // A per-series lookup failure (network blip, ComicVine 5xx) affects only that series.
                unresolved.AddRange(items.Select(i => new UnresolvedRequest(series.Name, i.Number ?? "?", ex.Message)));
                continue;
            }

            if (watched is null)
            {
                continue; // ResolveWatchedSeriesAsync already recorded why
            }

            var catalog = await EnsureCatalogAsync(context, watched, comicVine, cancellationToken).ConfigureAwait(false);
            bool refreshedForMissing = false;

            foreach (var item in items)
            {
                var entry = catalog.FirstOrDefault(c => IssueNumbers.Equal(c.IssueNumber, item.Number));

                if (entry is null && !refreshedForMissing && IsStale(watched))
                {
                    refreshedForMissing = true;
                    catalog = await RefreshAsync(context, watched, comicVine, cancellationToken).ConfigureAwait(false);
                    entry = catalog.FirstOrDefault(c => IssueNumbers.Equal(c.IssueNumber, item.Number));
                }

                if (entry is null)
                {
                    unresolved.Add(new UnresolvedRequest(series.Name, item.Number ?? "?", $"Issue not found in the ComicVine volume \"{watched.Name}\" ({watched.StartYear})."));
                    continue;
                }

                var existing = context.WantedIssues.FirstOrDefault(w => w.ComicVineIssueId == entry.ComicVineIssueId);
                if (existing is not null && existing.Status is not (WantedIssueStatus.Failed or WantedIssueStatus.Ignored))
                {
                    already++;
                    continue;
                }

                WantedService.Request(context, watched, entry);
                requested++;
            }
        }

        return new ArcRequestResult(requested, already, unresolved);
    }

    private static async Task<WatchedSeries?> ResolveWatchedSeriesAsync(PaperbunkrDbContext context, Series series, IReadOnlyList<Issue> items,
        IComicVineClient comicVine, List<UnresolvedRequest> unresolved, CancellationToken cancellationToken)
    {
        var existing = context.WatchedSeries.FirstOrDefault(w => w.SeriesId == series.Id);
        if (existing is not null)
        {
            return existing;
        }

        var volumes = await comicVine.SearchVolumesAsync(series.Name, cancellationToken).ConfigureAwait(false);
        int? year = items.Select(i => i.Year).Where(y => y is > 0).Min();

        var chosen = ChooseVolume(volumes, series.Name, year, out var reason);
        if (chosen is null)
        {
            unresolved.AddRange(items.Select(i => new UnresolvedRequest(series.Name, i.Number ?? "?", reason)));
            return null;
        }

        // A volume already tracked under another local series is reused as-is (its link is kept).
        return WantedService.TrackVolume(context, chosen, series.Id, watchFutureReleases: false);
    }

    /// <summary>
    /// Exact (punctuation/"the"-insensitive) name match only. Several volumes with the same name are told apart by the arc's year -
    /// the newest volume that had already started by then; with no year, only a unique biggest run is accepted.
    /// </summary>
    internal static ComicVineVolume? ChooseVolume(IReadOnlyList<ComicVineVolume> volumes, string seriesName, int? year, out string reason)
    {
        var exact = volumes.Where(v => SeriesNames.Same(v.Name, seriesName)).ToList();
        if (exact.Count == 0)
        {
            reason = $"No ComicVine volume named \"{seriesName}\".";
            return null;
        }

        if (exact.Count == 1)
        {
            reason = string.Empty;
            return exact[0];
        }

        if (year is int y)
        {
            var started = exact.Where(v => v.StartYear is int s && s <= y).ToList();
            if (started.Count > 0)
            {
                reason = string.Empty;
                return started.OrderByDescending(v => v.StartYear).ThenByDescending(v => v.CountOfIssues).First();
            }
        }
        else
        {
            var biggest = exact.OrderByDescending(v => v.CountOfIssues).ToList();
            if (biggest[0].CountOfIssues > biggest[1].CountOfIssues)
            {
                reason = string.Empty;
                return biggest[0];
            }
        }

        reason = $"\"{seriesName}\" matches {exact.Count} ComicVine volumes and the year doesn't decide between them - track the right one from its series page.";
        return null;
    }

    private static async Task<List<CatalogIssue>> EnsureCatalogAsync(PaperbunkrDbContext context, WatchedSeries watched, IComicVineClient comicVine, CancellationToken cancellationToken)
    {
        if (watched.LastRefreshedAt is null)
        {
            return await RefreshAsync(context, watched, comicVine, cancellationToken).ConfigureAwait(false);
        }

        return context.CatalogIssues.Where(c => c.WatchedSeriesId == watched.Id).ToList();
    }

    private static async Task<List<CatalogIssue>> RefreshAsync(PaperbunkrDbContext context, WatchedSeries watched, IComicVineClient comicVine, CancellationToken cancellationToken)
    {
        var issues = await comicVine.GetVolumeIssuesAsync(watched.ComicVineVolumeId, cancellationToken).ConfigureAwait(false);
        WantedService.RefreshCatalog(context, watched, issues);
        return context.CatalogIssues.Where(c => c.WatchedSeriesId == watched.Id).ToList();
    }

    private static bool IsStale(WatchedSeries watched) =>
        watched.LastRefreshedAt is null || DateTime.UtcNow - watched.LastRefreshedAt.Value > StaleCatalog;
}
