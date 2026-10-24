using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
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
    private readonly Func<int, string?> _getCoverPath = getCoverPath ?? (issueId => ResolveScrapeCoverPath(issueId, createContext));

    /// <summary>The real client for a source (shared rate-limited HTTP) using the login saved under Connections, or <c>null</c> when there is none.</summary>
    public static Func<ComicProvider, ComicVineRequestPriority, IScrapeComicVine?> CreateDefaultComicVine(Func<PaperbunkrDbContext> createContext) => (provider, priority) =>
    {
        using var context = createContext();
        var client = ComicProviderFactory.Create(context, provider, priority);
        return client is null ? null : new ScrapeComicVineAdapter(client, client, Paperbunkr.Data.ComicVine.Caching.ProviderCache.Shared, searchVolumesFirst: true);
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

    public async Task<string> ScrapeIssuesAsync(IReadOnlyList<int> issueIds, bool isInteractive = true, CancellationToken cancellationToken = default, IActivityJobHandle? existingJob = null,
        string jobKind = "Selection", ScrapeJobRecord? resuming = null)
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
            getCoverPath: _getCoverPath)
        {
            // The other source's copy of each issue, for per-field source choices and Metron's blanks (spec C section 4).
            SecondaryDetails = CreateSecondaryDetails(settings),
        };
        // A scheduled task already has its own Activity Center job; it lends it here so one run is one job, and settles it itself.
        string? jobTitle = existingJob is null && books.Count == 1
            ? $"Scraping {await ScrapeOrchestrator.ResolveBookLabelAsync(books[0], createContext, cancellationToken).ConfigureAwait(true)}"
            : null;
        using var owned = existingJob is null
            ? activity.StartJob(ActivityJobKind.Scrape, jobTitle ?? $"Scraping {books.Count} comics",
                cancellable: true, trigger: isInteractive ? ActivityTrigger.Manual : ActivityTrigger.Scheduled,
                startQueued: HeavyJobLane.Shared.WouldWait)
            : null;
        var job = existingJob ?? owned!;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, job.CancellationToken);

        // A source's "slow down" pause is shown, not sat through in silence (reported 2026-10-21: ComicVine scraping looked hung while its limiter
        // waited out a cool-off). Checked every few seconds for the length of the run.
        using var pauseWatch = new Timer(_ =>
        {
            if (ComicVineHttp.Handler.PausedUntil is { } until)
            {
                job.Report($"ComicVine asked Paperbunkr to slow down - continuing at {until.ToLocalTime():t}.");
            }
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));

        // Written down as it goes, so a cancel or a crash resumes from the last finished comic (docs/superpowers/specs/
        // 2026-10-21-scraper-organizer-b-speed-core-design.md section 8). Short batches are quicker to redo.
        var jobStore = ScrapeJobStore.Shared;
        ScrapeJobRecord? record = resuming;
        if (record is null && jobStore is not null && books.Count >= ScrapeJobStore.MinimumBooks)
        {
            record = new ScrapeJobRecord { Kind = jobKind, Interactive = isInteractive, Total = books.Count, Pending = books.Select(b => b.Id).ToList() };
            jobStore.Save(record);
        }

        var bookOrder = books.Select(b => b.Id).ToList();
        bool preview = isInteractive && settings.PreviewChanges && books.Count > 1;

        // "Undo this batch" (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 3): the before picture,
        // saved at the end for the comics the batch actually changed.
        ScrapeUndoBatch? undo = null;
        if (ScrapeUndoStore.Shared is not null)
        {
            using var undoContext = createContext();
            undo = ScrapeUndoStore.Capture(undoContext, bookOrder, books.Count == 1 ? jobTitle ?? "1 comic" : $"{books.Count} comics");
        }
        void NoteProgress(ScrapePhase phase, int index)
        {
            // Book `index` (1-based) is starting, so every book before it in this pass is finished. The Reviewing pass revisits deferred
            // books, which stay pending until the run ends.
            if (record is null || jobStore is null || phase == ScrapePhase.Reviewing)
            {
                return;
            }

            int finished = Math.Clamp(index - 1, 0, bookOrder.Count);
            record.Pending = bookOrder.Skip(finished).ToList();
            record.Done = (resuming?.Done ?? 0) + finished;
            if (finished % 5 == 0)
            {
                jobStore.Save(record);
            }
        }

        ScrapeBatchResult result;
        try
        {
            // One heavy job at a time (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md §4.3). A scheduled run
            // arrives already inside the lane with its own job, and passes straight through.
            using var laneSlot = HeavyJobLane.Activate(await HeavyJobLane.Shared.EnterAsync(isInteractive, linked.Token));
            owned?.Begin();

            if (!isInteractive)
            {
                result = await orchestrator.ScrapeAsync(books, isInteractive: false, interactiveReview: null, createContext, linked.Token,
                    onProgress: (phase, total, index, issue, bookLabel) =>
                    {
                        NoteProgress(phase, index);
                        job.Report(index, total, bookLabel);
                    }).ConfigureAwait(false);
            }
            else
            {
                // Preview (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 3): every match is worked out
                // without writing; the changes are shown at the end and only the comics the user keeps are applied.
                orchestrator.DryRun = preview;
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
                            NoteProgress(phase, index);
                            header.ReportProgress(phase, total, index, bookLabel, ThumbnailBytes(issue));
                            job.Report(index, total, bookLabel);
                        },
                        interactiveIssueReview: (label, volume, issues, autoMatched, ct) => ShowIssueReviewAsync(orchestrator, settings, label, issues, autoMatched)).ConfigureAwait(false);
                }

            }

            // ComicVine last (docs/superpowers/specs/2026-10-21-scraper-organizer-b-speed-core-design.md section 1): what Metron found nothing for
            // gets one unattended, cover-checked try on ComicVine before the run ends.
            ScrapeOrchestrator? fallback;
            (result, fallback) = await FallBackToComicVineAsync(result, books, orchestrator.Provider, settings, preview, linked.Token).ConfigureAwait(false);

            if (preview)
            {
                result = await ApplyPreviewedAsync(result, orchestrator, fallback, linked.Token).ConfigureAwait(false);
            }

            if (isInteractive)
            {
                // CE's real FinishForm (docs/superpowers/specs/2026-09-24-scraper-review-tables-and-
                // batch-summary-design.md §3.2) - a separate modal shown once the batch header/progress
                // UI has closed, not layered above it. Never shown for an unattended run, matching the
                // headless-automation gate every other modal in this scraper already respects.
                await ShowBatchSummaryAsync(result).ConfigureAwait(false);
            }
        }
        catch (ComicVineException ex) when (ScrapeOrchestrator.IsCredentialFailure(ex))
        {
            // The source refused the token/key/login: every other comic would fail the same way, so stop and say so plainly instead of
            // writing only the series-level fields (reported 2026-10-21). What's left stays resumable once the credential is fixed.
            if (record is not null)
            {
                jobStore!.Save(record);
            }

            string refused = $"{ex.Message} The scrape stopped; nothing more was changed.";
            owned?.Fail(refused, new ActivityLink(ActivityLinkKind.Preferences, "Connections"));
            return refused;
        }
        catch (OperationCanceledException)
        {
            if (record is not null)
            {
                jobStore!.Save(record); // kept: offered for resuming
            }

            if (undo is not null)
            {
                // Which comics were changed before the cancel isn't known here; restoring an unchanged one is harmless.
                ScrapeUndoStore.Shared!.Save(undo, bookOrder);
            }

            owned?.Fail(record is null ? "Scrape cancelled." : $"Scrape cancelled - {record.Pending.Count} comics left, resume it from Preferences → Organize & Scrape.");
            return "Scrape cancelled.";
        }

        if (record is not null)
        {
            jobStore!.Delete(record.Id);
        }

        UpdateReviewQueue(result, isInteractive);
        ScrapeDecisionHistory.Shared?.Flush();
        if (undo is not null)
        {
            ScrapeUndoStore.Shared!.Save(undo, result.Outcomes.Where(o => o.Kind == ScrapeOutcomeKind.Applied).Select(o => o.IssueId));
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

    /// <summary>
    /// Re-runs the comics Metron found nothing for on ComicVine, unattended and cover-checked, when that is switched on and a ComicVine key is
    /// saved. Their outcomes replace the Metron ones; everything else is returned as it was.
    /// </summary>
    private async Task<(ScrapeBatchResult Result, ScrapeOrchestrator? Fallback)> FallBackToComicVineAsync(ScrapeBatchResult result, IReadOnlyList<Issue> books, ComicProvider provider,
        ScrapeSettings settings, bool dryRun, CancellationToken cancellationToken)
    {
        if (provider != ComicProvider.Metron || !settings.FallBackToComicVine)
        {
            return (result, null);
        }

        var retry = result.Outcomes
            .Where(o => o.Kind == ScrapeOutcomeKind.NoMatchFound && o.Reason is not ("Permanently skipped" or "No series name on file"))
            .Select(o => o.IssueId)
            .ToHashSet();
        if (retry.Count == 0 || _createComicVine(ComicProvider.ComicVine, ComicVineRequestPriority.Low) is not { } comicVine)
        {
            return (result, null);
        }

        ScrapeSettings fallbackSettings;
        using (var context = createContext())
        {
            fallbackSettings = ScrapeSettings.Load(context);
        }

        fallbackSettings.AutoChooseTopMatch = true;
        var fallback = new ScrapeOrchestrator(comicVine, new ComicVineMatchMemory(createContext, ComicProvider.ComicVine), fallbackSettings, ComicProvider.ComicVine,
            getCoverPath: _getCoverPath) { DryRun = dryRun };
        var second = await fallback.ScrapeAsync(books.Where(b => retry.Contains(b.Id)).ToList(), isInteractive: false, interactiveReview: null, createContext, cancellationToken)
            .ConfigureAwait(false);

        var replaced = second.Outcomes.ToDictionary(o => o.IssueId);
        return (new ScrapeBatchResult(result.Outcomes
            .Select(o => replaced.TryGetValue(o.IssueId, out var r)
                ? (r.Kind == ScrapeOutcomeKind.Applied ? r with { Reason = $"{r.Reason} (ComicVine, after Metron found nothing)" } : o)
                : o)
            .ToList()), fallback);
    }

    /// <summary>
    /// Finds a matched issue on the other source through the cross-reference id the two keep: a Metron issue names its ComicVine id
    /// (<c>cv_id</c>); a ComicVine issue is looked up on Metron by that id. Null when nothing needs the other source, or it has no saved credential.
    /// </summary>
    private Func<ComicProvider, ComicVineIssueDetails, CancellationToken, Task<ComicVineIssueDetails?>>? CreateSecondaryDetails(ScrapeSettings settings)
    {
        bool anyPreference = settings.FieldSourcePreference.Count > 0;
        if (!anyPreference && !settings.FillBlanksFromComicVine)
        {
            return null;
        }

        var comicVine = _createComicVine(ComicProvider.ComicVine, ComicVineRequestPriority.Low);
        var metron = anyPreference ? _createComicVine(ComicProvider.Metron, ComicVineRequestPriority.Low) : null;
        return async (primary, details, ct) =>
        {
            if (primary == ComicProvider.Metron)
            {
                return comicVine is not null && details.ComicVineId is int cvId ? await comicVine.GetIssueDetailsAsync(cvId, ct).ConfigureAwait(false) : null;
            }

            if (metron is not IComicIssueLookup lookup)
            {
                return null;
            }

            var hits = await lookup.FindIssuesByComicVineIdAsync(details.Id, ct).ConfigureAwait(false);
            return hits.Count == 1 ? await metron.GetIssueDetailsAsync(hits[0].IssueId, ct).ConfigureAwait(false) : null;
        };
    }

    /// <summary>
    /// The end of a previewed run: shows every proposed change, applies the comics the user keeps (with the decisions already made), and
    /// turns the rest into "skipped". Cancelling the preview applies nothing.
    /// </summary>
    private async Task<ScrapeBatchResult> ApplyPreviewedAsync(ScrapeBatchResult result, ScrapeOrchestrator primary, ScrapeOrchestrator? fallback, CancellationToken cancellationToken)
    {
        var proposals = primary.Proposals.Concat(fallback?.Proposals ?? Array.Empty<ScrapeProposal>()).ToList();
        var keep = proposals.Count == 0
            ? new HashSet<int>()
            : (await modalHost.ShowAsync<IReadOnlyCollection<int>?>(resolve => new ScrapePreviewDialogView
            {
                DataContext = new ScrapePreviewDialogViewModel(proposals, resolve),
            }).ConfigureAwait(true))?.ToHashSet() ?? new HashSet<int>();

        var applied = new HashSet<int>(await primary.ApplyProposalsAsync(primary.Proposals.Where(p => keep.Contains(p.IssueId)), createContext, cancellationToken).ConfigureAwait(true));
        if (fallback is not null)
        {
            applied.UnionWith(await fallback.ApplyProposalsAsync(fallback.Proposals.Where(p => keep.Contains(p.IssueId)), createContext, cancellationToken).ConfigureAwait(true));
        }

        return new ScrapeBatchResult(result.Outcomes
            .Select(o => o.Kind == ScrapeOutcomeKind.Applied && !applied.Contains(o.IssueId) ? o with { Kind = ScrapeOutcomeKind.SkippedByUser, Reason = "Not applied after the preview" } : o)
            .ToList());
    }

    /// <summary>
    /// What an unattended run could not confirm goes to the review queue; what this run applied leaves it
    /// (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 2).
    /// </summary>
    private static void UpdateReviewQueue(ScrapeBatchResult result, bool isInteractive)
    {
        var queue = ScrapeReviewQueue.Shared;
        if (queue is null)
        {
            return;
        }

        queue.Remove(result.Outcomes.Where(o => o.Kind is ScrapeOutcomeKind.Applied or ScrapeOutcomeKind.SkippedByUser).Select(o => o.IssueId));
        if (!isInteractive)
        {
            queue.Add(result.Outcomes
                .Where(o => o.Kind == ScrapeOutcomeKind.NoMatchFound && o.Reason is not ("Permanently skipped" or "No series name on file"))
                .Select(o => new ScrapeReviewItem(o.IssueId, o.BookLabel, o.Reason, DateTime.UtcNow)));
        }
    }

    /// <summary>"Scrape all unmatched" (same spec, section 8): every comic never scraped, unattended, as one resumable job.</summary>
    public Task<string> ScrapeAllUnmatchedAsync(CancellationToken cancellationToken = default)
    {
        List<int> ids;
        using (var context = createContext())
        {
            ids = context.Issues.Where(i => i.MetadataSource == null && string.IsNullOrEmpty(i.Volume) && i.FilePath != null && !i.ScrapePermanentlySkipped)
                .Select(i => i.Id).ToList();
        }

        return ids.Count == 0 ? Task.FromResult("Every comic already has a match.") : ScrapeIssuesAsync(ids, isInteractive: false, cancellationToken, jobKind: "UnmatchedAll");
    }

    /// <summary>
    /// The review list: each Review closes it and runs an interactive scrape of that series, then the list comes back while anything is left.
    /// Never nests one modal inside another (the scraper's dialogs share one modal host).
    /// </summary>
    public async Task OpenReviewQueueAsync()
    {
        var queue = ScrapeReviewQueue.Shared;
        if (queue is null)
        {
            return;
        }

        while (true)
        {
            var toScrape = await modalHost.ShowAsync<IReadOnlyList<int>?>(resolve => new ScrapeReviewQueueDialogView
            {
                DataContext = new ScrapeReviewQueueDialogViewModel(queue, createContext, resolve),
            }).ConfigureAwait(true);

            if (toScrape is not { Count: > 0 })
            {
                return;
            }

            await ScrapeIssuesAsync(toScrape, isInteractive: true).ConfigureAwait(true);
            if (queue.Load().Count == 0)
            {
                return;
            }
        }
    }

    /// <summary>Restores the comics of a scrape batch to how they were before it, and queues them to be written back to their files.</summary>
    public string UndoBatch(ScrapeUndoBatch batch)
    {
        IReadOnlyList<int> restored;
        using (var context = createContext())
        {
            restored = ScrapeUndoStore.Shared?.Undo(context, batch) ?? Array.Empty<int>();
        }

        foreach (var id in restored)
        {
            enqueueWriteBack(id);
        }

        return restored.Count == 0 ? "Nothing to undo." : $"Put {restored.Count} comic{(restored.Count == 1 ? "" : "s")} back as they were before that scrape.";
    }

    /// <summary>
    /// The scheduled health pass (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 10): the review list is
    /// tried again unattended (what still can't be confirmed stays on it), then leftover swap files are swept from the library folders.
    /// </summary>
    public async Task<string> RunHealthPassAsync(CancellationToken cancellationToken, IActivityJobHandle? existingJob = null)
    {
        var parts = new List<string>();
        var waiting = ScrapeReviewQueue.Shared?.Load().Select(i => i.IssueId).ToList() ?? new List<int>();
        if (waiting.Count > 0)
        {
            int before = waiting.Count;
            await ScrapeIssuesAsync(waiting, isInteractive: false, cancellationToken, existingJob, jobKind: "Health").ConfigureAwait(false);
            int after = ScrapeReviewQueue.Shared?.Load().Count ?? 0;
            parts.Add($"{Math.Max(0, before - after)} of {before} waiting comics matched");
        }

        List<string> roots;
        using (var context = createContext())
        {
            roots = context.WatchedFolders.AsNoTracking().Select(f => f.Path).ToList();
        }

        var sweep = await Task.Run(() => Paperbunkr.Data.Library.SwapFileSweeper.Sweep(roots, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (sweep.Deleted > 0)
        {
            parts.Add($"{sweep.Deleted} leftover file{(sweep.Deleted == 1 ? "" : "s")} removed");
        }

        if (sweep.RestoreCandidates.Count > 0)
        {
            parts.Add($"{sweep.RestoreCandidates.Count} leftover cop{(sweep.RestoreCandidates.Count == 1 ? "y" : "ies")} kept for review in Library Health");
        }

        return parts.Count == 0 ? "Nothing needed attention." : string.Join("; ", parts) + ".";
    }

    /// <summary>Picks up a batch that was cancelled or cut short, from its first unfinished comic.</summary>
    public Task<string> ResumeAsync(ScrapeJobRecord job, CancellationToken cancellationToken = default) =>
        ScrapeIssuesAsync(job.Pending, isInteractive: false, cancellationToken, jobKind: job.Kind, resuming: job);

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

    private byte[]? ThumbnailBytes(Issue issue)
    {
        try
        {
            string? path = _getCoverPath(issue.Id);
            return path is not null && File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ResolveScrapeCoverPath(int issueId, Func<PaperbunkrDbContext> createContext)
    {
        string? existing = CoverThumbnailService.GetEffectiveCoverPath(issueId);
        if (existing is not null)
        {
            return existing;
        }

        string? filePath;
        long? fileSize;
        using (var context = createContext())
        {
            var source = context.Issues.Where(issue => issue.Id == issueId)
                .Select(issue => new { issue.FilePath, issue.FileSize, issue.RemoteSourceId })
                .FirstOrDefault();
            if (source is null || source.RemoteSourceId is not null || string.IsNullOrWhiteSpace(source.FilePath))
            {
                return null;
            }

            filePath = source.FilePath;
            fileSize = source.FileSize;
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        // The review header and the automatic cover-match gate both need the local cover. Older
        // libraries can have a valid archive but no generated thumbnail yet, so populate the
        // normal cache from the archive using its effective tagged/preferred cover page.
        if (!new CoverThumbnailService(createContext).TryGenerateThumbnail(issueId, filePath, fileSize))
        {
            return null;
        }

        return CoverThumbnailService.GetEffectiveCoverPath(issueId);
    }
}
