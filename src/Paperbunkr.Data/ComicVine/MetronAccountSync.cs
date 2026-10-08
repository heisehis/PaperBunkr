using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ComicVine;

/// <summary>What one sync run did, for the Activity Center line and the tests.</summary>
public sealed record MetronSyncReport
{
    public int PullListAdded { get; init; }
    public int PullListRemoved { get; init; }
    public int SeriesFollowed { get; init; }
    public int ReadsSent { get; init; }
    public int WishListAdded { get; init; }
    public int WishListRemoved { get; init; }
    public int WishListAcquired { get; init; }
    public int CollectionAdded { get; init; }
    public int RatingsSent { get; init; }
    public int Requests { get; init; }

    /// <summary>Why the run ended before everything was sent; null when it finished. What was left is still a gap, so the next run picks it up.</summary>
    public string? StoppedBecause { get; init; }

    /// <summary>True when <see cref="StoppedBecause"/> is something the user has to act on (a refused login, Metron down) rather than the routine budget or rate limit.</summary>
    public bool Failed { get; init; }

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            void Add(int count, string one, string many)
            {
                if (count > 0)
                {
                    parts.Add(count == 1 ? one : string.Format(CultureInfo.InvariantCulture, many, count));
                }
            }

            Add(PullListAdded, "1 series added to the pull list", "{0} series added to the pull list");
            Add(PullListRemoved, "1 series removed from the pull list", "{0} series removed from the pull list");
            Add(SeriesFollowed, "1 series followed from the pull list", "{0} series followed from the pull list");
            Add(ReadsSent, "1 read sent", "{0} reads sent");
            Add(WishListAdded, "1 issue added to the wish list", "{0} issues added to the wish list");
            Add(WishListRemoved, "1 issue removed from the wish list", "{0} issues removed from the wish list");
            Add(WishListAcquired, "1 wish-list issue marked acquired", "{0} wish-list issues marked acquired");
            Add(CollectionAdded, "1 issue added to the collection", "{0} issues added to the collection");
            Add(RatingsSent, "1 rating sent", "{0} ratings sent");

            string done = parts.Count == 0 ? "Nothing to send" : string.Join(" · ", parts);
            return StoppedBecause is null ? done : $"{done}. Stopped early: {StoppedBecause}";
        }
    }
}

public sealed record MetronImportReport(int Matched, int RatingsFilled, int MarkedRead, string? StoppedBecause)
{
    public string Summary
    {
        get
        {
            string done = Matched == 0
                ? "Nothing in your Metron collection matches an issue in this library"
                : $"{Matched} collection item{(Matched == 1 ? "" : "s")} matched · {RatingsFilled} rating{(RatingsFilled == 1 ? "" : "s")} filled in · {MarkedRead} marked read";
            return StoppedBecause is null ? done : $"{done}. Stopped early: {StoppedBecause}";
        }
    }
}

/// <summary>
/// Keeps the user's Metron account in step with the library (docs/superpowers/specs/2026-10-05-metron-account-
/// sync-design.md). It reconciles rather than reacts: each run compares what the database says should be on
/// Metron with the <see cref="MetronSyncLink"/> ledger of what has been sent, and makes the calls that close the
/// gap. Follows, wants, reads and ratings change in a dozen view models and in the daemon process; none of them
/// needs to know this exists, and a run cut short by the rate limit or a dropped connection loses nothing.
///
/// A run spends at most <c>requestBudget</c> requests, at background priority, cheapest and most visible areas
/// first (pull list, reads, wish list) with the bulk collection upload taking what is left - a large library's
/// first upload is spread over many runs on purpose.
/// </summary>
public sealed class MetronAccountSync(
    Func<PaperbunkrDbContext> createContext,
    IMetronAccount account,
    IComicIssueLookup? lookup = null,
    Func<DateTime>? now = null,
    int requestBudget = MetronAccountSync.DefaultRequestBudget)
{
    /// <summary>Six minutes of Metron's 20-a-minute allowance: enough to be useful every hour, not enough to crowd out anything else.</summary>
    public const int DefaultRequestBudget = 120;

    private const int ImportPageBudget = 60;

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly Dictionary<int, int?> _resolved = new();
    private int _requests;

    /// <summary>Ends a run. Not an error to the caller: whatever was not sent is still a gap for the next run.</summary>
    private sealed class StopRun(string reason, bool failed) : Exception(reason)
    {
        public bool Failed { get; } = failed;
    }

    /// <summary>Metron answered that this one thing can't be done (no such issue, a bad request). Retrying won't change that.</summary>
    private sealed class Rejected : Exception;

    public const string AlreadyRunningMessage = "another sync with Metron is already running; this one was not started";

    /// <summary>
    /// One sync at a time, process-wide. The hourly task and the Sync now button can land together, and two runs reconciling the same ledger both try to
    /// record the same row (and send the same request twice). The second one is turned away rather than queued: the first is already doing its work.
    /// </summary>
    private static readonly SemaphoreSlim RunGate = new(1, 1);

    public async Task<MetronSyncReport> RunAsync(CancellationToken cancellationToken)
    {
        if (!await RunGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new MetronSyncReport { StoppedBecause = AlreadyRunningMessage };
        }

        try
        {
            return await RunCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RunGate.Release();
        }
    }

    private async Task<MetronSyncReport> RunCoreAsync(CancellationToken cancellationToken)
    {
        bool pullList, reading, wishList, collection;
        using (var context = createContext())
        {
            var settings = context.GetOrCreateAppSettings();
            if (!settings.MetronSyncEnabled)
            {
                return new MetronSyncReport();
            }

            (pullList, reading, wishList, collection) = (settings.MetronSyncPullList, settings.MetronSyncReading, settings.MetronSyncWishList, settings.MetronSyncCollection);
        }

        var report = new MetronSyncReport();
        try
        {
            if (pullList)
            {
                report = await SyncPullListAsync(report, cancellationToken).ConfigureAwait(false);
            }

            if (reading)
            {
                report = await SyncReadingAsync(report, cancellationToken).ConfigureAwait(false);
            }

            if (wishList)
            {
                report = await SyncWishListAsync(report, cancellationToken).ConfigureAwait(false);
            }

            if (collection)
            {
                report = await SyncCollectionAsync(report, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (StopRun stop)
        {
            // Each area hands its counts back through Current as it goes, so a stop keeps what was done.
            report = Current with { StoppedBecause = stop.Message, Failed = stop.Failed };
        }

        return report with { Requests = _requests };
    }

    /// <summary>The report as it stands, kept up to date by every area so a <see cref="StopRun"/> mid-area still reports what was sent before it.</summary>
    private MetronSyncReport Current { get; set; } = new();

    // ---- Pull list ----------------------------------------------------------------------------------

    private async Task<MetronSyncReport> SyncPullListAsync(MetronSyncReport report, CancellationToken cancellationToken)
    {
        Current = report;
        var remote = await Call(() => account.GetPullListAsync(cancellationToken)).ConfigureAwait(false);
        var remoteIds = remote.Select(r => r.SeriesId).ToHashSet();
        var onMetronAtStart = remoteIds.ToHashSet();

        using var context = createContext();
        var followed = context.WatchedSeries.Where(w => w.Provider == ComicProvider.Metron && w.WatchFutureReleases).ToList();
        var links = context.MetronSyncLinks.Where(l => l.Kind == MetronSyncKind.PullListSeries).ToList();
        var linkByLocal = links.ToDictionary(l => l.LocalId);
        var followedIds = followed.Select(w => w.Id).ToHashSet();

        // Unfollowed here since it was sent: take it off Metron too. One the user already removed there is just forgotten.
        foreach (var link in links.Where(l => !followedIds.Contains(l.LocalId)).ToList())
        {
            if (link.State == MetronSyncState.Synced && remoteIds.Contains(link.RemoteId))
            {
                await Call(() => account.RemoveFromPullListAsync(link.RemoteId, cancellationToken)).ConfigureAwait(false);
                remoteIds.Remove(link.RemoteId);
                Current = Current with { PullListRemoved = Current.PullListRemoved + 1 };
            }

            context.MetronSyncLinks.Remove(link);
            linkByLocal.Remove(link.LocalId);
            context.SaveChanges();
        }

        foreach (var watched in followed)
        {
            if (linkByLocal.TryGetValue(watched.Id, out var link))
            {
                // Sent before, gone from Metron now: the user removed it there. Not pushed back, and the follow here stays.
                if (link.State == MetronSyncState.Synced && !onMetronAtStart.Contains(link.RemoteId))
                {
                    link.State = MetronSyncState.RemovedRemotely;
                    context.SaveChanges();
                }

                continue;
            }

            if (!remoteIds.Contains(watched.ExternalVolumeId))
            {
                await Call(() => account.AddToPullListAsync(watched.ExternalVolumeId, cancellationToken)).ConfigureAwait(false);
                remoteIds.Add(watched.ExternalVolumeId);
                Current = Current with { PullListAdded = Current.PullListAdded + 1 };
            }

            context.MetronSyncLinks.Add(NewLink(MetronSyncKind.PullListSeries, watched.Id, watched.ExternalVolumeId));
            context.SaveChanges();
        }

        // On Metron and not followed here: follow it. Costs no request - the list already named it.
        var knownRemote = context.MetronSyncLinks.Where(l => l.Kind == MetronSyncKind.PullListSeries).Select(l => l.RemoteId).ToHashSet();
        foreach (var series in remote.Where(r => remoteIds.Contains(r.SeriesId) && !knownRemote.Contains(r.SeriesId)))
        {
            var watched = WantedService.TrackVolume(context, new ComicVineVolume(series.SeriesId, series.Name, null, series.YearBegan, 0, null), seriesId: null, watchFutureReleases: true, ComicProvider.Metron);
            context.MetronSyncLinks.Add(NewLink(MetronSyncKind.PullListSeries, watched.Id, series.SeriesId));
            context.SaveChanges();
            Current = Current with { SeriesFollowed = Current.SeriesFollowed + 1 };
        }

        return Current;
    }

    // ---- Reading ------------------------------------------------------------------------------------

    private async Task<MetronSyncReport> SyncReadingAsync(MetronSyncReport report, CancellationToken cancellationToken)
    {
        Current = report;
        using var context = createContext();
        var settings = context.GetOrCreateAppSettings();

        while (true)
        {
            int watermark = settings.MetronSyncReadingEventId;
            var batch = context.ReadingEvents
                .Where(e => e.Id > watermark && e.Kind == ReadingEventKind.Finished && e.ItemType == ReadingItemType.Comic)
                .OrderBy(e => e.Id)
                .Take(50)
                .ToList();
            if (batch.Count == 0)
            {
                // Nothing left to send: step over whatever else was recorded (opens, novels) so it isn't scanned again.
                int newest = context.ReadingEvents.Max(e => (int?)e.Id) ?? 0;
                if (newest > settings.MetronSyncReadingEventId)
                {
                    settings.MetronSyncReadingEventId = newest;
                    context.SaveChanges();
                }

                return Current;
            }

            foreach (var read in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A read this app imported *from* Metron is already there; sending it back would double it.
                var imported = context.MetronSyncLinks.FirstOrDefault(l => l.Kind == MetronSyncKind.ImportedRead && l.LocalId == read.Id);
                if (imported is not null)
                {
                    context.MetronSyncLinks.Remove(imported);
                }
                else if (await ResolveIssueIdAsync(context, read.ItemId, cancellationToken).ConfigureAwait(false) is int metronIssueId)
                {
                    int? stars = Stars(context.Issues.Where(i => i.Id == read.ItemId).Select(i => i.Rating).FirstOrDefault());
                    try
                    {
                        var item = await Call(() => account.ScrobbleAsync(metronIssueId, read.TimestampUtc, stars, cancellationToken)).ConfigureAwait(false);
                        UpsertCollectionLink(context, read.ItemId, item.ItemId, stars ?? item.Rating);
                        Current = Current with { ReadsSent = Current.ReadsSent + 1 };
                    }
                    catch (Rejected)
                    {
                        // Metron has no such issue any more. Nothing to retry.
                    }
                }

                // Sent, already there, or never sendable (no Metron id): in every case this read is dealt with.
                settings.MetronSyncReadingEventId = read.Id;
                context.SaveChanges();
            }
        }
    }

    // ---- Wish list ----------------------------------------------------------------------------------

    private async Task<MetronSyncReport> SyncWishListAsync(MetronSyncReport report, CancellationToken cancellationToken)
    {
        Current = report;
        using var context = createContext();
        var wants = context.WantedIssues.Where(w => w.Provider == ComicProvider.Metron).ToList();
        var wantById = wants.ToDictionary(w => w.Id);
        var links = context.MetronSyncLinks.Where(l => l.Kind == MetronSyncKind.WishListItem).ToList();
        var linked = links.Select(l => l.LocalId).ToHashSet();

        foreach (var link in links)
        {
            wantById.TryGetValue(link.LocalId, out var want);
            if (link.State == MetronSyncState.Synced && want?.Status == WantedIssueStatus.Imported)
            {
                try
                {
                    int? collectionItemId = await Call(() => account.AcquireWishListItemAsync(link.RemoteId, cancellationToken)).ConfigureAwait(false);
                    if (collectionItemId is int itemId && want.IssueId is int issueId)
                    {
                        UpsertCollectionLink(context, issueId, itemId, pushedRating: null);
                    }

                    Current = Current with { WishListAcquired = Current.WishListAcquired + 1 };
                }
                catch (Rejected)
                {
                    // The item is gone from the wish list; there is nothing left to mark.
                }

                link.State = MetronSyncState.Acquired;
                context.SaveChanges();
            }
            else if (want is null || want.Status == WantedIssueStatus.Ignored)
            {
                if (link.State == MetronSyncState.Synced)
                {
                    await Call(() => account.RemoveFromWishListAsync(link.RemoteId, cancellationToken)).ConfigureAwait(false);
                    Current = Current with { WishListRemoved = Current.WishListRemoved + 1 };
                }

                context.MetronSyncLinks.Remove(link);
                context.SaveChanges();
            }
        }

        foreach (var want in wants.Where(w => !linked.Contains(w.Id) && IsOpen(w.Status)))
        {
            try
            {
                var item = await Call(() => account.AddToWishListAsync(want.ExternalIssueId, cancellationToken)).ConfigureAwait(false);
                context.MetronSyncLinks.Add(NewLink(MetronSyncKind.WishListItem, want.Id, item.ItemId));
                Current = Current with { WishListAdded = Current.WishListAdded + 1 };
            }
            catch (Rejected)
            {
                context.MetronSyncLinks.Add(NewLink(MetronSyncKind.WishListItem, want.Id, 0, MetronSyncState.Rejected));
            }

            context.SaveChanges();
        }

        return Current;
    }

    private static bool IsOpen(WantedIssueStatus status) =>
        status is WantedIssueStatus.Wanted or WantedIssueStatus.Snatched or WantedIssueStatus.Downloading or WantedIssueStatus.Failed;

    // ---- Collection ---------------------------------------------------------------------------------

    private async Task<MetronSyncReport> SyncCollectionAsync(MetronSyncReport report, CancellationToken cancellationToken)
    {
        Current = report;
        using var context = createContext();

        // Rating changes first: one small request each, and the part of this the user can see.
        var rated = (from link in context.MetronSyncLinks
                     where link.Kind == MetronSyncKind.CollectionItem && link.State == MetronSyncState.Synced
                     join issue in context.Issues on link.LocalId equals issue.Id
                     select new { Link = link, issue.Rating }).ToList();
        foreach (var row in rated)
        {
            int? stars = Stars(row.Rating);
            if (stars == row.Link.PushedRating)
            {
                continue;
            }

            try
            {
                await Call(() => account.SetCollectionRatingAsync(row.Link.RemoteId, stars, cancellationToken)).ConfigureAwait(false);
                Current = Current with { RatingsSent = Current.RatingsSent + 1 };
            }
            catch (Rejected)
            {
                // The item was deleted on Metron. Leave it deleted: nothing here ever re-adds what the user removed.
                row.Link.State = MetronSyncState.RemovedRemotely;
            }

            row.Link.PushedRating = stars;
            row.Link.SyncedAt = _now();
            context.SaveChanges();
        }

        // Then every issue with a file that isn't in the collection yet. Only ids are loaded up front: the list can be the whole library.
        var done = context.MetronSyncLinks.Where(l => l.Kind == MetronSyncKind.CollectionItem).Select(l => l.LocalId);
        var pending = context.Issues
            .Where(i => !i.IsPlaceholder && !i.FileIsMissing && i.FilePath != null && !done.Contains(i.Id))
            .Where(i => context.ComicMetadataExternalIds.Any(e => e.EntityKind == ComicMetadataEntityKind.Issue && e.EntityId == i.Id))
            .OrderBy(i => i.Id)
            .Select(i => i.Id)
            .ToList();

        foreach (int issueId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ResolveIssueIdAsync(context, issueId, cancellationToken).ConfigureAwait(false) is not int metronIssueId)
            {
                // Its only id is one Metron doesn't know. Remembered, so the lookup isn't repeated every run.
                context.MetronSyncLinks.Add(NewLink(MetronSyncKind.CollectionItem, issueId, 0, MetronSyncState.Rejected));
                context.SaveChanges();
                continue;
            }

            try
            {
                var item = await Call(() => account.AddToCollectionAsync(metronIssueId, cancellationToken)).ConfigureAwait(false);
                UpsertCollectionLink(context, issueId, item.ItemId, item.Rating);
                Current = Current with { CollectionAdded = Current.CollectionAdded + 1 };
            }
            catch (Rejected)
            {
                context.MetronSyncLinks.Add(NewLink(MetronSyncKind.CollectionItem, issueId, 0, MetronSyncState.Rejected));
            }

            context.SaveChanges();
        }

        return Current;
    }

    // ---- Import from Metron (manual) ----------------------------------------------------------------

    /// <summary>
    /// Fills in what the account knows and the library doesn't: a rating on an unrated issue, a read on an unread one. It never overwrites, and the reads
    /// it records are marked so reading sync doesn't send them straight back. Runs only when the user asks, regardless of the area switches.
    /// </summary>
    public async Task<MetronImportReport> ImportAsync(CancellationToken cancellationToken)
    {
        if (!await RunGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new MetronImportReport(0, 0, 0, AlreadyRunningMessage);
        }

        try
        {
            return await ImportCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RunGate.Release();
        }
    }

    private async Task<MetronImportReport> ImportCoreAsync(CancellationToken cancellationToken)
    {
        int matched = 0, ratings = 0, reads = 0;
        string? stopped = null;
        using var context = createContext();
        string? pageUrl = null;

        try
        {
            for (int page = 0; page < ImportPageBudget; page++)
            {
                var result = await Call(() => account.GetCollectionPageAsync(pageUrl, cancellationToken), budgeted: false).ConfigureAwait(false);
                var metronIds = result.Items.Select(i => i.IssueId.ToString(CultureInfo.InvariantCulture)).ToList();
                var localByMetronId = context.ComicMetadataExternalIds
                    .Where(e => e.EntityKind == ComicMetadataEntityKind.Issue && e.Provider == ComicProvider.Metron && metronIds.Contains(e.ExternalId))
                    .ToDictionary(e => e.ExternalId, e => e.EntityId);

                foreach (var item in result.Items)
                {
                    if (!localByMetronId.TryGetValue(item.IssueId.ToString(CultureInfo.InvariantCulture), out int issueId)
                        || context.Issues.Include(i => i.Series).FirstOrDefault(i => i.Id == issueId) is not { } issue)
                    {
                        continue;
                    }

                    matched++;
                    if (issue.Rating is null && item.Rating is int stars)
                    {
                        issue.Rating = stars;
                        ratings++;
                    }

                    // Whatever the account has is what was "sent": a rating we just took from it must not go back as a change.
                    UpsertCollectionLink(context, issue.Id, item.ItemId, Stars(issue.Rating) == item.Rating ? item.Rating : null, keepExistingRating: true);

                    if (item.IsRead && !issue.HasBeenRead())
                    {
                        IssueReadStateResolver.MarkAsRead(issue);
                        if (issue.HasBeenRead())
                        {
                            var read = new ReadingEvent
                            {
                                ItemType = ReadingItemType.Comic,
                                ItemId = issue.Id,
                                Kind = ReadingEventKind.Finished,
                                TimestampUtc = item.DateRead ?? _now(),
                                SeriesId = issue.SeriesId,
                                Publisher = issue.Publisher,
                                SeriesTitle = issue.Series?.Name,
                            };
                            context.ReadingEvents.Add(read);
                            context.SaveChanges();
                            context.MetronSyncLinks.Add(NewLink(MetronSyncKind.ImportedRead, read.Id, item.ItemId));
                            reads++;
                        }
                    }

                    context.SaveChanges();
                }

                pageUrl = result.Next;
                if (pageUrl is null)
                {
                    break;
                }
            }
        }
        catch (StopRun stop)
        {
            stopped = stop.Message;
        }

        return new MetronImportReport(matched, ratings, reads, stopped);
    }

    // ---- Shared -------------------------------------------------------------------------------------

    /// <summary>Old history is sent only when asked for: turning reading sync on starts from the newest read there is.</summary>
    public static void StartReadingFromNow(PaperbunkrDbContext context)
    {
        context.GetOrCreateAppSettings().MetronSyncReadingEventId = context.ReadingEvents.Max(e => (int?)e.Id) ?? 0;
        context.SaveChanges();
    }

    /// <summary>"Send my reading history": the next runs work through every finished read from the beginning, within their budgets.</summary>
    public static void SendReadingHistory(PaperbunkrDbContext context)
    {
        context.GetOrCreateAppSettings().MetronSyncReadingEventId = 0;
        context.SaveChanges();
    }

    /// <summary>A 0-5 rating as the 1-5 stars Metron takes; unrated (or zero) is no rating at all.</summary>
    internal static int? Stars(float? rating) =>
        rating is > 0f ? Math.Clamp((int)Math.Round(rating.Value, MidpointRounding.AwayFromZero), 1, 5) : null;

    /// <summary>
    /// The Metron id of a local issue: its own link, or - for a book only ever matched on ComicVine - one <c>cv_id</c> lookup, kept as a link so it is never
    /// asked twice. Null when the issue has neither, or Metron doesn't know the ComicVine id.
    /// </summary>
    private async Task<int?> ResolveIssueIdAsync(PaperbunkrDbContext context, int issueId, CancellationToken cancellationToken)
    {
        if (_resolved.TryGetValue(issueId, out int? known))
        {
            return known;
        }

        var links = context.ComicMetadataExternalIds
            .Where(e => e.EntityKind == ComicMetadataEntityKind.Issue && e.EntityId == issueId)
            .Select(e => new { e.Provider, e.ExternalId })
            .ToList();

        int? Parse(ComicProvider provider) =>
            links.Where(l => l.Provider == provider).Select(l => int.TryParse(l.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 ? id : (int?)null).FirstOrDefault(id => id is not null);

        int? metronId = Parse(ComicProvider.Metron);
        if (metronId is null && lookup is not null && Parse(ComicProvider.ComicVine) is int comicVineId)
        {
            var hits = await Call(() => lookup.FindIssuesByComicVineIdAsync(comicVineId, cancellationToken)).ConfigureAwait(false);
            if (hits.Count == 1)
            {
                metronId = hits[0].IssueId;
                ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Issue, ComicProvider.Metron, issueId, hits[0].IssueId);
            }
        }

        return _resolved[issueId] = metronId;
    }

    private void UpsertCollectionLink(PaperbunkrDbContext context, int issueId, int itemId, int? pushedRating, bool keepExistingRating = false)
    {
        var link = context.MetronSyncLinks.FirstOrDefault(l => l.Kind == MetronSyncKind.CollectionItem && l.LocalId == issueId);
        if (link is null)
        {
            context.MetronSyncLinks.Add(NewLink(MetronSyncKind.CollectionItem, issueId, itemId, pushedRating: pushedRating));
        }
        else
        {
            link.RemoteId = itemId;
            link.State = MetronSyncState.Synced;
            link.SyncedAt = _now();
            if (!keepExistingRating || link.PushedRating is null)
            {
                link.PushedRating = pushedRating ?? link.PushedRating;
            }
        }

        context.SaveChanges();
    }

    private MetronSyncLink NewLink(MetronSyncKind kind, int localId, int remoteId, MetronSyncState state = MetronSyncState.Synced, int? pushedRating = null) =>
        new() { Kind = kind, LocalId = localId, RemoteId = remoteId, State = state, PushedRating = pushedRating, SyncedAt = _now() };

    /// <summary>
    /// Every request goes through here: it counts against the run's budget, yields to interactive use when the day's quota is nearly gone, and turns Metron's
    /// answers into the two things a run can do about them - stop (rate limit, login, an outage) or skip this one item (<see cref="Rejected"/>).
    /// </summary>
    private async Task<T> Call<T>(Func<Task<T>> call, bool budgeted = true)
    {
        if (budgeted && _requests >= requestBudget)
        {
            throw new StopRun("this run's share of requests is used up; the rest goes next time", failed: false);
        }

        if (MetronQuota.BackgroundShouldWait(out _))
        {
            throw new StopRun("Metron's daily limit is nearly used up; the rest goes after it resets", failed: false);
        }

        _requests++;
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 107)
        {
            throw new StopRun("Metron's rate limit was reached; the rest goes next time", failed: false);
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 100)
        {
            throw new StopRun(ex.Message, failed: true);
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 101 || ex.HttpStatus == 400)
        {
            throw new Rejected();
        }
        catch (ComicVineException ex)
        {
            throw new StopRun(ex.Message, failed: true);
        }
    }

    private async Task Call(Func<Task> call) =>
        await Call(async () =>
        {
            await call().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
}
