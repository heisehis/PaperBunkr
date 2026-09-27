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
    Func<ComicProvider, ComicVineRequestPriority, IScrapeComicVine?>? createComicVine = null,
    Func<int, string?>? getCoverPath = null)
{
    private readonly Func<ComicProvider, ComicVineRequestPriority, IScrapeComicVine?> _createComicVine = createComicVine ?? CreateDefaultComicVine(createContext);
    private readonly Func<int, string?> _getCoverPath = getCoverPath ?? CoverThumbnailService.GetEffectiveCoverPath;

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
            switchTo => _createComicVine(switchTo, priority) is { } source ? (source, new ComicVineMatchMemory(createContext, switchTo)) : null,
            getCoverPath: _getCoverPath);
        // A scheduled task already has its own Activity Center job; it lends it here so one run is one job, and settles it itself.
        string? jobTitle = existingJob is null && books.Count == 1
            ? $"Scraping {await ScrapeOrchestrator.ResolveBookLabelAsync(books[0], createContext, cancellationToken).ConfigureAwait(true)}"
            : null;
        using var owned = existingJob is null
            ? activity.StartJob(ActivityJobKind.Scrape, jobTitle ?? $"Scraping {books.Count} comics",
                cancellable: true, trigger: isInteractive ? ActivityTrigger.Manual : ActivityTrigger.Scheduled)
            : null;
        var job = existingJob ?? owned!;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, job.CancellationToken);

        ScrapeBatchResult result;
        try
        {
            if (!isInteractive)
            {
                result = await orchestrator.ScrapeAsync(books, isInteractive: false, interactiveReview: null, createContext, linked.Token,
                    onProgress: (phase, total, index, issue, bookLabel) => job.Report(index, total, bookLabel)).ConfigureAwait(false);
            }
            else
            {
                var header = new ScrapeBatchHeaderViewModel(books.Count, linked.Cancel);
                using (var batch = modalHost.BeginBatch(new ScrapeBatchHeaderView { DataContext = header }))
                {
                    result = await orchestrator.ScrapeAsync(
                        books,
                        isInteractive: true,
                        (label, query, candidates, search, ct) => ShowMatchReviewAsync(orchestrator, settings, label, query, candidates, search),
                        createContext,
                        linked.Token,
                        onProgress: (phase, total, index, issue, bookLabel) =>
                        {
                            header.ReportProgress(phase, total, index, bookLabel, ThumbnailBytes(issue));
                            job.Report(index, total, bookLabel);
                        },
                        interactiveIssueReview: (label, volume, issues, autoMatched, ct) => ShowIssueReviewAsync(orchestrator, settings, label, issues, autoMatched)).ConfigureAwait(false);
                }

                // CE's real FinishForm (docs/superpowers/specs/2026-09-24-scraper-review-tables-and-
                // batch-summary-design.md §3.2) - a separate modal shown once the batch header/progress
                // UI has closed, not layered above it. Never shown for an unattended run, matching the
                // headless-automation gate every other modal in this scraper already respects.
                await ShowBatchSummaryAsync(result).ConfigureAwait(false);
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

        // The real breakdown (docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md
        // §4.1), not just the old bare "applied to N of M" - only the non-zero buckets beyond Applied
        // are named, so a clean run still reads as the simple sentence it always did.
        var parts = new List<string> { $"Applied a {ComicProviderFactory.DisplayName(orchestrator.Provider)} match to {result.Applied} of {books.Count} comic{(books.Count == 1 ? string.Empty : "s")}." };
        if (result.SkippedByUser > 0) parts.Add($"{result.SkippedByUser} skipped.");
        if (result.NoMatchFound > 0) parts.Add($"{result.NoMatchFound} no match found.");
        if (result.Failed > 0) parts.Add($"{result.Failed} failed.");
        // Write-back is off by default and otherwise silent - a scrape that applied metadata but never
        // touched the .cbz files looked, to the user, like the write step had just never reported back.
        if (result.Applied > 0)
        {
            using var settingsContext = createContext();
            var app = settingsContext.GetOrCreateAppSettings();
            if (!app.WriteMetadataToFiles)
            {
                parts.Add("Not written into the comic files - turn on Preferences → Advanced → Comic file metadata.");
            }
            else if (!app.WriteMetadataAutomatically)
            {
                parts.Add("Not written into the comic files yet - use Write Now… under Preferences → Advanced, or turn on automatic writing.");
            }
            else
            {
                parts.Add("Writing ComicInfo.xml into the comic files now - a second notice follows when that finishes.");
            }
        }

        var summary = string.Join(" ", parts);
        owned?.Succeed(summary, itemsProcessed: result.Applied, itemsFailed: result.Failed);
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

    /// <summary>Shown when a series' Detail page asks for the scrape panel: not for the manga family, whose sources are different.
    /// <paramref name="onScraped"/> fires once the batch actually finishes (whether it applied to
    /// anything or not) - the panel itself has no way to refresh the Issues tab's tiles it lives
    /// beside, so without this callback every field the scrape just wrote stayed invisible until the
    /// user navigated away from Detail and back (the panel's own "Applied a match to N of M" status
    /// text was the only visible sign anything happened at all). Optional so every pre-existing
    /// caller/test that doesn't pass it keeps today's exact behavior.</summary>
    public Control? CreateSeriesPanel(Series series, Action? onScraped = null)
    {
        if (series.ContentType is ContentType.Manga or ContentType.Manhua or ContentType.Manhwa)
        {
            return null;
        }

        return new SeriesScraperPanelView
        {
            DataContext = new SeriesScraperPanelViewModel(series.Name, async () =>
            {
                string result = await ScrapeSeriesAsync(series.Id).ConfigureAwait(true);
                onScraped?.Invoke();
                return result;
            }),
        };
    }

    // The dialogs use a plain data fetch for the issue list: nesting a second modal inside the first crashed the original plugin when the queued modal was cancelled.
    private Task<ComicVineVolumeSearchResult?> ShowMatchReviewAsync(
        ScrapeOrchestrator orchestrator,
        ScrapeSettings settings,
        string bookLabel,
        string initialQuery,
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)> initialCandidates,
        ScrapeOrchestrator.SearchAndRankDelegate search) =>
        modalHost.ShowAsync<ComicVineVolumeSearchResult?>(resolve => new ComicVineMatchReviewDialogView
        {
            DataContext = new ComicVineMatchReviewDialogViewModel(bookLabel, initialQuery, initialCandidates, search, resolve, loadIssues: volumeId => orchestrator.LoadIssuesAsync(volumeId),
                provider: orchestrator.Provider, switchProvider: orchestrator.TrySwitchProvider,
                forceSeriesArt: settings.ForceSeriesArt, showCovers: settings.ShowCovers, findIssueCoverUrl: orchestrator.FindIssueCoverUrlAsync,
                markPermanentlySkipped: () => MarkPermanentlySkipped(orchestrator.CurrentIssueId)),
        });

    private Task<ComicVineIssueReviewResult> ShowIssueReviewAsync(ScrapeOrchestrator orchestrator, ScrapeSettings settings, string bookLabel, IReadOnlyList<ComicVineIssueSummary> issues, ComicVineIssueSummary? autoMatched) =>
        modalHost.ShowAsync<ComicVineIssueReviewResult>(resolve => new ComicVineIssueReviewDialogView
        {
            DataContext = new ComicVineIssueReviewDialogViewModel(bookLabel, issues, autoMatched, readOnlyPeek: false, resolve, settings.ShowCovers,
                markPermanentlySkipped: () => MarkPermanentlySkipped(orchestrator.CurrentIssueId)),
        });

    private Task<bool> ShowBatchSummaryAsync(ScrapeBatchResult result) =>
        modalHost.ShowAsync<bool>(resolve => new ScrapeBatchSummaryDialogView
        {
            DataContext = new ScrapeBatchSummaryDialogViewModel(result, () => resolve(true)),
        });

    /// <summary>Durable "never auto-match this book again" marker (docs/superpowers/specs/2026-09-24-
    /// comicvine-scraper-fidelity-plan.md Step 16) - CE's real <c>book.skip_forever()</c>. Synchronous
    /// and best-effort: a failed write here shouldn't crash the dialog resolution it's called from.</summary>
    private void MarkPermanentlySkipped(int issueId)
    {
        using var context = createContext();
        var issue = context.Issues.Find(issueId);
        if (issue is not null)
        {
            issue.ScrapePermanentlySkipped = true;
            context.SaveChanges();
        }
    }

    private static byte[]? ThumbnailBytes(Issue issue)
    {
        var path = CoverThumbnailPaths.GetCachePath(CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize));
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
