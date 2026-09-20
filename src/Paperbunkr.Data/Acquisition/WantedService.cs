using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Acquisition;

/// <summary>
/// The want-list rules (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §5, §7): what a watched
/// volume is missing, and Request / Watch / "I have this". Every method takes the caller's <see cref="PaperbunkrDbContext"/>
/// (the UI and the daemon each own theirs) and saves before returning. All are idempotent.
/// </summary>
public static class WantedService
{
    /// <summary>
    /// Starts tracking a ComicVine volume, or updates the existing row for it. <paramref name="seriesId"/> links the local
    /// series (kept if already set and none is passed). <paramref name="watchFutureReleases"/> is only ever turned <b>on</b>
    /// by this call, never silently off, so re-tracking can't undo a user's choice.
    /// </summary>
    public static WatchedSeries TrackVolume(PaperbunkrDbContext context, ComicVineVolume volume, int? seriesId, bool watchFutureReleases)
    {
        var watched = context.WatchedSeries.FirstOrDefault(w => w.ComicVineVolumeId == volume.Id);
        if (watched is null)
        {
            watched = new WatchedSeries { ComicVineVolumeId = volume.Id, AddedAt = DateTime.UtcNow };
            context.WatchedSeries.Add(watched);
        }

        watched.Name = volume.Name;
        watched.Publisher = volume.Publisher;
        watched.StartYear = volume.StartYear;
        watched.CoverImageUrl = volume.ImageUrl;
        watched.SeriesId ??= seriesId;
        watched.WatchFutureReleases |= watchFutureReleases;

        context.SaveChanges();
        return watched;
    }

    public static void SetWatchFutureReleases(PaperbunkrDbContext context, int watchedSeriesId, bool watch)
    {
        var watched = context.WatchedSeries.First(w => w.Id == watchedSeriesId);
        watched.WatchFutureReleases = watch;
        context.SaveChanges();
    }

    /// <summary>Replaces the cached ComicVine issue list for a volume (insert new, update changed, keep the rest).</summary>
    public static void RefreshCatalog(PaperbunkrDbContext context, WatchedSeries watched, IReadOnlyList<ComicVineIssue> issues)
    {
        var existing = context.CatalogIssues.Where(c => c.WatchedSeriesId == watched.Id).ToDictionary(c => c.ComicVineIssueId);

        foreach (var issue in issues)
        {
            if (!existing.TryGetValue(issue.Id, out var row))
            {
                // ComicVineIssueId is unique across volumes; a row that already lives under another volume is left alone.
                if (context.CatalogIssues.Any(c => c.ComicVineIssueId == issue.Id))
                {
                    continue;
                }

                row = new CatalogIssue { WatchedSeriesId = watched.Id, ComicVineIssueId = issue.Id };
                context.CatalogIssues.Add(row);
            }

            row.IssueNumber = issue.IssueNumber;
            row.Name = issue.Name;
            row.StoreDate = issue.StoreDate;
            row.CoverDate = issue.CoverDate;
            row.CoverImageUrl = issue.ImageUrl;
        }

        watched.LastRefreshedAt = DateTime.UtcNow;
        context.SaveChanges();
    }

    /// <summary>
    /// Catalog issues that are not owned (a real, present local issue of the linked series with the same number),
    /// not already wanted/snatched/imported, and not ignored - the "Missing Issues" list.
    /// </summary>
    public static IReadOnlyList<CatalogIssue> GetMissing(PaperbunkrDbContext context, WatchedSeries watched)
    {
        var owned = OwnedNumbers(context, watched);
        var taken = context.WantedIssues
            .Where(w => w.WatchedSeriesId == watched.Id)
            .Select(w => w.ComicVineIssueId)
            .ToHashSet();

        return context.CatalogIssues
            .Where(c => c.WatchedSeriesId == watched.Id)
            .AsEnumerable()
            .Where(c => !taken.Contains(c.ComicVineIssueId) && !owned.Any(n => IssueNumbers.Equal(n, c.IssueNumber)))
            .OrderBy(c => c.StoreDate ?? c.CoverDate ?? DateTime.MaxValue)
            .ThenBy(c => c.IssueNumber, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Marks an issue wanted. Already wanted/snatched/downloading/imported rows are left as they are; a failed or ignored one is put back to Wanted.</summary>
    public static WantedIssue Request(PaperbunkrDbContext context, WatchedSeries watched, ComicVineIssue issue)
    {
        var wanted = context.WantedIssues.FirstOrDefault(w => w.ComicVineIssueId == issue.Id);
        if (wanted is null)
        {
            wanted = new WantedIssue
            {
                WatchedSeriesId = watched.Id,
                ComicVineIssueId = issue.Id,
                CreatedAt = DateTime.UtcNow,
            };
            context.WantedIssues.Add(wanted);
        }
        else if (wanted.Status is WantedIssueStatus.Failed or WantedIssueStatus.Ignored)
        {
            wanted.Status = WantedIssueStatus.Wanted;
        }

        wanted.IssueNumber = issue.IssueNumber;
        wanted.Name = issue.Name;
        wanted.StoreDate = issue.StoreDate;
        wanted.CoverImageUrl = issue.ImageUrl;

        context.SaveChanges();
        return wanted;
    }

    public static WantedIssue Request(PaperbunkrDbContext context, WatchedSeries watched, CatalogIssue issue) =>
        Request(context, watched, new ComicVineIssue(issue.ComicVineIssueId, issue.IssueNumber, issue.Name, issue.StoreDate, issue.CoverDate, issue.CoverImageUrl, watched.ComicVineVolumeId));

    /// <summary>"Request all shown": every currently missing issue. Returns how many were newly requested.</summary>
    public static int RequestAllMissing(PaperbunkrDbContext context, WatchedSeries watched)
    {
        var missing = GetMissing(context, watched);
        foreach (var issue in missing)
        {
            Request(context, watched, issue);
        }

        return missing.Count;
    }

    /// <summary>"I have this / ignore": removes an issue from Missing without ever searching for it.</summary>
    public static WantedIssue Ignore(PaperbunkrDbContext context, WatchedSeries watched, CatalogIssue issue)
    {
        var wanted = context.WantedIssues.FirstOrDefault(w => w.ComicVineIssueId == issue.ComicVineIssueId);
        if (wanted is null)
        {
            wanted = new WantedIssue
            {
                WatchedSeriesId = watched.Id,
                ComicVineIssueId = issue.ComicVineIssueId,
                IssueNumber = issue.IssueNumber,
                Name = issue.Name,
                StoreDate = issue.StoreDate,
                CoverImageUrl = issue.CoverImageUrl,
                CreatedAt = DateTime.UtcNow,
            };
            context.WantedIssues.Add(wanted);
        }

        wanted.Status = WantedIssueStatus.Ignored;
        context.SaveChanges();
        return wanted;
    }

    /// <summary>
    /// Followed series (<see cref="WatchedSeries.WatchFutureReleases"/>, not paused): request every catalog issue whose store date is
    /// today or later that isn't owned or already tracked. Past gaps are never auto-requested - those are the user's "Request missing".
    /// Returns how many were requested.
    /// </summary>
    public static int PromoteFollowedUpcoming(PaperbunkrDbContext context, DateTime today)
    {
        int requested = 0;
        var followed = context.WatchedSeries.Where(w => w.WatchFutureReleases && !w.IsPaused).ToList();

        foreach (var watched in followed)
        {
            foreach (var issue in GetMissing(context, watched).Where(c => c.StoreDate is DateTime d && d.Date >= today.Date))
            {
                Request(context, watched, issue);
                requested++;
            }
        }

        return requested;
    }

    /// <summary>
    /// Closes every open want the library now owns (a file added by hand, a scan, another download): status <c>Imported</c>, linked to the owned issue,
    /// candidates dropped. Rows whose download is still in flight are returned so the caller can cancel an unfinished torrent that would otherwise
    /// finish and map to nothing. A completed download is left to import normally.
    /// </summary>
    public static IReadOnlyList<WantedIssue> CloseOwned(PaperbunkrDbContext context)
    {
        var open = context.WantedIssues.Include(w => w.WatchedSeries)
            .Where(w => w.Status == WantedIssueStatus.Wanted || w.Status == WantedIssueStatus.Snatched
                || w.Status == WantedIssueStatus.Downloading || w.Status == WantedIssueStatus.Failed)
            .ToList();

        var inFlight = new List<WantedIssue>();
        foreach (var group in open.Where(w => w.WatchedSeries?.SeriesId != null).GroupBy(w => w.WatchedSeries!.SeriesId!.Value))
        {
            var owned = context.Issues.Where(i => i.SeriesId == group.Key && !i.IsPlaceholder && !i.FileIsMissing).ToList();
            foreach (var wanted in group)
            {
                var match = owned.FirstOrDefault(i => IssueNumbers.Equal(i.Number, wanted.IssueNumber));
                if (match is null)
                {
                    continue;
                }

                bool wasInFlight = wanted.Status is WantedIssueStatus.Snatched or WantedIssueStatus.Downloading;
                wanted.Status = WantedIssueStatus.Imported;
                wanted.IssueId = match.Id;
                wanted.ImportedAt ??= DateTime.UtcNow;
                wanted.DownloadProgress = null;
                wanted.FailureReason = null;
                context.ReleaseCandidates.RemoveRange(context.ReleaseCandidates.Where(c => c.WantedIssueId == wanted.Id));
                if (wasInFlight && wanted.TorrentHash is not null)
                {
                    inFlight.Add(wanted);
                }
            }
        }

        context.SaveChanges();
        return inFlight;
    }

    /// <summary>Wanted rows still due in the future - the "Upcoming" list. (A due one is simply Wanted; there is no separate status.)</summary>
    public static IQueryable<WantedIssue> Upcoming(PaperbunkrDbContext context, DateTime today) =>
        context.WantedIssues.Where(w => w.Status == WantedIssueStatus.Wanted && w.StoreDate != null && w.StoreDate > today.Date);

    /// <summary>Wanted rows that are due now (store date passed or unknown) - what the daemon searches for.</summary>
    public static IQueryable<WantedIssue> Due(PaperbunkrDbContext context, DateTime today) =>
        context.WantedIssues.Where(w => w.Status == WantedIssueStatus.Wanted && (w.StoreDate == null || w.StoreDate <= today.Date));

    private static List<string?> OwnedNumbers(PaperbunkrDbContext context, WatchedSeries watched)
    {
        if (watched.SeriesId is not int seriesId)
        {
            return new List<string?>();
        }

        // A placeholder is "in a list but not owned", and a file flagged missing isn't here either.
        return context.Issues
            .Where(i => i.SeriesId == seriesId && !i.IsPlaceholder && !i.FileIsMissing)
            .Select(i => i.Number)
            .ToList();
    }
}
