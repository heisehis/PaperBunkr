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
    Func<ComicProvider, ComicVineRequestPriority, IScrapeComicVine?>? createComicVine = null)
{
    private readonly Func<ComicProvider, ComicVineRequestPriority, IScrapeComicVine?> _createComicVine = createComicVine ?? CreateDefaultComicVine(createContext);

    /// <summary>The real client for a source (shared rate-limited HTTP) using the login saved under Connections, or <c>null</c> when there is none.</summary>
    public static Func<ComicProvider, ComicVineRequestPriority, IScrapeComicVine?> CreateDefaultComicVine(Func<PaperbunkrDbContext> createContext) => (provider, priority) =>
    {
        using var context = createContext();
        var client = ComicProviderFactory.Create(context, provider, priority);
        return client is null ? null : new ScrapeComicVineAdapter(client, client);
    };

    /// <summary>The source most of <paramref name="books"/> were last scraped from, or <paramref name="fallback"/> when none has a recorded source (ties go to the fallback).</summary>
    internal static ComicProvider StartingProvider(IReadOnlyList<Issue> books, ComicProvider fallback)
    {
        var counts = books.Where(b => b.MetadataSource is not null).GroupBy(b => b.MetadataSource!.Value).Select(g => (Provider: g.Key, Count: g.Count())).ToList();
        if (counts.Count == 0)
        {
            return fallback;
        }

        int best = counts.Max(c => c.Count);
        var leaders = counts.Where(c => c.Count == best).Select(c => c.Provider).ToList();
        return leaders.Contains(fallback) ? fallback : leaders[0];
    }

    public const string NoKeyMessage = "Add your ComicVine API key under Preferences → Connections first.";

    public async Task<string> ScrapeIssuesAsync(IReadOnlyList<int> issueIds, bool isInteractive = true, CancellationToken cancellationToken = default, IActivityJobHandle? existingJob = null)
    {
        List<Issue> books;
        ScrapeSettings settings;
        using (var context = createContext())
        {
            books = context.Issues.Include(i => i.Series).Where(i => issueIds.Contains(i.Id)).ToList();
            settings = ScrapeSettings.Load(context);
        }

        // Starts on the source most of these comics were last scraped from (an id from one source means nothing on the other), else the default from
        // Preferences → Organize & Scrape; an interactive run can switch inside the match dialog. Unattended runs only ever take never-scraped comics,
        // which have no recorded source, so they use the default.
        var provider = StartingProvider(books, settings.DefaultProvider);
        var priority = isInteractive ? ComicVineRequestPriority.High : ComicVineRequestPriority.Low;
        var comicVine = _createComicVine(provider, priority);
        if (comicVine is null)
        {
            return provider == ComicProvider.ComicVine ? NoKeyMessage : ComicProviderFactory.MissingCredentialsMessage(provider);
        }

        if (books.Count == 0)
        {
            return "Nothing to scrape.";
        }

        var orchestrator = new ScrapeOrchestrator(comicVine, new ComicVineMatchMemory(createContext, provider), settings, provider,
            switchTo => _createComicVine(switchTo, priority) is { } source ? (source, new ComicVineMatchMemory(createContext, switchTo)) : null);
        // A scheduled task already has its own Activity Center job; it lends it here so one run is one job, and settles it itself.
        using var owned = existingJob is null
            ? activity.StartJob(ActivityJobKind.Scrape, books.Count == 1 ? $"Scraping {BookLabel(books[0])}" : $"Scraping {books.Count} comics",
                cancellable: true, trigger: isInteractive ? ActivityTrigger.Manual : ActivityTrigger.Scheduled)
            : null;
        var job = existingJob ?? owned!;
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
            owned?.Fail("Scrape cancelled.");
            return "Scrape cancelled.";
        }

        foreach (var id in books.Select(b => b.Id))
        {
            enqueueWriteBack(id);
        }

        var summary = $"Applied a {ComicProviderFactory.DisplayName(orchestrator.Provider)} match to {applied} of {books.Count} comic{(books.Count == 1 ? string.Empty : "s")}.";
        owned?.Succeed(summary, itemsProcessed: applied, itemsFailed: books.Count - applied);
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
    public Task<string> ScrapeUnscrapedAsync(CancellationToken cancellationToken, IActivityJobHandle? existingJob = null)
    {
        List<int> ids;
        using (var context = createContext())
        {
            // Never scraped: no recorded source and no volume. (The volume alone can't say, since it is only a year and stays empty when the source doesn't know one.)
            ids = context.Issues.Where(i => i.MetadataSource == null && string.IsNullOrEmpty(i.Volume) && i.FilePath != null).Select(i => i.Id).ToList();
        }

        return ids.Count == 0 ? Task.FromResult("Nothing left to scrape.") : ScrapeIssuesAsync(ids, isInteractive: false, cancellationToken, existingJob);
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
            DataContext = new ComicVineMatchReviewDialogViewModel(bookLabel, initialQuery, initialCandidates, search, resolve, loadIssues: volumeId => orchestrator.LoadIssuesAsync(volumeId),
                provider: orchestrator.Provider, switchProvider: orchestrator.TrySwitchProvider),
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
