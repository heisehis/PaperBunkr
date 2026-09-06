using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.App.Services.Covers;
using Paperbunkr.Data.Entities;

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
