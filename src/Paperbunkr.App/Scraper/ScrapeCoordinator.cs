using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Scraper;

/// <summary>
/// The app-side glue for "Scrape with ComicVine" (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md section 8): builds the orchestrator
/// from the saved settings and the ComicVine key in Connections, shows the series-match and issue-match review dialogs through the shared modal host with the batch
/// progress header, reports the whole run as one Activity Center job, and queues the changed files for metadata write-back.
/// <para>
/// An interactive run may open dialogs; an unattended run (<paramref name="isInteractive"/> false, used by the scheduled task) never does: with auto-choose off
/// the orchestrator skips any book it would have had to ask about, exactly as the original plugin's headless gate specified.
/// </para>
/// </summary>
public sealed class ScrapeCoordinator(
    NativePluginModalHostViewModel modalHost,
    Func<PaperbunkrDbContext> createContext,
    IActivityService activity,
    Action<int> enqueueWriteBack,
    Func<ComicVineRequestPriority, IScrapeComicVine?>? createComicVine = null)
{
    private readonly Func<ComicVineRequestPriority, IScrapeComicVine?> _createComicVine = createComicVine ?? CreateDefaultComicVine(createContext);

    /// <summary>The real ComicVine client (shared rate-limited HTTP) using the key saved under Connections, or <c>null</c> when there is none.</summary>
    public static Func<ComicVineRequestPriority, IScrapeComicVine?> CreateDefaultComicVine(Func<PaperbunkrDbContext> createContext) => priority =>
    {
        using var context = createContext();
        var key = CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var client = new ComicVineClient(key, priority);
        return new ScrapeComicVineAdapter(client, client);
    };

    public const string NoKeyMessage = "Add your ComicVine API key under Preferences → Connections first.";

    public async Task<string> ScrapeIssuesAsync(IReadOnlyList<int> issueIds, bool isInteractive = true, CancellationToken cancellationToken = default)
    {
        var comicVine = _createComicVine(isInteractive ? ComicVineRequestPriority.High : ComicVineRequestPriority.Low);
        if (comicVine is null)
        {
            return NoKeyMessage;
        }

        List<Issue> books;
        ScrapeSettings settings;
        using (var context = createContext())
        {
            books = context.Issues.Include(i => i.Series).Where(i => issueIds.Contains(i.Id)).ToList();
            settings = ScrapeSettings.Load(context);
        }

        if (books.Count == 0)
        {
            return "Nothing to scrape.";
        }

        var orchestrator = new ScrapeOrchestrator(comicVine, new ComicVineMatchMemory(createContext), settings);
        using var job = activity.StartJob(ActivityJobKind.Scrape, books.Count == 1 ? $"Scraping {BookLabel(books[0])}" : $"Scraping {books.Count} comics with ComicVine",
            cancellable: true, trigger: isInteractive ? ActivityTrigger.Manual : ActivityTrigger.Scheduled);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, job.CancellationToken);

        int applied;
        try
        {
            if (!isInteractive)
            {
                applied = await orchestrator.ScrapeAsync(books, isInteractive: false, interactiveReview: null, createContext, linked.Token,
                    onProgress: (total, index, issue) => job.Report(index, total, BookLabel(issue))).ConfigureAwait(false);
            }
            else
            {
                var header = new ScrapeBatchHeaderViewModel(books.Count, linked.Cancel);
                using var batch = modalHost.BeginBatch(new ScrapeBatchHeaderView { DataContext = header });
                applied = await orchestrator.ScrapeAsync(
                    books,
                    isInteractive: true,
                    (label, query, candidates, search, ct) => ShowMatchReviewAsync(orchestrator, label, query, candidates, search),
                    createContext,
                    linked.Token,
                    onProgress: (total, index, issue) =>
                    {
                        header.ReportProgress(total, index, BookLabel(issue), ThumbnailBytes(issue));
                        job.Report(index, total, BookLabel(issue));
                    },
                    interactiveIssueReview: (label, volume, issues, autoMatched, ct) => ShowIssueReviewAsync(label, issues, autoMatched)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            job.Fail("Scrape cancelled.");
            return "Scrape cancelled.";
        }

        foreach (var id in books.Select(b => b.Id))
        {
            enqueueWriteBack(id);
        }

        var summary = $"Applied a ComicVine match to {applied} of {books.Count} comic{(books.Count == 1 ? string.Empty : "s")}.";
        job.Succeed(summary, itemsProcessed: applied, itemsFailed: books.Count - applied);
        return summary;
    }

    /// <summary>Every issue of a series (the Detail screen panel's button). Includes the series so the orchestrator can key its search and memory on its name.</summary>
    public Task<string> ScrapeSeriesAsync(int seriesId, bool isInteractive = true)
    {
        List<int> ids;
        using (var context = createContext())
        {
            ids = context.Issues.Where(i => i.SeriesId == seriesId).Select(i => i.Id).ToList();
        }

        return ids.Count == 0 ? Task.FromResult("No issues in this series to scrape.") : ScrapeIssuesAsync(ids, isInteractive);
    }

    /// <summary>The scheduled task: every comic with no ComicVine volume link yet, never asking anything.</summary>
    public Task<string> ScrapeUnscrapedAsync(CancellationToken cancellationToken)
    {
        List<int> ids;
        using (var context = createContext())
        {
            ids = context.Issues.Where(i => string.IsNullOrEmpty(i.Volume) && i.FilePath != null).Select(i => i.Id).ToList();
        }

        return ids.Count == 0 ? Task.FromResult("Nothing left to scrape.") : ScrapeIssuesAsync(ids, isInteractive: false, cancellationToken);
    }

    /// <summary>Shown when a series' Detail page asks for the scrape panel: not for the manga family, whose sources are different.</summary>
    public Control? CreateSeriesPanel(Series series)
    {
        if (series.ContentType is ContentType.Manga or ContentType.Manhua or ContentType.Manhwa)
        {
            return null;
        }

        return new SeriesScraperPanelView { DataContext = new SeriesScraperPanelViewModel(series.Name, () => ScrapeSeriesAsync(series.Id)) };
    }

    // The dialogs use a plain data fetch for the issue list: nesting a second modal inside the first crashed the original plugin when the queued modal was cancelled.
    private Task<ComicVineVolumeSearchResult?> ShowMatchReviewAsync(
        ScrapeOrchestrator orchestrator,
        string bookLabel,
        string initialQuery,
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)> initialCandidates,
        ScrapeOrchestrator.SearchAndRankDelegate search) =>
        modalHost.ShowAsync<ComicVineVolumeSearchResult?>(resolve => new ComicVineMatchReviewDialogView
        {
            DataContext = new ComicVineMatchReviewDialogViewModel(bookLabel, initialQuery, initialCandidates, search, resolve, loadIssues: volumeId => orchestrator.LoadIssuesAsync(volumeId)),
        });

    private Task<ComicVineIssueReviewResult> ShowIssueReviewAsync(string bookLabel, IReadOnlyList<ComicVineIssueSummary> issues, ComicVineIssueSummary? autoMatched) =>
        modalHost.ShowAsync<ComicVineIssueReviewResult>(resolve => new ComicVineIssueReviewDialogView
        {
            DataContext = new ComicVineIssueReviewDialogViewModel(bookLabel, issues, autoMatched, readOnlyPeek: false, resolve),
        });

    private static string BookLabel(Issue issue) => $"{issue.Series?.Name} #{issue.EffectiveNumber()}";

    private static byte[]? ThumbnailBytes(Issue issue)
    {
        var path = CoverThumbnailPaths.GetCachePath(CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize));
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
