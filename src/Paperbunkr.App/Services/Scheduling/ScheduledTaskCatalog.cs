using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Covers;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.Scheduling;

/// <summary>
/// The fixed set of recurring maintenance tasks the scheduler can run
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
/// Not user-extensible. Each descriptor's <c>RunAsync</c> wraps a real library operation - the same
/// one the corresponding Preferences button invokes.
/// </summary>
public static class ScheduledTaskCatalog
{
    public const string DbBackup = "db-backup";
    public const string LibraryScan = "library-scan";
    public const string BookScan = "book-scan";
    public const string SyncMetadata = "sync-metadata";
    public const string ContentTypeSweep = "content-type-sweep";
    public const string VerifyCovers = "verify-covers";
    public const string GenerateCovers = "generate-covers";
    public const string StoryEventAutodetect = "story-event-autodetect";
    public const string StoryEventIdentity = "story-event-identity";
    public const string ContinuityWikidataAutodetect = "continuity-wikidata-autodetect";
    public const string FollowArcs = "follow-arcs";
    public const string ComicVineScrape = "comicvine-scrape";
    public const string LibraryOrganize = "library-organize";
    public const string RemotePageCacheSweep = "remote-page-cache-sweep";
    public const string LibrarySnapshot = "library-snapshot";
    public const string GoalPaceCheck = "goal-pace-check";
    public const string DetectAdPages = "detect-ad-pages";
    public const string GcdMatch = "gcd-match";

    public static IReadOnlyList<ScheduledTaskDescriptor> All { get; } = Build();

    private static IReadOnlyList<ScheduledTaskDescriptor> Build() => new[]
    {
        new ScheduledTaskDescriptor(
            DbBackup, "Back up database",
            "Copies the library database to your backup folder.",
            ActivityJobKind.Other, Priority: 1, SchedulerResourceClass.Db,
            TimeSpan.FromHours(4), DefaultEnabled: true, ScheduleMode.Interval,
            static (handle, ct) => Task.Run(() =>
            {
                handle.Report("Backing up…");
                string path = new BackupService().BackupNow();
                return $"Backed up to {System.IO.Path.GetFileName(path)}";
            }, ct)),

        new ScheduledTaskDescriptor(
            LibraryScan, "Scan comic library folders",
            "Looks for new or changed comics in your watched folders.",
            ActivityJobKind.LibraryScan, Priority: 2, SchedulerResourceClass.Db,
            TimeSpan.FromHours(6), DefaultEnabled: false, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                var result = await new LibraryFolderScanner().ScanAllAsync(Adapt(handle, "files"), ct);
                return result.IssuesAdded == 0
                    ? "No new comics found"
                    : $"Added {result.IssuesAdded} issue{Plural(result.IssuesAdded)} across {result.SeriesTouched} series";
            }),

        new ScheduledTaskDescriptor(
            BookScan, "Scan book folders",
            "Looks for new or changed books in your book folders.",
            ActivityJobKind.BookScan, Priority: 3, SchedulerResourceClass.Db,
            TimeSpan.FromHours(6), DefaultEnabled: false, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                var result = await new BookFolderScanService().ScanAllAsync(Adapt(handle, "files"), ct);
                return result.BooksAdded == 0
                    ? "No new books found"
                    : $"Added {result.BooksAdded} book{Plural(result.BooksAdded)} across {result.SeriesTouched} series";
            }),

        new ScheduledTaskDescriptor(
            SyncMetadata, "Re-read embedded metadata",
            "Re-reads ComicInfo.xml for linked issues and fills in blank fields.",
            ActivityJobKind.SyncMetadata, Priority: 4, SchedulerResourceClass.Db,
            TimeSpan.FromDays(7), DefaultEnabled: false, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                var result = await new LibraryFolderScanner().SyncMetadataAsync(Adapt(handle, "issues"), ct);
                return result.IssuesUpdated == 0
                    ? "No new metadata found"
                    : $"Updated {result.IssuesUpdated} issue{Plural(result.IssuesUpdated)}";
            }),

        new ScheduledTaskDescriptor(
            ContentTypeSweep, "Classify unknown series",
            "Assigns a content type (comic / manga / …) to series still marked unknown.",
            ActivityJobKind.Other, Priority: 5, SchedulerResourceClass.Db,
            TimeSpan.FromDays(7), DefaultEnabled: true, ScheduleMode.Interval,
            static (handle, ct) => Task.Run(() =>
            {
                handle.Report("Classifying…");
                int changed = new LibraryFolderScanner().RunContentTypeSweepCore(ct);
                return changed == 0 ? "Nothing to classify" : $"Classified {changed} series";
            }, ct)),

        new ScheduledTaskDescriptor(
            VerifyCovers, "Verify cover thumbnails",
            "Re-generates covers whose source file has changed since the thumbnail was made.",
            ActivityJobKind.GenerateCovers, Priority: 6, SchedulerResourceClass.DiskCpu,
            TimeSpan.FromDays(14), DefaultEnabled: true, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                await new CoverThumbnailService().VerifyAllAsync(Adapt(handle, "comics"), ct);
                await new BookCoverThumbnailService().VerifyAllAsync(Adapt(handle, "books"), ct);
                MarkCoverVerificationDone();
                return "Covers verified";
            }),

        new ScheduledTaskDescriptor(
            GenerateCovers, "Generate missing covers",
            "Creates cover thumbnails for comics that don't have one yet.",
            ActivityJobKind.GenerateCovers, Priority: 7, SchedulerResourceClass.DiskCpu,
            TimeSpan.FromDays(7), DefaultEnabled: false, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                await new CoverThumbnailService().GenerateAllAsync(Adapt(handle, "issues"), ct);
                return "Covers generated";
            }),

        new ScheduledTaskDescriptor(
            StoryEventAutodetect, "Find story-event suggestions",
            "Groups already-tagged Story Arc issues into story-event suggestions, optionally " +
            "verified against ComicVine/Metron if configured in Preferences > Libraries.",
            ActivityJobKind.SyncMetadata, Priority: 8, SchedulerResourceClass.Network,
            TimeSpan.FromDays(7), DefaultEnabled: true, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                handle.Report("Grouping story arcs…");
                using var context = PaperbunkrDb.CreateContext();
                var candidates = StoryArcGroupingResolver.GetCandidates(context);
                handle.Report("Verifying against ComicVine/Metron…");
                var verified = await ArcExternalVerificationService.VerifyAsync(context, candidates, ct);

                string summary = verified.Count == 0
                    ? "No new story-event suggestions found"
                    : $"{verified.Count} new story-event suggestion{Plural(verified.Count)} found";

                // ScheduledTaskDescriptor.RunAsync has no IActivityService of its own (no DI
                // container in this app - see IActivityService's own doc comment), so a deep-linked
                // completion is raised here via the job handle's own Succeed(summary, link) rather
                // than IActivityService.RaiseAlert. SchedulerService calls handle.Succeed(summary)
                // again after this returns; ActivityService.JobHandle.Succeed is idempotent
                // (Interlocked-guarded), so that second call is a safe no-op.
                var link = verified.Count > 0 ? new ActivityLink(ActivityLinkKind.StoryEventsScreen) : null;
                handle.Succeed(summary, link);
                return summary;
            }),

        // "Check story events": the Story Event resolver (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §4) - id
        // completion in batches, then silent merges - followed by the smart connector (docs/superpowers/specs/2026-09-27-continuity-map-
        // design.md §2), which only links events once duplicates are merged. A brand-new interval task has no last run, so this also
        // runs on the first scheduler check after upgrading.
        new ScheduledTaskDescriptor(
            StoryEventIdentity, "Check story events",
            "Looks up each story event's ComicVine and Metron arc ids, merges events that turn out to be the same arc (the two sites " +
            "often spell one arc differently), then works out which events come before which (prequels, sequels, continuations) " +
            "from your library and Wikidata. Unsure matches wait under Possible duplicates on the Story Events screen.",
            ActivityJobKind.SyncMetadata, Priority: 17, SchedulerResourceClass.Network,
            TimeSpan.FromDays(7), DefaultEnabled: true, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                string summary = await StoryEventChecks.RunAsync(
                    new Progress<(int Done, int Total)>(p => handle.Report(p.Done, p.Total, "Checking story events")), ct);
                handle.Succeed(summary, new ActivityLink(ActivityLinkKind.StoryEventsScreen));
                return summary;
            }),

        new ScheduledTaskDescriptor(
            ContinuityWikidataAutodetect, "Find shared-universe suggestions",
            "Matches series against Wikidata via their most recurring characters to suggest " +
            "shared-universe continuities (20 series/run). No credentials needed - Wikidata's " +
            "search API is open.",
            ActivityJobKind.SyncMetadata, Priority: 9, SchedulerResourceClass.Network,
            // Daily, not weekly - at 20 series/run this is how a large library (400+ series) gets
            // fully covered in a reasonable number of days without the user manually re-clicking
            // "Check Wikidata" repeatedly (real complaint this was built to address). The manual
            // button itself now runs uncapped instead of relying on this cadence at all.
            TimeSpan.FromDays(1), DefaultEnabled: true, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                handle.Report("Matching series against Wikidata…");
                using var context = PaperbunkrDb.CreateContext();
                using var httpClient = WikidataClient.CreateClient();
                var client = new WikidataClient(httpClient);
                var suggestions = await ContinuityWikidataMatchResolver.GetSuggestionsAsync(context, client, ct, progress: Adapt(handle, "series"));

                string summary = suggestions.Count == 0
                    ? "No new shared-universe suggestions found"
                    : $"{suggestions.Count} new shared-universe suggestion{Plural(suggestions.Count)} found";

                // Same idempotent-Succeed reasoning as StoryEventAutodetect above.
                var link = suggestions.Count > 0 ? new ActivityLink(ActivityLinkKind.StoryEventsScreen) : null;
                handle.Succeed(summary, link);
                return summary;
            }),

        // "Follow arc" (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md 8): off by default, and does nothing unless the user has also
        // turned Acquisition on and added a ComicVine key. Runs at low ComicVine priority so it can never starve the UI's own lookups.
        new ScheduledTaskDescriptor(
            FollowArcs, "Follow story arcs",
            "Refreshes the story-arc reading lists you follow and requests any newly listed issues you don't have. " +
            "Needs Acquisition switched on and your ComicVine key.",
            ActivityJobKind.Acquisition, Priority: 10, SchedulerResourceClass.Network,
            TimeSpan.FromDays(1), DefaultEnabled: false, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                using var context = PaperbunkrDb.CreateContext();
                if (!context.GetOrCreateAcquisitionSettings().Enabled)
                {
                    const string off = "Acquisition is switched off, so nothing was requested.";
                    handle.Succeed(off);
                    return off;
                }

                handle.Report("Refreshing followed arcs…");
                var key = Paperbunkr.Data.Credentials.CredentialStore.Get(context, "ComicVine", Paperbunkr.Data.Entities.CredentialKind.ApiKey);
                var comicVine = string.IsNullOrWhiteSpace(key)
                    ? null
                    : new Paperbunkr.Data.ComicVine.ComicVineClient(key, Paperbunkr.Data.ComicVine.ComicVineRequestPriority.Low);

                var metron = Paperbunkr.Data.ComicVine.ComicProviderFactory.Create(context, Paperbunkr.Data.Entities.ComicProvider.Metron, Paperbunkr.Data.ComicVine.ComicVineRequestPriority.Low);
                var result = await Paperbunkr.Data.Acquisition.ArcFollowService.RunAsync(context, comicVine, ct, progress: (done, total) => handle.Report(done, total, $"{done} / {total} arcs"),
                    clientFor: comicVine is null && metron is null ? null : provider => provider == Paperbunkr.Data.Entities.ComicProvider.Metron ? metron : comicVine);

                var parts = new System.Collections.Generic.List<string> { $"{result.ListsChecked} arc{Plural(result.ListsChecked)} checked" };
                if (result.IssuesAdded > 0) parts.Add($"{result.IssuesAdded} new entr{(result.IssuesAdded == 1 ? "y" : "ies")}");
                if (result.Requested > 0) parts.Add($"{result.Requested} requested");
                if (result.Unresolved > 0) parts.Add($"{result.Unresolved} couldn't be matched");
                if (comicVine is null && result.ListsChecked > 0) parts.Add("add a ComicVine key to request missing issues");
                if (result.Problems.Count > 0) parts.Add($"{result.Problems.Count} problem{Plural(result.Problems.Count)}: {result.Problems[0]}");

                string summary = string.Join(", ", parts) + ".";
                handle.Succeed(summary, result.Requested > 0 ? new ActivityLink(ActivityLinkKind.WantedScreen) : null);
                return summary;
            }),

        // "Scrape with ComicVine" on a schedule (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md 8): off by default, never opens a dialog.
        // Every comic with no ComicVine volume link is matched; with "choose the best match automatically" off it skips whatever would have needed a question.
        new ScheduledTaskDescriptor(
            ComicVineScrape, "Scrape unscraped comics with ComicVine",
            "Matches comics that have no details yet (from the source chosen under Organize & Scrape), without asking. Comics that need a choice are skipped unless \"Choose the best match automatically\" is on " +
            "(Preferences → Organize & Scrape). Needs your ComicVine key.",
            ActivityJobKind.Scrape, Priority: 11, SchedulerResourceClass.Network,
            TimeSpan.FromDays(1), DefaultEnabled: false, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                var scraper = Paperbunkr.App.Scraper.ScheduledCoordinators.Scraper;
                if (scraper is null)
                {
                    const string notReady = "The scraper isn't ready yet.";
                    handle.Succeed(notReady);
                    return notReady;
                }

                handle.Report("Scraping unscraped comics…");
                string summary = await scraper.ScrapeUnscrapedAsync(ct, handle);
                handle.Succeed(summary);
                return summary;
            }),

        // "Organize library" on a schedule: off by default; uses the first organizer profile marked for scheduled runs, and never opens a dialog
        // (collisions follow that profile's automatic-collision setting).
        new ScheduledTaskDescriptor(
            LibraryOrganize, "Organize library",
            "Moves or copies files into the folders an organizer profile's templates describe. Uses the profile marked for scheduled runs " +
            "(Preferences → Organize & Scrape); nothing happens until one is. Every move can be undone.",
            ActivityJobKind.Import, Priority: 12, SchedulerResourceClass.Db,
            TimeSpan.FromDays(1), DefaultEnabled: false, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                var organizer = Paperbunkr.App.Scraper.ScheduledCoordinators.Organizer;
                if (organizer is null)
                {
                    const string notReady = "The organizer isn't ready yet.";
                    handle.Succeed(notReady);
                    return notReady;
                }

                var scheduled = organizer.Profiles.GetAll().Where(p => p.UseForScheduledRun).ToList();
                if (scheduled.Count == 0)
                {
                    const string none = "No organizer profile is marked for scheduled runs, so nothing was organized.";
                    handle.Succeed(none);
                    return none;
                }

                handle.Report(scheduled.Count == 1 ? $"Organizing with \"{scheduled[0].Name}\"…" : $"Organizing with {scheduled.Count} profiles…");
                string summary = await organizer.OrganizeLibraryAsync(scheduled.Select(p => p.Id).ToList(), ct, handle);
                handle.Succeed(summary);
                return summary;
            }),

        // Pages read from remote libraries are cached on disk under a size quota; this is the other bound (docs/superpowers/specs/
        // 2026-09-19-remote-library-sharing-design.md section 7.3): pages nobody has opened for 30 days go, so the cache can't quietly
        // hold everything you ever read once.
        new ScheduledTaskDescriptor(
            RemotePageCacheSweep, "Clean up remote library pages",
            "Removes pages of remote libraries you haven't opened in 30 days from this computer. They are downloaded again if you open them.",
            ActivityJobKind.Other, Priority: 13, SchedulerResourceClass.DiskCpu,
            TimeSpan.FromDays(7), DefaultEnabled: true, ScheduleMode.Interval,
            static (handle, ct) => Task.Run(() =>
            {
                handle.Report("Checking cached pages…");
                int removed = Paperbunkr.App.Services.Sharing.PeerPageCache.Shared.SweepExpired();
                return removed == 0 ? "Nothing to clean up" : $"Removed {removed} page{Plural(removed)}";
            }, ct)),

        // First real use of ScheduleMode.DailyAt (docs/superpowers/specs/2026-09-22-insights-backlog-
        // burndown-design.md) - every other entry above uses Interval. Counts local comics for the
        // Insights screen's Backlog burn-down chart; DefaultEnabled true and ActivityJobKind.Other match
        // DbBackup's own reasoning (cheap, safe, no visible side effect, unlike the scan/organize tasks
        // that default off).
        new ScheduledTaskDescriptor(
            LibrarySnapshot, "Record daily library snapshot",
            "Counts your unread comics for the Backlog burn-down chart on the Insights screen.",
            ActivityJobKind.Other, Priority: 14, SchedulerResourceClass.Db,
            TimeSpan.FromHours(24), DefaultEnabled: true, ScheduleMode.DailyAt,
            static (handle, ct) => Task.Run(() =>
            {
                handle.Report("Recording snapshot…");
                using var context = PaperbunkrDb.CreateContext();
                var (total, backlog) = new LibrarySnapshotService().Capture(context);
                return backlog == 0 ? "No backlog - all caught up" : $"{backlog} of {total} comics unread";
            }, ct)),

        // Behind-pace check for reading goals (docs/superpowers/specs/2026-09-23-insights-reading-goals-
        // design.md). Milestone (50%/100%) nudges are live, in GoalsViewModel - this task only covers the
        // daily pace check, which needs an elapsed-time context that only makes sense to evaluate
        // periodically. No per-goal ActivityAlert here (a task body has no IActivityService of its own - see
        // the StoryEventAutodetect entry's own comment above) - like every other task in this catalog, the
        // outcome is just this task's own completion summary.
        new ScheduledTaskDescriptor(
            GoalPaceCheck, "Check reading goal pace",
            "Lets you know if you're falling behind on an active reading goal.",
            ActivityJobKind.Other, Priority: 15, SchedulerResourceClass.Db,
            TimeSpan.FromHours(24), DefaultEnabled: true, ScheduleMode.DailyAt,
            static (handle, ct) => Task.Run(() =>
            {
                handle.Report("Checking goal pace…");
                using var context = PaperbunkrDb.CreateContext();
                int behind = GoalResolver.Build(context, DateTime.UtcNow).Count(g => g.PaceState == GoalPaceState.Behind);
                return behind == 0 ? "No goals behind pace" : $"{behind} goal{Plural(behind)} behind pace";
            }, ct)),

        // Ad-page detection (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md §5, pitch
        // #9), modelled on VerifyCovers: DiskCpu (it decodes pages), off by default (a first pass over a large
        // library is long and the user has to opt in), later runs only touch changed files. It only ever creates
        // proposals for Library Health - never a page tag. Its Run-now button in Preferences > Automation is the
        // "Scan now" of the design; the finished job's summary in the Activity Center carries the count.
        new ScheduledTaskDescriptor(
            DetectAdPages, "Detect advertisement pages",
            "Looks for pages that match ads you have already tagged in the reader and lists them in Library Health.",
            ActivityJobKind.Other, Priority: 16, SchedulerResourceClass.DiskCpu,
            TimeSpan.FromDays(7), DefaultEnabled: false, ScheduleMode.Interval,
            static async (handle, ct) =>
            {
                var result = await new Paperbunkr.App.Services.AdDetection.AdPageDetectionService().ScanAsync(Adapt(handle, "issues"), ct);
                if (result.IssuesScanned == 0 && result.IssuesFailed == 0)
                {
                    return "Nothing to scan yet - tag an advertisement page in the reader first";
                }

                return result.ProposalsCreated == 0
                    ? "No new ad pages found"
                    : $"Found {result.ProposalsCreated} possible ad page{Plural(result.ProposalsCreated)} - review them in Library Health";
            }),

        // Grand Comics Database matching (docs/superpowers/specs/2026-09-27-gcd-data-design.md §3-§4): local except Metron's GCD ids for
        // Metron-scraped series (a few dozen lookups a run at low priority). Does nothing until the data is downloaded in Preferences →
        // Connections, which also runs this straight after installing.
        new ScheduledTaskDescriptor(
            GcdMatch, "Match series to GCD",
            "Links your series and issues to the Grand Comics Database data (downloaded under Preferences → Connections) so continued " +
            "series are linked and story events are ordered by on-sale dates. Uses Metron's GCD ids when you have a Metron login.",
            ActivityJobKind.SyncMetadata, Priority: 18, SchedulerResourceClass.Network,
            TimeSpan.FromDays(7), DefaultEnabled: true, ScheduleMode.Interval,
            static (handle, ct) => Task.Run(() => Paperbunkr.App.Services.Gcd.GcdMatching.RunAsync(() => PaperbunkrDb.CreateContext(), handle, ct), ct)),
    };

    public static ScheduledTaskDescriptor? Find(string id)
    {
        foreach (var d in All)
        {
            if (d.Id == id)
            {
                return d;
            }
        }

        return null;
    }

    private static IProgress<(int Done, int Total)> Adapt(IActivityJobHandle handle, string unit) =>
        new Progress<(int Done, int Total)>(p => handle.Report(p.Done, p.Total, $"{p.Done} / {p.Total} {unit}"));

    private static string Plural(int n) => n == 1 ? string.Empty : "s";

    private static void MarkCoverVerificationDone()
    {
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            context.GetOrCreateAppSettings().LastCoverVerificationUtc = DateTime.UtcNow;
            context.SaveChanges();
        }
        catch
        {
            // Best-effort mirror of the legacy column - the scheduler's own LastRunUtc is authoritative.
        }
    }
}
