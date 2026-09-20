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
    public const string ContinuityWikidataAutodetect = "continuity-wikidata-autodetect";
    public const string FollowArcs = "follow-arcs";
    public const string ComicVineScrape = "comicvine-scrape";
    public const string LibraryOrganize = "library-organize";

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

                var result = await Paperbunkr.Data.Acquisition.ArcFollowService.RunAsync(context, comicVine, ct, progress: (done, total) => handle.Report(done, total, $"{done} / {total} arcs"));

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
            "Matches comics that have no ComicVine details yet, without asking. Comics that need a choice are skipped unless \"Choose the best match automatically\" is on " +
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

                var profile = organizer.Profiles.GetAll().FirstOrDefault(p => p.UseForScheduledRun);
                if (profile is null)
                {
                    const string none = "No organizer profile is marked for scheduled runs, so nothing was organized.";
                    handle.Succeed(none);
                    return none;
                }

                handle.Report($"Organizing with \"{profile.Name}\"…");
                string summary = await organizer.OrganizeLibraryAsync(profile.Id, ct, handle);
                handle.Succeed(summary);
                return summary;
            }),
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
