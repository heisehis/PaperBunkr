using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Covers;
using Paperbunkr.App.ViewModels.LibraryActions;
using Paperbunkr.Data;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// "Refresh ▸" (CE's <c>RefreshInformation</c>, <c>ComicBrowserControl.cs:2594-2618</c>; docs/superpowers/specs/2026-09-29-library-bulk-
/// actions-design.md §2): CE's plain Refresh only rebuilds thumbnails and its Ctrl+Refresh re-reads the file - here those are two visible
/// entries. Both run as Activity Center jobs over an id list captured at start.
/// </summary>
public partial class LibraryScreenViewModel
{
    /// <summary>One re-read at a time: a second one waits, shown as Queued, until the first settles.</summary>
    private readonly SemaphoreSlim _rereadGate = new(1, 1);

    private CoverThumbnailService? _coverThumbnails;

    /// <summary>Cover generation for "Refresh thumbnails" - the real service unless a test swaps it.</summary>
    internal CoverThumbnailService CoverThumbnails
    {
        get => _coverThumbnails ??= new CoverThumbnailService();
        set => _coverThumbnails = value;
    }

    /// <summary>"Refresh thumbnails": re-derives each targeted book's cover from its file. Books with a custom cover keep it.</summary>
    [RelayCommand]
    private async Task RefreshThumbnails(LibraryTarget target)
    {
        var ids = IssueIdsOf(target).Where(id => !_remoteIssueIds.Contains(id)).ToList();
        List<(int Id, string Path, long? Size)> books;
        using (var context = PaperbunkrDb.CreateContext())
        {
            books = context.Issues.Where(i => ids.Contains(i.Id) && i.FilePath != null)
                .Select(i => new { i.Id, i.FilePath, i.FileSize })
                .ToList()
                .Where(i => !CustomCoverPaths.Exists(i.Id))
                .Select(i => (i.Id, i.FilePath!, i.FileSize))
                .ToList();
        }

        if (books.Count == 0)
        {
            return;
        }

        using var job = _activity.StartJob(ActivityJobKind.GenerateCovers, books.Count == 1 ? "Refreshing 1 thumbnail" : $"Refreshing {books.Count} thumbnails");
        var covers = CoverThumbnails;
        var token = job.CancellationToken;
        var (done, failed) = await Task.Run(() =>
        {
            int d = 0, f = 0;
            foreach (var (id, path, size) in books)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (!covers.TryGenerateThumbnail(id, path, size, force: true))
                {
                    f++;
                }

                d++;
                job.Report(d, books.Count);
            }

            return (d, f);
        });

        // The in-memory cover cache is UI-thread state - drop the refreshed entries here, after the worker is done.
        foreach (var (id, _, _) in books)
        {
            CoverImageCache.InvalidateMemoryOnly(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (token.IsCancellationRequested)
        {
            LoadFromDatabase();
            return;
        }

        job.Succeed(failed == 0 ? $"Refreshed {done} thumbnails" : $"Refreshed {done - failed} thumbnails · {failed} failed", itemsProcessed: done, itemsFailed: failed);
        LoadFromDatabase();
    }

    /// <summary>
    /// "Re-read info from file…": after a confirmation, each targeted book's metadata is replaced by its file's ComicInfo.xml
    /// (<see cref="IssueFileRescanService"/>). One undo step for the whole batch; cancelling keeps - and the undo covers - the books already done.
    /// </summary>
    [RelayCommand]
    private async Task RereadFromFile(LibraryTarget target)
    {
        var ids = LocalIssuesOnly(IssueIdsOf(target));
        if (ids.Count == 0 || _dialogs is null)
        {
            return;
        }

        string message = $"Replace the library's metadata for {Books(ids.Count)} with what's stored in their files? This can be reverted with Undo.";
        if (!await _dialogs.ConfirmAsync(message, title: "Re-read info from file", confirmLabel: "Re-read"))
        {
            return;
        }

        bool queued = _rereadGate.CurrentCount == 0;
        using var job = _activity.StartJob(ActivityJobKind.SyncMetadata, $"Re-reading {Books(ids.Count)} from file", startQueued: queued);
        await _rereadGate.WaitAsync();
        try
        {
            job.Begin();
            var token = job.CancellationToken;
            var result = await Task.Run(() => Reread(ids, job, token));
            if (result.Before.Count > 0)
            {
                _history.Record($"Re-read {Books(result.Before.Count)} from file", result.Before, result.After);
                foreach (int id in result.Before.Keys)
                {
                    _enqueueMetadataWriteBack(id, false);
                }
            }

            LoadFromDatabase();
            if (token.IsCancellationRequested)
            {
                return;
            }

            string summary = $"Re-read {result.Updated} books";
            if (result.NoInfo > 0)
            {
                summary += $" · {result.NoInfo} with no embedded info";
            }

            if (result.Failed > 0)
            {
                summary += $" · {result.Failed} failed";
            }

            job.Succeed(summary, itemsProcessed: result.Updated + result.NoInfo + result.Failed, itemsFailed: result.Failed);
        }
        finally
        {
            _rereadGate.Release();
        }
    }

    private sealed record RereadResult(
        Dictionary<int, Dictionary<string, string?>> Before,
        Dictionary<int, Dictionary<string, string?>> After,
        int Updated,
        int NoInfo,
        int Failed);

    /// <summary>Worker half of <see cref="RereadFromFile"/>: one context, saved once at the end so a cancel keeps exactly the finished books.</summary>
    private static RereadResult Reread(IReadOnlyList<int> ids, IActivityJobHandle job, CancellationToken token)
    {
        var before = new Dictionary<int, Dictionary<string, string?>>();
        var after = new Dictionary<int, Dictionary<string, string?>>();
        int updated = 0, noInfo = 0, failed = 0, done = 0;

        using var context = PaperbunkrDb.CreateContext();
        var issues = context.Issues.Include(i => i.Series).Include(i => i.Tags).Where(i => ids.Contains(i.Id)).ToList();
        foreach (var issue in issues)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            var snapshot = Services.MetadataEditHistoryService.CaptureSnapshot(issue);
            switch (IssueFileRescanService.Rescan(issue))
            {
                case IssueRescanOutcome.Updated:
                    before[issue.Id] = snapshot;
                    after[issue.Id] = Services.MetadataEditHistoryService.CaptureSnapshot(issue);
                    updated++;
                    break;
                case IssueRescanOutcome.NoEmbeddedInfo:
                    noInfo++;
                    break;
                default:
                    failed++;
                    break;
            }

            done++;
            job.Report(done, issues.Count, issue.FilePath is { } p ? System.IO.Path.GetFileName(p) : null);
        }

        context.SaveChanges();
        return new RereadResult(before, after, updated, noInfo, failed);
    }
}
