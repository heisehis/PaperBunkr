
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// Ties <see cref="ComicVineService"/>, <see cref="MatchScoreCalculator"/>, and
/// <see cref="ComicVineMatchMemory"/> together into the actual "scrape this book" flow (design doc
/// §4) - search by series name, rank candidates, resolve via the review dialog (interactive) or
/// auto-choose/skip-and-log (design §4/§9's headless-automation gate), then apply.
///
/// <b>Full field-toggle-matrix apply</b>, not just the volume-level subset an earlier pass shipped.
/// Once a volume is chosen, <see cref="FindIssueDetailsAsync"/> pages through
/// <see cref="ComicVineService.SearchIssuesAsync"/> for that volume to find the specific issue whose
/// <c>issue_number</c> matches this book's own number (or the volume's only issue, for a one-shot/TPB),
/// then <see cref="ComicVineService.GetIssueDetailsAsync"/> fetches its full detail record - title,
/// summary, dates, story arcs, characters/teams/locations, and person credits. All 20 entries of
/// <see cref="ScrapeField"/> are applied from there (volume-level: <see cref="ScrapeField.Publisher"/>/
/// <see cref="ScrapeField.Imprint"/>/<see cref="ScrapeField.Volume"/>; per-issue: everything else),
/// each still gated by <see cref="ScrapeSettings.OverwriteExisting"/>/
/// <see cref="ScrapeSettings.IgnoreBlankValues"/>/<see cref="ScrapeSettings.EnabledScrapeFields"/>. No
/// matching issue found within the volume (a network failure, or no <c>issue_number</c> lines up) just
/// means the per-issue fields are skipped for that book - the volume-level fields still apply, same
/// per-item resilience principle as the rest of this orchestrator.
/// </summary>
/// <summary>Which of the four buckets one book landed in (docs/superpowers/specs/2026-09-24-scraper-
/// review-tables-and-batch-summary-design.md §3.1): <see cref="Applied"/> a match was written
/// (volume-only or full issue detail); <see cref="SkippedByUser"/> a human explicitly declined this
/// book (Skip, or closed a review dialog); <see cref="NoMatchFound"/> nothing was written and no
/// human was involved either - covers a genuinely empty search with no reviewer available, and every
/// headless-automation decline where confirmation was required but nothing could ask for it ("can't
/// confirm" is treated the same as "nothing found" for this count, since neither wrote anything);
/// <see cref="Failed"/> the search itself threw (<see cref="ComicVineException"/>), not just came
/// back empty.</summary>
public enum ScrapeOutcomeKind { Applied, SkippedByUser, NoMatchFound, Failed }

/// <summary>Which stage of a batch a given <c>onProgress</c> callback is reporting for (2026-09-25,
/// CE's real two-pass auto-choose flow, verified against <c>scrapeengine.py</c>): <see cref="Single"/>
/// for any run that never defers at all (non-interactive, auto-choose off, or no reviewer given) -
/// every book gets exactly one report, same as before this phase concept existed.
/// <see cref="AutoMatching"/> for the first pass of a two-phase (interactive, auto-choose on) run,
/// where every book in the original batch gets its automatic shot. <see cref="Reviewing"/> for the
/// second pass, only entered if anything actually got deferred - interactively resolving just those,
/// with their own separate 1-based count.</summary>
public enum ScrapePhase { Single, AutoMatching, Reviewing }

/// <summary>One book's real outcome from a <see cref="ScrapeOrchestrator.ScrapeAsync"/> batch -
/// <paramref name="Reason"/> is a short, human-readable explanation for the App layer's
/// <c>ScrapeBatchSummaryDialogViewModel</c>'s per-bucket lists (docs/superpowers/specs/2026-09-24-
/// scraper-review-tables-and-batch-summary-design.md §3.2); null only for a plain
/// <see cref="ScrapeOutcomeKind.Applied"/> where nothing beyond the match itself needs explaining.</summary>
public sealed record ScrapeBookOutcome(int IssueId, string BookLabel, ScrapeOutcomeKind Kind, string? Reason);

/// <summary>The real breakdown behind one <see cref="ScrapeOrchestrator.ScrapeAsync"/> batch (docs/
/// superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md §4.1, extended by docs/
/// superpowers/specs/2026-09-24-scraper-review-tables-and-batch-summary-design.md §3.1 to carry
/// per-book detail instead of just counts) - the four counts below are computed from
/// <see cref="Outcomes"/>, not stored separately, so they can never drift from the list a summary UI
/// actually renders.</summary>
public sealed record ScrapeBatchResult(IReadOnlyList<ScrapeBookOutcome> Outcomes)
{
    public int Applied => Outcomes.Count(o => o.Kind == ScrapeOutcomeKind.Applied);
    public int SkippedByUser => Outcomes.Count(o => o.Kind == ScrapeOutcomeKind.SkippedByUser);
    public int NoMatchFound => Outcomes.Count(o => o.Kind == ScrapeOutcomeKind.NoMatchFound);
    public int Failed => Outcomes.Count(o => o.Kind == ScrapeOutcomeKind.Failed);
    public int Total => Outcomes.Count;
}

public sealed class ScrapeOrchestrator
{
    /// <summary>Swappable in tests (a fake handler) so <see cref="HashRemoteCoverAsync"/> never needs a live network call - mirrors <see cref="ComicVineClient.RetryDelay"/>'s test-seam pattern.</summary>
    internal static HttpClient CoverHttp { get; set; } = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Overrides <see cref="ScrapeSettings.ScrapeDelayMs"/>'s clamped inter-book delay in tests - same test-seam pattern as <see cref="ComicVineClient.RetryDelay"/>, so a multi-book batch test never actually waits 2+ real seconds.</summary>
    internal static TimeSpan? ScrapeDelayOverride { get; set; }

    /// <summary>Counts how many times the inter-book delay actually ran, regardless of its configured
    /// length - lets a test assert "delayed once, not zero or twice" without depending on wall-clock
    /// timing at all (a real-duration assertion is flaky under a full parallel test-suite run's own
    /// CPU/thread-pool contention - confirmed live: 200ms measured as 148 real seconds under load).</summary>
    internal static int DelayInvocationCount { get; set; }

    private IScrapeComicVine _comicVine;
    private ComicVineMatchMemory _matchMemory;
    private readonly ScrapeSettings _settings;
    private readonly Func<ComicProvider, (IScrapeComicVine Source, ComicVineMatchMemory Memory)?>? _providerSwitcher;
    private readonly Func<int, string?>? _getCoverPath;

    /// <param name="getCoverPath">Resolves a local <see cref="Issue"/> id to the file path of its own
    /// decoded cover, for the auto-match cover-hash safety gate (docs/superpowers/specs/2026-09-24-
    /// comicvine-scraper-fidelity-design.md §2.1) - null (the default) disables the gate entirely, since
    /// this class lives in <c>Paperbunkr.Data</c> and can't reference <c>Paperbunkr.App</c>'s
    /// <c>CoverThumbnailService</c> directly; the caller (<c>ScrapeCoordinator</c>) supplies it.</param>
    public ScrapeOrchestrator(IScrapeComicVine comicVine, ComicVineMatchMemory matchMemory, ScrapeSettings settings,
        ComicProvider provider = ComicProvider.ComicVine, Func<ComicProvider, (IScrapeComicVine Source, ComicVineMatchMemory Memory)?>? providerSwitcher = null,
        Func<int, string?>? getCoverPath = null)
    {
        _comicVine = comicVine;
        _matchMemory = matchMemory;
        _settings = settings;
        Provider = provider;
        _providerSwitcher = providerSwitcher;
        _getCoverPath = getCoverPath;
    }

    /// <summary>The source this run is currently searching and reading details from. Changes only through <see cref="TrySwitchProvider"/> (the review dialog's source switch).</summary>
    public ComicProvider Provider { get; private set; }

    /// <summary>The book currently being reviewed's own effective issue number - set once per book at
    /// the top of <see cref="ScrapeAsync"/>'s loop, read by <see cref="FindIssueCoverUrlAsync"/>.</summary>
    public string? CurrentBookNumber { get; private set; }

    /// <summary>The local <see cref="Issue.Id"/> currently being reviewed - set once per book at the
    /// top of <see cref="ScrapeAsync"/>'s loop, read by the caller when wiring a review dialog's
    /// "permanently skip" action (Step 16) to the right book.</summary>
    public int CurrentIssueId { get; private set; }

    /// <summary>
    /// Points the rest of the run at another source (the match dialog's per-run switch): searches, issue lists, details and the match memory all follow it. Returns
    /// false, leaving the run as it was, when that source can't be built (its login isn't saved) or this orchestrator wasn't given a way to build one.
    /// </summary>
    public bool TrySwitchProvider(ComicProvider provider)
    {
        if (provider == Provider)
        {
            return true;
        }

        var next = _providerSwitcher?.Invoke(provider);
        if (next is null)
        {
            return false;
        }

        (_comicVine, _matchMemory) = next.Value;
        Provider = provider;
        _issueSummaryCache.Clear();   // ids from the other source mean nothing here
        _arcOrder = null;
        return true;
    }

    /// <summary>
    /// Re-runs a ComicVine search for whatever query the review dialog is currently showing and
    /// scores the results against one specific book - CE's real flow lets the user retype the query
    /// (pre-filled with the series name, not locked to it) when the automatic guess misses, rather
    /// than only ever picking from one fixed, un-editable candidate list. Kept as a caller-visible
    /// delegate type (not an inline <c>Func</c>) purely for readability at the call sites.
    /// </summary>
    public delegate Task<IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>> SearchAndRankDelegate(string query, CancellationToken cancellationToken);

    /// <summary>Interactive review of the specific issue within an already-chosen volume (docs/
    /// superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-design.md §3) - called only when
    /// interactive, auto-choose is off, and <see cref="ScrapeSettings.ConfirmIssueMatch"/> is on.
    /// Receives the full issue list for the volume plus whichever one <c>FindByNumber</c>'s own
    /// logic auto-matched (possibly null), and returns how the user resolved it.</summary>
    public delegate Task<ComicVineIssueReviewResult> InteractiveIssueReviewDelegate(
        string bookLabel,
        ComicVineVolumeSearchResult volume,
        IReadOnlyList<ComicVineIssueSummary> issues,
        ComicVineIssueSummary? autoMatched,
        CancellationToken cancellationToken);

    /// <summary>Fires once per book, before that book's series search - not once per dialog step, so
    /// a book that goes through both the series and issue dialogs (or "Go Back"s between them) only
    /// advances a batch-progress counter once. <c>onProgress</c>/<c>interactiveIssueReview</c> are
    /// both optional so every pre-existing caller (and test) that doesn't pass them keeps today's
    /// exact behavior.</summary>
    /// <summary>
    /// <paramref name="interactiveReview"/> is called only when <paramref name="isInteractive"/> is
    /// true and auto-choose is off - shown even when the automatic search found zero candidates
    /// (previously a silent skip with no feedback at all), since the user can still type a better
    /// query themselves via the passed-in <see cref="SearchAndRankDelegate"/>. A non-interactive run
    /// with auto-choose off skips the book entirely rather than ever attempting to show a modal
    /// (design §4/§9's headless-automation fix - the actual reason this parameter is nullable).
    /// </summary>
    public async Task<ScrapeBatchResult> ScrapeAsync(
        IReadOnlyList<Issue> books,
        bool isInteractive,
        Func<string, string, IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>, SearchAndRankDelegate, CancellationToken, Task<ComicVineVolumeSearchResult?>>? interactiveReview,
        Func<PaperbunkrDbContext> createDbContext,
        CancellationToken cancellationToken = default,
        Action<ScrapePhase, int, int, Issue, string>? onProgress = null,
        InteractiveIssueReviewDelegate? interactiveIssueReview = null)
    {
        var outcomes = new List<ScrapeBookOutcome>();

        // CE's real two-pass auto-choose flow (scrapeengine.py __scrape/__scrape_book, verified
        // directly against source 2026-09-25): with auto-choose on, CE never interrupts the batch to
        // ask about a book its own automatcher (automatcher.py's find_series_ref, verified) couldn't
        // confidently resolve - it silently sets that book aside (BookStatus("DELAYED"), appended to
        // the end of its own worklist) and keeps auto-matching the rest. Only once every book has had
        // its automatic shot does CE loop back and interactively resolve whatever got set aside, one
        // dialog after another - "keep going till it's scraped all the matched comics, then let you
        // sort out the unmatched ones" (the user's own words, 2026-09-25). Paperbunkr's earlier
        // "manual-search fallback" (docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-
        // design.md §2.8) modeled the wrong CE codepath - the always-immediate BookStatus("UNSCRAPED")
        // retry, not the auto-scrape-specific deferred one - so it blocked on a dialog for the very
        // first unmatched book instead of finishing the batch first. find_series_ref (verified) returns
        // "no match" for both an empty search AND a failed cover-hash check, so both of Paperbunkr's
        // equivalent failure modes defer identically. A deferred book's own review still does a fresh
        // search (not a reused one) - CE's automatcher and its later interactive step are genuinely
        // separate queries (automatcher.py's own __find_best_series vs scrapeengine.py's
        // __query_series_refs), not one cached and replayed for the other.
        bool twoPhaseAutoChoose = isInteractive && _settings.AutoChooseTopMatch && interactiveReview is not null;
        var deferred = new List<Issue>();

        int index = 0;
        foreach (Issue issue in books)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // CE's real proactive inter-book delay (scrapeengine.py:231-235, verified: "wait for the
            // scrape delay to pass after scraping each book...don't do this for...the first book") -
            // distinct from and in addition to the existing per-HTTP-request 1100ms spacing inside the
            // client itself. Never waits before the first book of this phase.
            if (index > 0)
            {
                DelayInvocationCount++;
                TimeSpan delay = ScrapeDelayOverride ?? TimeSpan.FromMilliseconds(Math.Clamp(_settings.ScrapeDelayMs, 2000, 3_600_000));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            index++;
            bool deferredThisBook = await ProcessBookAsync(
                issue, books.Count, index, twoPhaseAutoChoose ? ScrapePhase.AutoMatching : ScrapePhase.Single).ConfigureAwait(false);
            if (deferredThisBook)
            {
                deferred.Add(issue);
            }
        }

        if (deferred.Count > 0)
        {
            // No inter-book delay here (scrapeengine.py:236, verified: delayed_b skips
            // __wait_until_ready entirely) - a deferred book's own dialog interaction already paces
            // requests through real human time, so the artificial delay would be pure dead air.
            int reviewIndex = 0;
            foreach (Issue issue in deferred)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reviewIndex++;
                await ProcessBookAsync(issue, deferred.Count, reviewIndex, ScrapePhase.Reviewing).ConfigureAwait(false);
            }
        }

        return new ScrapeBatchResult(outcomes);

        // Returns true only when this book was set aside for the Reviewing phase instead of being
        // resolved now (only possible when phase is AutoMatching) - every other path fully resolves
        // the book itself, always calling Record before returning false.
        async Task<bool> ProcessBookAsync(Issue issue, int total, int bookIndex, ScrapePhase phase)
        {
            CurrentIssueId = issue.Id;

            // Resolved once per book, up front, so every label shown for this book - the batch
            // header's current-book text, the review dialogs' own subtitle, and every outcome in the
            // end-of-batch summary - agrees with each other and with what FindByNumber below actually
            // used. See ResolveEffectiveNumberAndYearAsync's own doc comment for why
            // issue.EffectiveNumber() alone isn't enough here.
            (string? effectiveNumber, int? effectiveYear) = await ResolveEffectiveNumberAndYearAsync(issue, createDbContext, cancellationToken).ConfigureAwait(false);
            CurrentBookNumber = effectiveNumber;
            string bookLabel = BookLabel(issue, effectiveNumber);

            onProgress?.Invoke(phase, total, bookIndex, issue, bookLabel);

            void Record(ScrapeOutcomeKind kind, string? reason = null) =>
                outcomes.Add(new ScrapeBookOutcome(issue.Id, bookLabel, kind, reason));

            // CE's real book.skip_forever() (comicbook.py:109, verified) - a book marked this way is
            // silently skipped on every future scrape, interactive or unattended, without even asking -
            // this is the single choke point every scrape path funnels through, so it's enforced here
            // rather than at each caller's own book-selection query.
            if (issue.ScrapePermanentlySkipped)
            {
                Record(ScrapeOutcomeKind.NoMatchFound, "Permanently skipped");
                return false;
            }

            string? seriesName = issue.Series?.Name;
            if (string.IsNullOrWhiteSpace(seriesName))
            {
                Record(ScrapeOutcomeKind.NoMatchFound, "No series name on file");
                return false;
            }

            // Keyed on the book's own series name regardless of what the user later retypes in the
            // dialog - priorscore's whole point is "this volume was previously chosen for a
            // similarly-named book", a per-series memory, not a per-search-string one.
            string searchKey = ComicVineMatchMemory.NormalizeSearchKey(seriesName);

            // Set (and cleared) fresh on every SearchAndRank call, not just once per book - the "Go
            // Back"/retype loop below can call it again after an earlier failure, and a later success
            // must not still be misreported through a stale true/message from the earlier attempt.
            bool searchFailed = false;
            string? searchFailureReason = null;

            async Task<IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>> SearchAndRank(string query, CancellationToken ct)
            {
                searchFailed = false;
                searchFailureReason = null;
                IReadOnlyList<ComicVineVolumeSearchResult> found;
                try
                {
                    // CE's db.query_series_refs order (verified): strip ignored terms, clean the terms up,
                    // search; if that finds nothing, retry once with digits/words swapped ("8" <-> "eight").
                    // ComicVine only: the cleanup exists to suit ComicVine's own search. Metron's is a plain
                    // name-contains filter, where rewriting "Batman & Robin" to "batman and robin" would
                    // stop it matching, so Metron keeps getting the query as typed.
                    bool comicVineQuery = Provider == ComicProvider.ComicVine;
                    string cleaned = comicVineQuery
                        ? SearchTermCleaner.Clean(StripIgnoredSearchTerms(query), alternate: false)
                        : StripIgnoredSearchTerms(query);
                    found = string.IsNullOrWhiteSpace(cleaned)
                        ? Array.Empty<ComicVineVolumeSearchResult>()
                        : await _comicVine.SearchVolumesAsync(cleaned, cancellationToken: ct).ConfigureAwait(false);
                    if (comicVineQuery && found.Count == 0 && cleaned.Length > 0)
                    {
                        string alternate = SearchTermCleaner.Clean(cleaned, alternate: true);
                        if (alternate.Length > 0 && alternate != cleaned)
                        {
                            found = await _comicVine.SearchVolumesAsync(alternate, cancellationToken: ct).ConfigureAwait(false);
                        }
                    }
                }
                catch (ComicVineException ex)
                {
                    // A network/API failure for this one search shouldn't abort the batch - same
                    // per-item isolation principle as LibraryOrganizerService.ExecuteAsync. Returning
                    // empty (not throwing) also lets an interactive user just try again. Recorded as a
                    // real Failed count (docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-
                    // design.md §4.1), distinct from a search that genuinely came back with nothing.
                    searchFailed = true;
                    searchFailureReason = ex.Message;
                    return Array.Empty<(ComicVineVolumeSearchResult, double)>();
                }

                found = ApplyAdvancedFilters(found);
                return found
                    .Select(c => (Volume: c, Score: MatchScoreCalculator.Compute(
                        seriesName,
                        issue.EffectiveFormat(),
                        effectiveNumber,
                        effectiveYear,
                        c,
                        _matchMemory.WasChosen(searchKey, c.Id),
                        DateTime.UtcNow.Year)))
                    .OrderByDescending(c => c.Score)
                    .ToList();
            }

            // Loops on "Go Back" from the issue dialog (design doc §3) - re-shows the series dialog
            // for this same book rather than a literal recursive call, so an arbitrary number of
            // back-and-forths between the two dialogs never grows the call stack.
            while (true)
            {
                ComicVineVolumeSearchResult? chosen;
                if (phase == ScrapePhase.Reviewing)
                {
                    // A deferred book (scrapeengine.py: delayed_b forces autoscrape_b=False
                    // unconditionally, verified) - never retries the automatic matcher a second time
                    // for a book that already failed it once; goes straight to the same always-
                    // interactive path AutoChooseTopMatch-off already uses. interactiveReview is
                    // guaranteed non-null here: this phase only ever runs when twoPhaseAutoChoose was
                    // true, which itself required it.
                    var ranked = await SearchAndRank(seriesName, cancellationToken).ConfigureAwait(false);
                    try
                    {
                        chosen = await interactiveReview!(bookLabel, seriesName, ranked, SearchAndRank, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        Record(ScrapeOutcomeKind.SkippedByUser, "Review dialog closed");
                        return false;
                    }
                }
                else if (_settings.AutoChooseTopMatch)
                {
                    // Only ever true during the AutoMatching phase of a two-phase run - a single-phase
                    // auto-choose run (non-interactive, or no reviewer given) still needs its old
                    // immediate-skip behavior below, not a defer nothing will ever resolve.
                    bool allowDefer = phase == ScrapePhase.AutoMatching;

                    var ranked = await SearchAndRank(seriesName, cancellationToken).ConfigureAwait(false);
                    if (ranked.Count == 0)
                    {
                        if (searchFailed)
                        {
                            if (allowDefer)
                            {
                                return true;
                            }

                            Record(ScrapeOutcomeKind.Failed, searchFailureReason);
                            return false;
                        }

                        if (allowDefer)
                        {
                            return true;
                        }

                        // CE's real fallback for a single-phase run (scrapeengine.py:262-266,
                        // verified): "no series could be found using the current (automatic or manual)
                        // search terms...force the user to choose the search terms" - shown only when
                        // there's no deferred second pass to catch it instead. The dialog already
                        // handles an empty candidate list on its own (SearchStatus = "No matches
                        // found - try a different search.").
                        if (isInteractive && interactiveReview is not null)
                        {
                            try
                            {
                                chosen = await interactiveReview(bookLabel, seriesName, ranked, SearchAndRank, cancellationToken).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                            {
                                Record(ScrapeOutcomeKind.SkippedByUser, "Review dialog closed");
                                return false;
                            }
                        }
                        else
                        {
                            Record(ScrapeOutcomeKind.NoMatchFound, "No search results");
                            return false;
                        }
                    }
                    else
                    {
                        // Cover-hash safety gate (docs/superpowers/specs/2026-09-24-comicvine-scraper-
                        // fidelity-design.md §2.1) - CE never trusts text-based match score alone for
                        // unattended auto-choose; it independently confirms the book's own cover
                        // perceptually matches the candidate's before applying without a modal.
                        // Disabled entirely (old behavior: trust the top score) when no getCoverPath
                        // delegate was given - opted in per-call, not a breaking default.
                        bool passedCoverGate = _getCoverPath is null
                            || await PassesCoverHashGateAsync(issue.Id, effectiveNumber, ranked, cancellationToken).ConfigureAwait(false);

                        if (passedCoverGate)
                        {
                            chosen = ranked[0].Volume;
                        }
                        else if (allowDefer)
                        {
                            return true;
                        }
                        else if (isInteractive && interactiveReview is not null)
                        {
                            // Fall through exactly as if AutoChooseTopMatch were off for this one book -
                            // the gate failing doesn't mean "no match," it means "don't trust this one
                            // without a human looking at it."
                            try
                            {
                                chosen = await interactiveReview(bookLabel, seriesName, ranked, SearchAndRank, cancellationToken).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                            {
                                Record(ScrapeOutcomeKind.SkippedByUser, "Review dialog closed"); // the review dialog was dismissed (scrim/window close), same as an explicit Skip
                                return false;
                            }
                        }
                        else
                        {
                            // Non-interactive (or no review callback): skip-and-log, same headless-
                            // automation gate every other low-confidence path in this method already
                            // uses. A candidate existed but nothing could confirm it and no human was
                            // there to ask - counted as NoMatchFound (docs/superpowers/specs/2026-09-24-
                            // comicvine-scraper-fidelity-design.md §4.1): nothing was written either
                            // way, and this isn't a thrown failure or an actual human decision.
                            Record(ScrapeOutcomeKind.NoMatchFound, "Couldn't confirm the cover match");
                            return false;
                        }
                    }
                }
                else if (isInteractive && interactiveReview is not null)
                {
                    var ranked = await SearchAndRank(seriesName, cancellationToken).ConfigureAwait(false);
                    try
                    {
                        chosen = await interactiveReview(bookLabel, seriesName, ranked, SearchAndRank, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // NativePluginModalHostViewModel.Dismiss() (scrim/window close) cancels only
                        // this book's review task, not the whole scrape - cancellationToken itself
                        // wasn't requested. Skip this one book, same as picking "none" from the
                        // dialog, instead of letting the TaskCanceledException escape and abort the
                        // entire batch.
                        Record(ScrapeOutcomeKind.SkippedByUser, "Review dialog closed");
                        return false;
                    }
                }
                else
                {
                    // Auto-choose off, unattended (or no review callback given): CE parity - an
                    // unattended run with auto-choose off skips everything unconditionally, no search
                    // even attempted. NoMatchFound for the same reason as the gate-declined case above.
                    Record(ScrapeOutcomeKind.NoMatchFound, "Not searched (unattended, confirmation required)");
                    return false;
                }

                if (chosen is null)
                {
                    // Distinguishes a plain Skip from a Ctrl-held permanent skip (Step 16) - the
                    // orchestrator has no direct signal for which one just happened (the marker write
                    // happens on a side channel, ScrapeCoordinator.MarkPermanentlySkipped, called
                    // synchronously before _resolve(null) fires), so it re-reads the flag it would
                    // just have written rather than threading a new signal through interactiveReview's
                    // return type.
                    bool wasPermanent;
                    using (var checkContext = createDbContext())
                    {
                        wasPermanent = checkContext.Issues.Where(i => i.Id == issue.Id).Select(i => i.ScrapePermanentlySkipped).FirstOrDefault();
                    }

                    Record(ScrapeOutcomeKind.SkippedByUser, wasPermanent ? "Skipped permanently" : "Skipped");
                    return false;
                }

                _matchMemory.RecordChoice(searchKey, chosen.Id);

                // Same gate CE's own two checkboxes express (design doc §3): the issue dialog only
                // ever shows when the series one could have too - non-interactive and auto-choose-on
                // runs never see either.
                bool showIssueDialog = isInteractive && !_settings.AutoChooseTopMatch
                    && _settings.ConfirmIssueMatch && interactiveIssueReview is not null;
                if (!showIssueDialog)
                {
                    await ApplyAsync(issue, chosen, effectiveNumber, createDbContext, cancellationToken).ConfigureAwait(false);
                    Record(ScrapeOutcomeKind.Applied, $"Matched \"{chosen.Name}\"");
                    return false;
                }

                (IReadOnlyList<ComicVineIssueSummary> allIssues, ComicVineIssueSummary? autoMatched) =
                    await LoadIssueSummariesAsync(chosen.Id, effectiveNumber, cancellationToken).ConfigureAwait(false);
                if (allIssues.Count == 0)
                {
                    // No issues to choose from (a fetch failure or a genuinely empty volume) - fall
                    // back to the same silent volume-only apply a non-interactive run would use,
                    // rather than showing a dialog with nothing in it.
                    await ApplyVolumeAndIssueAsync(issue, chosen, details: null, createDbContext, cancellationToken).ConfigureAwait(false);
                    Record(ScrapeOutcomeKind.Applied, $"Matched \"{chosen.Name}\"");
                    return false;
                }

                ComicVineIssueReviewResult issueResult;
                try
                {
                    issueResult = await interactiveIssueReview!(bookLabel, chosen, allIssues, autoMatched, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    Record(ScrapeOutcomeKind.SkippedByUser, "Issue dialog closed");
                    return false;
                }

                switch (issueResult.Outcome)
                {
                    case ComicVineIssueReviewOutcome.WentBack:
                        continue; // re-show the series dialog for this same book

                    case ComicVineIssueReviewOutcome.Skipped:
                        await ApplyVolumeAndIssueAsync(issue, chosen, details: null, createDbContext, cancellationToken).ConfigureAwait(false);
                        Record(ScrapeOutcomeKind.Applied, $"Matched \"{chosen.Name}\" (issue not confirmed)");
                        return false;

                    case ComicVineIssueReviewOutcome.Confirmed:
                        ComicVineIssueDetails? details = await SafeGetIssueDetailsAsync(issueResult.Issue!.Id, cancellationToken).ConfigureAwait(false);
                        await ApplyVolumeAndIssueAsync(issue, chosen, details, createDbContext, cancellationToken).ConfigureAwait(false);
                        Record(ScrapeOutcomeKind.Applied, $"Matched \"{chosen.Name}\" #{issueResult.Issue!.IssueNumber}");
                        return false;

                    default:
                        throw new InvalidOperationException($"Unexpected {nameof(ComicVineIssueReviewOutcome)}: {issueResult.Outcome}");
                }
            }
        }
    }

    /// <summary>
    /// CE's real <c>automatcher.py</c> safety gate (docs/superpowers/specs/2026-09-24-comicvine-
    /// scraper-fidelity-design.md §2.1, verified directly against source): a book's own cover must
    /// perceptually match the top candidate's above 0.87 similarity, AND the top candidate's cover
    /// must not also be suspiciously similar (&gt;0.77) to the 2nd/3rd-ranked candidate's - CE's guard
    /// against a TPB-vs-issue-#1 or same-named-reboot collision. Any cover that can't be hashed
    /// (missing file, network failure, undecodable image) fails the gate rather than being skipped -
    /// this method is only ever called when the caller has opted in via a non-null <c>_getCoverPath</c>,
    /// so "can't confirm" must mean "don't trust it," not "check was unavailable."
    /// </summary>
    private async Task<bool> PassesCoverHashGateAsync(int issueId, string? bookNumber, IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)> ranked, CancellationToken cancellationToken)
    {
        // Rebuilt 2026-09-25 to match automatcher.py's real find_series_ref (verified). The old gate
        // hashed the volume's generic series art, which is nearly always issue #1's cover, so for any
        // other issue it could never match - almost every book failed and was deferred to review - and
        // it ran its runner-up check on every book, comparing the book's own cover instead of the top
        // series' art against the runner-ups.
        //
        // 1. Trade-paperback-vs-issue-#1 guard: only for a first or unnumbered book, the top series' art
        //    must not look too much like the 2nd (or, failing that, 3rd) series' art (> 0.77, i.e. the
        //    0.87 threshold minus CE's 0.10 margin) - if it does, a TPB and a regular issue are
        //    indistinguishable here and CE bails rather than guess.
        if (IsFirstIssueOrUnnumbered(bookNumber) && ranked.Count >= 2)
        {
            ulong? primary = await HashRemoteCoverAsync(ranked[0].Volume.ImageUrl, cancellationToken).ConfigureAwait(false);
            ulong? secondary = await HashRemoteCoverAsync(ranked[1].Volume.ImageUrl, cancellationToken).ConfigureAwait(false);
            if (primary is not null && secondary is not null && CoverPerceptualHash.Similarity(primary.Value, secondary.Value) > 0.77)
            {
                return false;
            }

            if (ranked.Count >= 3)
            {
                ulong? tertiary = await HashRemoteCoverAsync(ranked[2].Volume.ImageUrl, cancellationToken).ConfigureAwait(false);
                if (primary is not null && tertiary is not null && CoverPerceptualHash.Similarity(primary.Value, tertiary.Value) > 0.77)
                {
                    return false;
                }
            }
        }

        // 2. No hashable local cover means nothing to confirm against: fail, don't skip the check.
        ulong? localHash = CoverPerceptualHash.HashFromFile(_getCoverPath!(issueId));
        if (localHash is null)
        {
            return false;
        }

        // 3. Compare against this book's own issue cover (found by its number), falling back to the
        //    series art only when there's no number or no such issue - CE's
        //    "ref = query_issue_ref(...) if issue_num else series_ref; ref = series_ref if not ref".
        ComicVineVolumeSearchResult top = ranked[0].Volume;
        ComicVineIssueSummary? matchedIssue = null;
        if (!string.IsNullOrWhiteSpace(bookNumber))
        {
            try
            {
                matchedIssue = await FindIssueSummaryAsync(top.Id, bookNumber, cancellationToken).ConfigureAwait(false);
            }
            catch (ComicVineException)
            {
                // A failed lookup here just means falling back to the series art, as "no such issue" does.
            }
        }

        ulong? remoteHash = await HashRemoteCoverAsync(matchedIssue is null ? top.ImageUrl : matchedIssue.ImageUrl, cancellationToken).ConfigureAwait(false);
        return remoteHash is not null && CoverPerceptualHash.Similarity(localHash.Value, remoteHash.Value) > 0.87;
    }

    /// <summary>CE's <c>is_first_issue</c>: no number at all, or a number equal to 1.</summary>
    private static bool IsFirstIssueOrUnnumbered(string? bookNumber) =>
        string.IsNullOrWhiteSpace(bookNumber)
        || (double.TryParse(bookNumber.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n) && n == 1.0);

    private static async Task<ulong?> HashRemoteCoverAsync(string? imageUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        try
        {
            byte[] bytes = await CoverHttp.GetByteArrayAsync(imageUrl, cancellationToken).ConfigureAwait(false);
            return CoverPerceptualHash.HashFromBytes(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // A cover fetch failing shouldn't abort the batch - same per-item resilience principle as
            // the rest of this orchestrator. Treated as "can't confirm," not "check unavailable."
            return null;
        }
    }

    /// <summary>CE's advanced-settings `IGNORE_BEFORE_YEAR`/`IGNORE_AFTER_YEAR`/`MAX_SEARCH_RESULTS`
    /// (design doc §12, deferred out of the first pass, now added). Year filters drop volumes outside
    /// the cutoff(s) before they're ever scored, matching `dbutils.filter_series_refs`'s real "unknown
    /// year is never excluded" rule (verified) for each bound independently; the result-count cap
    /// applies after that, matching "search results" - order matters, since capping first could drop a
    /// valid in-range volume to make room for one the year filters would have removed anyway.</summary>
    private IReadOnlyList<ComicVineVolumeSearchResult> ApplyAdvancedFilters(IReadOnlyList<ComicVineVolumeSearchResult> candidates)
    {
        IEnumerable<ComicVineVolumeSearchResult> filtered = candidates.Where(PassesYearAndPublisherFilters);

        if (_settings.MaxSearchResults is int max && max > 0)
        {
            filtered = filtered.Take(max);
        }

        return filtered.ToList();
    }

    /// <summary>Ported verbatim from CE's real <c>dbutils.filter_series_refs</c> (verified) - a
    /// candidate whose <see cref="ComicVineVolumeSearchResult.CountOfIssues"/> meets
    /// <see cref="ScrapeSettings.NeverIgnoreThreshold"/> bypasses BOTH the year filters AND the
    /// publisher filter together for that one candidate (CE's own combined <c>and</c>, not two
    /// independent checks) - a long-running series is never excluded just because it started early or
    /// its publisher happens to be on the ignore list.</summary>
    private bool PassesYearAndPublisherFilters(ComicVineVolumeSearchResult candidate)
    {
        if (_settings.NeverIgnoreThreshold is int threshold && candidate.CountOfIssues is int count && count >= threshold)
        {
            return true;
        }

        bool yearPasses = !int.TryParse(candidate.StartYear, out int year)
            || ((_settings.IgnoreVolumesBeforeYear is not int minYear || year >= minYear)
                && (_settings.IgnoreVolumesAfterYear is not int maxYear || year <= maxYear));

        // Trim BOTH sides - a stored ignore-list entry is kept exactly as the user typed it (see
        // ScrapeSettings.IgnoredPublishers's own doc comment), so only trimming the candidate's own
        // publisher string here would silently fail to match an entry saved with incidental whitespace.
        bool publisherPasses = _settings.IgnoredPublishers.Count == 0
            || candidate.Publisher is null
            || !_settings.IgnoredPublishers.Any(p => string.Equals(p.Trim(), candidate.Publisher.Trim(), StringComparison.OrdinalIgnoreCase));

        return yearPasses && publisherPasses;
    }

    /// <summary>Ported from CE's real <c>db.query_series_refs</c> (verified) - strips each ignored term
    /// (whole-word, case-insensitive) out of the search query text sent to ComicVine, rather than
    /// filtering results after the fact like <see cref="PassesYearAndPublisherFilters"/> does. Only a
    /// plain alphanumeric ignored-term entry is ever applied, matching CE's own `term.isalnum()` guard
    /// - a non-alphanumeric entry is silently skipped at use time rather than rejected at input time.</summary>
    private string StripIgnoredSearchTerms(string query)
    {
        List<string> terms = _settings.IgnoredSearchTerms
            .Where(t => t.Length > 0 && t.All(char.IsLetterOrDigit))
            .Select(System.Text.RegularExpressions.Regex.Escape)
            .ToList();

        if (terms.Count == 0)
        {
            return query;
        }

        string pattern = $@"\b(?:{string.Join('|', terms)})\b";
        return System.Text.RegularExpressions.Regex.Replace(query, pattern, string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>Legacy silent path (pre-dates the issue-review dialog): looks up the issue by number
    /// exactly as before and applies it with no confirmation step. Used whenever the new interactive
    /// issue dialog isn't in play - non-interactive runs, auto-choose-on runs, and interactive runs
    /// with <see cref="ScrapeSettings.ConfirmIssueMatch"/> off. <paramref name="bookNumber"/> is the
    /// caller's already-resolved effective number (issue.Number, or a filename-parsed Pending proposal's
    /// raw value) - not recomputed via issue.EffectiveNumber() here, since that ignores a Pending
    /// proposal and would silently never find a per-issue match for any book whose number hasn't been
    /// manually accepted yet.</summary>
    private async Task ApplyAsync(Issue issue, ComicVineVolumeSearchResult volume, string? bookNumber, Func<PaperbunkrDbContext> createDbContext, CancellationToken cancellationToken)
    {
        ComicVineIssueDetails? details = await FindIssueDetailsAsync(volume.Id, bookNumber, cancellationToken).ConfigureAwait(false);
        await ApplyVolumeAndIssueAsync(issue, volume, details, createDbContext, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The actual DB write (docs/superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-
    /// design.md §3) - volume-level fields always, per-issue fields only when <paramref name="details"/>
    /// is non-null. Shared by the legacy silent path (<see cref="ApplyAsync"/>) and the new interactive
    /// issue-review path, so both apply identically once the issue (or "no issue") is decided.</summary>
    private async Task ApplyVolumeAndIssueAsync(Issue issue, ComicVineVolumeSearchResult volume, ComicVineIssueDetails? details, Func<PaperbunkrDbContext> createDbContext, CancellationToken cancellationToken)
    {
        using PaperbunkrDbContext context = createDbContext();
        Issue? tracked = await context.Issues.Include(i => i.Series)
            .FirstOrDefaultAsync(i => i.Id == issue.Id, cancellationToken).ConfigureAwait(false);
        if (tracked is null)
        {
            return;
        }

        // Imprint resolution (docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md §1.1)
        // - fixed a real bug found by comparing against CE's actual source (comicbook.py:444-493): CV's
        // own "publisher" field for an imprint-published book is often literally the imprint's own name
        // (e.g. "Vertigo"), but CE only ever writes Imprint when that raw string is actually a recognized
        // imprint (via ComicVineImprints' 72-entry table or a user override) - otherwise Publisher gets
        // the raw value unchanged and Imprint is left alone. The previous version here unconditionally
        // wrote Imprint = volume.Publisher on every single scrape, so a plain non-imprint book (e.g.
        // "Marvel") ended up with Publisher="Marvel" AND Imprint="Marvel" every time - CE never does that.
        // ConvertImprints=false reproduces CE's real off-behavior (comicbook.py:464-466, verified) -
        // not "do nothing": the raw imprint name lands straight in Publisher, Imprint stays untouched.
        // That's exactly the "not a recognized imprint" branch below, so turning conversion off just
        // means resolution is skipped rather than attempted.
        string? resolved = !_settings.ConvertImprints || volume.Publisher is null
            ? volume.Publisher
            : ComicVineImprints.FindParentPublisher(volume.Publisher, _settings.ImprintOverrides);
        bool wasRecognizedImprint = _settings.ConvertImprints && volume.Publisher is not null && resolved != volume.Publisher;

        // CE's PUBLISHER_ALIAS setting (publisher_aliases_sm) applies to both the resolved publisher
        // and the imprint string, after resolution - distinct from ImprintOverrides above.
        string? Alias(string? value) => value is not null && _settings.PublisherAliases.TryGetValue(value, out var alias) ? alias : value;

        if (wasRecognizedImprint)
        {
            string? aliasedPublisher = Alias(resolved);
            string? aliasedImprint = Alias(volume.Publisher);

            if (ShouldWrite(ScrapeField.Publisher, tracked.Publisher, aliasedPublisher))
            {
                tracked.Publisher = aliasedPublisher;
            }

            if (ShouldWrite(ScrapeField.Imprint, tracked.Imprint, aliasedImprint))
            {
                tracked.Imprint = aliasedImprint;
            }
        }
        else
        {
            string? aliasedPublisher = Alias(resolved);
            if (ShouldWrite(ScrapeField.Publisher, tracked.Publisher, aliasedPublisher))
            {
                tracked.Publisher = aliasedPublisher;
            }
        }

        // The comic's Volume is the series' start year, as in the original ComicVine Scraper (its "volume_year_n": "Volume (start year) of this book", ComicVine having no
        // volume numbers). It is never the source's volume id - an earlier port wrote that here, which showed up as a random five-digit "volume". Left alone when the year is unknown.
        if (int.TryParse(volume.StartYear, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int startYear) && startYear > 0)
        {
            string year = startYear.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (ShouldWrite(ScrapeField.Volume, tracked.Volume, year))
            {
                tracked.Volume = year;
            }
        }

        // "Series" here is Paperbunkr's real relational Series.Name, not a per-issue flat string like
        // CE's ComicInfo <Series> field - every issue shares one Series row, so this corrects the
        // canonical series name (e.g. CV's exact capitalization/title) for the whole series at once,
        // which is strictly better than CE's per-book-redundant-string approach for the same field.
        if (tracked.Series is not null && ShouldWrite(ScrapeField.Series, tracked.Series.Name, volume.Name))
        {
            tracked.Series.Name = volume.Name;
        }

        // Count (2026-09-25, approved by the user - CE's own scraper never wrote it): the volume's
        // ComicVine issue count. A zero means the source didn't know, so it never blanks or zeroes a value.
        if (volume.CountOfIssues is int volumeCount && volumeCount > 0
            && ShouldWrite(ScrapeField.Count, tracked.Count.HasValue, true))
        {
            tracked.Count = volumeCount;
        }

        if (details is not null)
        {
            StoryArcPositions? arcPositions = null;
            if (_settings.EnabledScrapeFields.Contains(ScrapeField.StoryArcOrder) && details.StoryArcs.Count > 0)
            {
                arcPositions = await ArcOrder.ComputeAsync(details.StoryArcs, details.Id, cancellationToken).ConfigureAwait(false);
            }

            ApplyIssueDetails(tracked, details, arcPositions);
        }

        tracked.MetadataSource = Provider;      // the chosen match came from whichever source the run is on now (the dialog may have switched it)

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Keeps the derived Character/Team/Location/Creator/Publisher index current with every scrape
        // (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md) - see the matching note
        // in ScrapeByIdService.ScrapeAsync. Runs unconditionally, not just when `details is not null`,
        // since Publisher/Imprint are set above from volume-level data even without issue details.
        CharacterResolver.SyncFromIssue(context, tracked.Id);
        TeamResolver.SyncFromIssue(context, tracked.Id);
        LocationResolver.SyncFromIssue(context, tracked.Id);
        CreatorResolver.SyncFromIssue(context, tracked.Id);
        PublisherResolver.SyncIssue(context, tracked.Id);

        // Persisted provider identity (docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-
        // design.md §2.7) - CE writes a durable comicvine_issue/comicvine_volume link on every scraped
        // book (pluginbookdata.py:23-24, verified); Paperbunkr had no equivalent, so "already scraped"
        // was only ever inferred heuristically. Series identity is always known (the chosen volume);
        // issue identity only when a specific issue was actually matched (details is not null).
        ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Series, Provider, tracked.SeriesId, volume.Id);
        if (details is not null)
        {
            ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Issue, Provider, tracked.Id, details.Id);
            ComicMetadataExternalIdSync.SyncFromIssueDetails(context, tracked.Id, Provider, details);
            ContinuityMetronMatchResolver.SyncFromIssueDetails(context, tracked.SeriesId, Provider, details.Universes);
            IssueVariantCoverSync.SyncFromIssueDetails(context, tracked.Id, details.Variants);

            // Metron carries the Grand Comics Database id on every issue (docs/superpowers/specs/2026-09-27-gcd-data-design.md §3).
            if (details.GcdId is int gcdIssueId)
            {
                tracked.GcdIssueId = gcdIssueId;
            }
        }
    }

    /// <summary>Applies every per-issue field ComicVine carries, through the shared applier (the same one scrape-on-import uses) under this run's field toggles.</summary>
    private void ApplyIssueDetails(Issue tracked, ComicVineIssueDetails details, StoryArcPositions? arcPositions) =>
        IssueDetailsApplier.Apply(tracked, details, _settings.ToPolicy(), arcPositions);

    private StoryArcOrderResolver? _arcOrder;

    /// <summary>One resolver per orchestrator so each arc's issue list is fetched once per run, not once per book; dropped on a provider switch since arc ids are per-source.</summary>
    private StoryArcOrderResolver ArcOrder => _arcOrder ??= new StoryArcOrderResolver(
        (arcId, ct) => _comicVine.GetStoryArcIssuesAsync(arcId, ct),
        Path.Combine(AppDataPaths.Root, "arc_overrides.json"));

    /// <summary>Pages through <see cref="IScrapeComicVine.SearchIssuesAsync"/> for <paramref name="volumeId"/>
    /// looking for the one issue whose <c>issue_number</c> matches <paramref name="bookNumber"/> (a
    /// single-issue volume - a one-shot or TPB - matches regardless of number), then fetches its full
    /// detail record. Returns null on no match, an empty volume, or any <see cref="ComicVineException"/>
    /// along the way - same per-item resilience as the rest of this orchestrator, not a batch-aborting
    /// failure.</summary>
    private async Task<ComicVineIssueDetails?> FindIssueDetailsAsync(int volumeId, string? bookNumber, CancellationToken cancellationToken)
    {
        try
        {
            ComicVineIssueSummary? summary = await FindIssueSummaryAsync(volumeId, bookNumber, cancellationToken).ConfigureAwait(false);
            return summary is null
                ? null
                : await _comicVine.GetIssueDetailsAsync(summary.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (ComicVineException)
        {
            return null;
        }
    }

    // Per (volume, book number), including "no match" - the cover gate and the apply step both need the
    // same lookup for the same book, and a whole-series scrape asks it once per issue; without this each
    // book paid for the volume's issue list twice.
    private readonly Dictionary<(int VolumeId, string Number), ComicVineIssueSummary?> _issueSummaryCache = new();

    /// <summary>The volume's own issue matching <paramref name="bookNumber"/> (or its only issue, for a
    /// one-shot/TPB), paging <see cref="IScrapeComicVine.SearchIssuesAsync"/> and stopping at the first
    /// number match. Throws <see cref="ComicVineException"/> unchanged so each caller keeps its own
    /// resilience rule; a failure is never cached.</summary>
    private async Task<ComicVineIssueSummary?> FindIssueSummaryAsync(int volumeId, string? bookNumber, CancellationToken cancellationToken)
    {
        var key = (volumeId, bookNumber?.Trim() ?? string.Empty);
        if (_issueSummaryCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        const int maxPages = 20; // generous cap against a runaway loop on an unexpectedly huge omnibus volume
        var allSummaries = new List<ComicVineIssueSummary>();
        ComicVineIssueSummary? result = null;

        for (int page = 1; page <= maxPages; page++)
        {
            IReadOnlyList<ComicVineIssueSummary> batch = await _comicVine.SearchIssuesAsync(volumeId, page, cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            allSummaries.AddRange(batch);

            // Exit as soon as a number match turns up - most volumes need only their first page,
            // and there's no reason to keep paging (or keep throttling on more requests) once the
            // one issue this book actually is has been found.
            result = FindByNumber(batch, bookNumber);
            if (result is not null)
            {
                break;
            }
        }

        // No number match anywhere in the volume - the only remaining case worth matching on is a
        // volume with exactly one issue total (a one-shot or a single-book TPB), which is the book
        // regardless of what its own number field says. This can only be decided once pagination is
        // actually exhausted, not from any single page's count.
        if (result is null && allSummaries.Count == 1)
        {
            result = allSummaries[0];
        }

        _issueSummaryCache[key] = result;
        return result;
    }

    private static ComicVineIssueSummary? FindByNumber(IReadOnlyList<ComicVineIssueSummary> summaries, string? bookNumber)
    {
        if (string.IsNullOrWhiteSpace(bookNumber))
        {
            return null;
        }

        string normalized = bookNumber.Trim();
        ComicVineIssueSummary? exact = summaries.FirstOrDefault(s => string.Equals(s.IssueNumber?.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // Numeric fallback: CV often stores "1" while the book's own number is "01", or vice versa.
        if (double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double bookValue))
        {
            return summaries.FirstOrDefault(s =>
                double.TryParse(s.IssueNumber?.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double candidateValue)
                && candidateValue == bookValue);
        }

        return null;
    }

    /// <summary>Public entry point for the series dialog's "Show issues" peek (docs/superpowers/specs/
    /// 2026-09-13-cluster-scraper-ui-redesign-design.md §2) - just the issue list, no auto-match
    /// needed since the peek is read-only and doesn't pre-select anything meaningfully different from
    /// what the real issue dialog already would.</summary>
    public async Task<IReadOnlyList<ComicVineIssueSummary>> LoadIssuesAsync(int volumeId, CancellationToken cancellationToken = default) =>
        (await LoadIssueSummariesAsync(volumeId, bookNumber: null, cancellationToken).ConfigureAwait(false)).Summaries;

    /// <summary>The series match-review dialog's cover pane when <see cref="ScrapeSettings.ForceSeriesArt"/>
    /// is off (CE's real off-behavior, verified directly against <c>seriesform.py</c>: the panel shows
    /// that one issue's own cover, found by the book's own number, instead of the volume's generic
    /// image). Returns null when the volume's issue list can't be loaded or has no matching issue - the
    /// caller falls back to the volume's own art in that case, same as force-series-art being on.</summary>
    public async Task<string?> FindIssueCoverUrlAsync(int volumeId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ComicVineIssueSummary> summaries = await LoadIssuesAsync(volumeId, cancellationToken).ConfigureAwait(false);
        return FindByNumber(summaries, CurrentBookNumber)?.ImageUrl;
    }

    /// <summary>
    /// Full-list counterpart to <see cref="FindIssueDetailsAsync"/>, for the interactive issue-review
    /// dialog (docs/superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-design.md §3): pages
    /// through every page (rather than exiting the moment a number match is found) since the dialog
    /// needs the complete issue list to show, not just however much of it happened to load before a
    /// match turned up. Returns the full list plus whichever issue <see cref="FindByNumber"/> (or the
    /// single-issue-volume fallback) would auto-match - empty list and null on any fetch failure, same
    /// per-item resilience as the rest of this orchestrator.
    ///
    /// De-duplicates by issue id and stops as soon as a page contributes nothing new, rather than
    /// trusting an empty batch as the only end-of-results signal (confirmed live: ComicVine's real API
    /// does not reliably return an empty array once <c>page</c> goes past a small volume's last page -
    /// it can just repeat the final page's results instead, which produced a duplicated,
    /// endlessly-repeating candidate list in the issue dialog until this guard was added).
    /// </summary>
    private async Task<(IReadOnlyList<ComicVineIssueSummary> Summaries, ComicVineIssueSummary? AutoMatched)> LoadIssueSummariesAsync(
        int volumeId, string? bookNumber, CancellationToken cancellationToken)
    {
        const int maxPages = 20;
        var allSummaries = new List<ComicVineIssueSummary>();
        var seenIds = new HashSet<int>();

        for (int page = 1; page <= maxPages; page++)
        {
            IReadOnlyList<ComicVineIssueSummary> batch;
            try
            {
                batch = await _comicVine.SearchIssuesAsync(volumeId, page, cancellationToken).ConfigureAwait(false);
            }
            catch (ComicVineException)
            {
                // A later page failing shouldn't discard whatever earlier pages already returned -
                // same per-item resilience principle as the rest of this orchestrator, just applied
                // within one volume's own pagination instead of across books.
                break;
            }

            if (batch.Count == 0)
            {
                break;
            }

            bool sawNewIssue = false;
            foreach (ComicVineIssueSummary summary in batch)
            {
                if (seenIds.Add(summary.Id))
                {
                    allSummaries.Add(summary);
                    sawNewIssue = true;
                }
            }

            if (!sawNewIssue)
            {
                break; // this page repeated the previous one verbatim - end of real results
            }
        }

        ComicVineIssueSummary? autoMatched = FindByNumber(allSummaries, bookNumber)
            ?? (allSummaries.Count == 1 ? allSummaries[0] : null);
        return (allSummaries, autoMatched);
    }

    /// <summary>Fetches full details for an issue the user already confirmed (or the dialog auto-
    /// matched) - null on any <see cref="ComicVineException"/>, same resilience as everywhere else in
    /// this orchestrator: a failed detail fetch still leaves the volume-level fields applied.</summary>
    private async Task<ComicVineIssueDetails?> SafeGetIssueDetailsAsync(int issueId, CancellationToken cancellationToken)
    {
        try
        {
            return await _comicVine.GetIssueDetailsAsync(issueId, cancellationToken).ConfigureAwait(false);
        }
        catch (ComicVineException)
        {
            return null;
        }
    }

    /// <summary>CE's overwrite / ignore-blank gate plus the enabled-field toggle. Series is hardcoded
    /// to always ignore blanks regardless of <see cref="ScrapeSettings.IgnoreBlankValues"/> - CE
    /// exempts Series and Issue Number from that setting unconditionally (comicbook.py:259-261,
    /// "we ALWAYS ignore blanks for 'series'!", verified directly against source) so an empty CV
    /// response can never blank out either identity field even with the global setting off. Number's
    /// own exemption lives in <see cref="ScrapeFieldPolicy.ShouldWrite"/> instead, since it's applied
    /// through <see cref="IssueDetailsApplier"/>, not here.</summary>
    private bool ShouldWrite(ScrapeField field, bool hasCurrentValue, bool hasNewValue)
    {
        if (!_settings.EnabledScrapeFields.Contains(field))
        {
            return false;
        }

        bool ignoreBlankValues = field == ScrapeField.Series || _settings.IgnoreBlankValues;
        if (ignoreBlankValues && !hasNewValue)
        {
            return false;
        }

        return _settings.OverwriteExisting || !hasCurrentValue;
    }

    private bool ShouldWrite(ScrapeField field, string? currentValue, string? newValue) =>
        ShouldWrite(field, !string.IsNullOrEmpty(currentValue), !string.IsNullOrEmpty(newValue));

    /// <summary>Not <c>issue.EffectiveNumber()</c> - see <see cref="ResolveEffectiveNumberAndYearAsync"/>'s
    /// own doc comment for why that alone silently omits the number for almost every real book.
    /// <paramref name="number"/> should always be a value this method's own caller already resolved via
    /// <see cref="ResolveEffectiveNumberAndYearAsync"/>.</summary>
    private static string BookLabel(Issue issue, string? number) => $"{issue.Series?.Name} #{number}";

    /// <summary>Public one-off entry point for a caller that needs a single book's correctly-resolved
    /// label outside of a running <see cref="ScrapeAsync"/> batch - specifically
    /// <c>ScrapeCoordinator</c>'s Activity Center job title, built before the batch (and therefore this
    /// orchestrator's own per-book resolution) has started.</summary>
    public static async Task<string> ResolveBookLabelAsync(Issue issue, Func<PaperbunkrDbContext> createDbContext, CancellationToken cancellationToken = default)
    {
        (string? number, _) = await ResolveEffectiveNumberAndYearAsync(issue, createDbContext, cancellationToken).ConfigureAwait(false);
        return BookLabel(issue, number);
    }

    /// <summary>See the call site's own comment (docs/superpowers/specs/2026-09-24-comicvine-scraper-
    /// fidelity-design.md §2.8, and its 2026-09-24 extension) - resolves the book's real number/year
    /// for both match scoring and the actual per-issue FindByNumber lookup, via a direct DB read rather
    /// than <see cref="IssueMetadataExtensions.EffectiveNumber"/>/<see cref="IssueMetadataExtensions.EffectiveYear"/>.
    ///
    /// Deliberately NOT just <c>issue.EffectiveNumber()</c>: that resolver reads the already-loaded
    /// <c>issue.MetadataProposals</c> in-memory collection, which none of this orchestrator's real
    /// callers ever <c>.Include()</c> (<see cref="ScrapeCoordinator.ScrapeIssuesAsync"/> loads books via
    /// a bare <c>.Include(i => i.Series)</c>) - so it's always empty here, and <c>EffectiveNumber()</c>
    /// silently falls straight to null even when the DB has a perfectly good <c>Accepted</c> proposal.
    /// Confirmed live (2026-09-24): since <see cref="MetadataResolutionPolicy.Prompt"/> has no
    /// Preferences toggle to ever reach, <c>Automatic</c> is what every real library actually runs
    /// under, meaning a filename-parsed Number/Year is *always* Accepted immediately
    /// (<c>LibraryFolderScanner.AddFilenameProposal</c>) and never written to <c>Issue.Number</c>/
    /// <c>Issue.Year</c> directly - so this unloaded-collection gap, not a Pending-vs-Accepted policy
    /// difference, is why almost every freshly-scanned book's per-issue scrape fields never applied
    /// until its number was set directly (Issue Properties editor, which writes the field itself).
    /// Also still checks Pending (never resolves it) for the book still sitting in Needs Review.
    /// Read-only either way: never writes, never resolves a proposal.</summary>
    private static async Task<(string? Number, int? Year)> ResolveEffectiveNumberAndYearAsync(
        Issue issue, Func<PaperbunkrDbContext> createDbContext, CancellationToken cancellationToken)
    {
        if (issue.Number is not null && issue.Year is not null)
        {
            return (issue.Number, issue.Year);
        }

        using PaperbunkrDbContext context = createDbContext();
        var proposals = await context.MetadataProposals.AsNoTracking()
            .Where(p => p.IssueId == issue.Id
                && (p.Field == MetadataProposalField.Number || p.Field == MetadataProposalField.Year)
                && (p.Status == MetadataProposalStatus.Accepted || p.Status == MetadataProposalStatus.Pending))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        string? number = issue.Number ?? BestProposedValue(proposals, MetadataProposalField.Number);
        string? yearText = issue.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? BestProposedValue(proposals, MetadataProposalField.Year);
        int? year = yearText is not null && int.TryParse(yearText, out int parsedYear) ? parsedYear : null;

        return (number, year);
    }

    /// <summary>An Accepted proposal wins over a Pending one for the same field (mirrors
    /// <see cref="IssueMetadataExtensions.EffectiveNumber"/>'s own Accepted-only contract), falling back
    /// to Pending only when nothing has been accepted yet; <paramref name="proposals"/> is already
    /// ordered newest-first.</summary>
    private static string? BestProposedValue(List<MetadataProposal> proposals, MetadataProposalField field) =>
        proposals.FirstOrDefault(p => p.Field == field && p.Status == MetadataProposalStatus.Accepted)?.ProposedValue
        ?? proposals.FirstOrDefault(p => p.Field == field && p.Status == MetadataProposalStatus.Pending)?.ProposedValue;
}
