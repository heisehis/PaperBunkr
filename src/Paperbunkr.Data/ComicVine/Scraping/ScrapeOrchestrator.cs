
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
public sealed class ScrapeOrchestrator
{
    private IScrapeComicVine _comicVine;
    private ComicVineMatchMemory _matchMemory;
    private readonly ScrapeSettings _settings;
    private readonly Func<ComicProvider, (IScrapeComicVine Source, ComicVineMatchMemory Memory)?>? _providerSwitcher;

    public ScrapeOrchestrator(IScrapeComicVine comicVine, ComicVineMatchMemory matchMemory, ScrapeSettings settings,
        ComicProvider provider = ComicProvider.ComicVine, Func<ComicProvider, (IScrapeComicVine Source, ComicVineMatchMemory Memory)?>? providerSwitcher = null)
    {
        _comicVine = comicVine;
        _matchMemory = matchMemory;
        _settings = settings;
        Provider = provider;
        _providerSwitcher = providerSwitcher;
    }

    /// <summary>The source this run is currently searching and reading details from. Changes only through <see cref="TrySwitchProvider"/> (the review dialog's source switch).</summary>
    public ComicProvider Provider { get; private set; }

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
    public async Task<int> ScrapeAsync(
        IReadOnlyList<Issue> books,
        bool isInteractive,
        Func<string, string, IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>, SearchAndRankDelegate, CancellationToken, Task<ComicVineVolumeSearchResult?>>? interactiveReview,
        Func<PaperbunkrDbContext> createDbContext,
        CancellationToken cancellationToken = default,
        Action<int, int, Issue>? onProgress = null,
        InteractiveIssueReviewDelegate? interactiveIssueReview = null)
    {
        int applied = 0;
        int index = 0;
        foreach (Issue issue in books)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            onProgress?.Invoke(books.Count, index, issue);

            string? seriesName = issue.Series?.Name;
            if (string.IsNullOrWhiteSpace(seriesName))
            {
                continue;
            }

            // Keyed on the book's own series name regardless of what the user later retypes in the
            // dialog - priorscore's whole point is "this volume was previously chosen for a
            // similarly-named book", a per-series memory, not a per-search-string one.
            string searchKey = ComicVineMatchMemory.NormalizeSearchKey(seriesName);

            async Task<IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>> SearchAndRank(string query, CancellationToken ct)
            {
                IReadOnlyList<ComicVineVolumeSearchResult> found;
                try
                {
                    found = await _comicVine.SearchVolumesAsync(StripIgnoredSearchTerms(query), cancellationToken: ct).ConfigureAwait(false);
                }
                catch (ComicVineException)
                {
                    // A network/API failure for this one search shouldn't abort the batch - same
                    // per-item isolation principle as LibraryOrganizerService.ExecuteAsync. Returning
                    // empty (not throwing) also lets an interactive user just try again.
                    return Array.Empty<(ComicVineVolumeSearchResult, double)>();
                }

                found = ApplyAdvancedFilters(found);
                return found
                    .Select(c => (Volume: c, Score: MatchScoreCalculator.Compute(
                        seriesName,
                        issue.EffectiveFormat(),
                        ParseIssueNumber(issue.EffectiveNumber()),
                        issue.EffectiveYear(),
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
                if (_settings.AutoChooseTopMatch)
                {
                    var ranked = await SearchAndRank(seriesName, cancellationToken).ConfigureAwait(false);
                    if (ranked.Count == 0)
                    {
                        break;
                    }

                    chosen = ranked[0].Volume;
                }
                else if (isInteractive && interactiveReview is not null)
                {
                    var ranked = await SearchAndRank(seriesName, cancellationToken).ConfigureAwait(false);
                    try
                    {
                        chosen = await interactiveReview(BookLabel(issue), seriesName, ranked, SearchAndRank, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // NativePluginModalHostViewModel.Dismiss() (scrim/window close) cancels only
                        // this book's review task, not the whole scrape - cancellationToken itself
                        // wasn't requested. Skip this one book, same as picking "none" from the
                        // dialog, instead of letting the TaskCanceledException escape and abort the
                        // entire batch.
                        break;
                    }
                }
                else
                {
                    break;
                }

                if (chosen is null)
                {
                    break;
                }

                _matchMemory.RecordChoice(searchKey, chosen.Id);

                // Same gate CE's own two checkboxes express (design doc §3): the issue dialog only
                // ever shows when the series one could have too - non-interactive and auto-choose-on
                // runs never see either.
                bool showIssueDialog = isInteractive && !_settings.AutoChooseTopMatch
                    && _settings.ConfirmIssueMatch && interactiveIssueReview is not null;
                if (!showIssueDialog)
                {
                    await ApplyAsync(issue, chosen, createDbContext, cancellationToken).ConfigureAwait(false);
                    applied++;
                    break;
                }

                (IReadOnlyList<ComicVineIssueSummary> allIssues, ComicVineIssueSummary? autoMatched) =
                    await LoadIssueSummariesAsync(chosen.Id, issue.EffectiveNumber(), cancellationToken).ConfigureAwait(false);
                if (allIssues.Count == 0)
                {
                    // No issues to choose from (a fetch failure or a genuinely empty volume) - fall
                    // back to the same silent volume-only apply a non-interactive run would use,
                    // rather than showing a dialog with nothing in it.
                    await ApplyVolumeAndIssueAsync(issue, chosen, details: null, createDbContext, cancellationToken).ConfigureAwait(false);
                    applied++;
                    break;
                }

                ComicVineIssueReviewResult issueResult;
                try
                {
                    issueResult = await interactiveIssueReview!(BookLabel(issue), chosen, allIssues, autoMatched, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                switch (issueResult.Outcome)
                {
                    case ComicVineIssueReviewOutcome.WentBack:
                        continue; // re-show the series dialog for this same book

                    case ComicVineIssueReviewOutcome.Skipped:
                        await ApplyVolumeAndIssueAsync(issue, chosen, details: null, createDbContext, cancellationToken).ConfigureAwait(false);
                        applied++;
                        break;

                    case ComicVineIssueReviewOutcome.Confirmed:
                        ComicVineIssueDetails? details = await SafeGetIssueDetailsAsync(issueResult.Issue!.Id, cancellationToken).ConfigureAwait(false);
                        await ApplyVolumeAndIssueAsync(issue, chosen, details, createDbContext, cancellationToken).ConfigureAwait(false);
                        applied++;
                        break;
                }

                break;
            }
        }

        return applied;
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
    /// with <see cref="ScrapeSettings.ConfirmIssueMatch"/> off.</summary>
    private async Task ApplyAsync(Issue issue, ComicVineVolumeSearchResult volume, Func<PaperbunkrDbContext> createDbContext, CancellationToken cancellationToken)
    {
        ComicVineIssueDetails? details = await FindIssueDetailsAsync(volume.Id, issue.EffectiveNumber(), cancellationToken).ConfigureAwait(false);
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

        // Imprint resolution (design doc §3): CV's own "publisher" field for an imprint-published
        // book is often literally the imprint's own name (e.g. "Vertigo"), not its parent publisher -
        // ScrapeField.Publisher gets it resolved through the user's overrides then the static table
        // (same as CE); ScrapeField.Imprint gets CV's own raw, unresolved value, matching the design
        // doc §3 note that CV's "publisher" field often literally *is* the imprint name.
        string? resolvedPublisher = volume.Publisher is null
            ? null
            : ComicVineImprints.FindParentPublisher(volume.Publisher, _settings.ImprintOverrides);

        if (ShouldWrite(ScrapeField.Publisher, tracked.Publisher, resolvedPublisher))
        {
            tracked.Publisher = resolvedPublisher;
        }

        if (ShouldWrite(ScrapeField.Imprint, tracked.Imprint, volume.Publisher))
        {
            tracked.Imprint = volume.Publisher;
        }

        if (ShouldWrite(ScrapeField.Volume, tracked.Volume, volume.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            tracked.Volume = volume.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // "Series" here is Paperbunkr's real relational Series.Name, not a per-issue flat string like
        // CE's ComicInfo <Series> field - every issue shares one Series row, so this corrects the
        // canonical series name (e.g. CV's exact capitalization/title) for the whole series at once,
        // which is strictly better than CE's per-book-redundant-string approach for the same field.
        if (tracked.Series is not null && ShouldWrite(ScrapeField.Series, tracked.Series.Name, volume.Name))
        {
            tracked.Series.Name = volume.Name;
        }

        if (details is not null)
        {
            ApplyIssueDetails(tracked, details);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies every per-issue field ComicVine carries, through the shared applier (the same one scrape-on-import uses) under this run's field toggles.</summary>
    private void ApplyIssueDetails(Issue tracked, ComicVineIssueDetails details) => IssueDetailsApplier.Apply(tracked, details, _settings.ToPolicy());

    /// <summary>Pages through <see cref="IScrapeComicVine.SearchIssuesAsync"/> for <paramref name="volumeId"/>
    /// looking for the one issue whose <c>issue_number</c> matches <paramref name="bookNumber"/> (a
    /// single-issue volume - a one-shot or TPB - matches regardless of number), then fetches its full
    /// detail record. Returns null on no match, an empty volume, or any <see cref="ComicVineException"/>
    /// along the way - same per-item resilience as the rest of this orchestrator, not a batch-aborting
    /// failure.</summary>
    private async Task<ComicVineIssueDetails?> FindIssueDetailsAsync(int volumeId, string? bookNumber, CancellationToken cancellationToken)
    {
        const int maxPages = 20; // generous cap against a runaway loop on an unexpectedly huge omnibus volume
        var allSummaries = new List<ComicVineIssueSummary>();

        try
        {
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
                ComicVineIssueSummary? numberMatch = FindByNumber(batch, bookNumber);
                if (numberMatch is not null)
                {
                    return await _comicVine.GetIssueDetailsAsync(numberMatch.Id, cancellationToken).ConfigureAwait(false);
                }
            }

            // No number match anywhere in the volume - the only remaining case worth matching on is a
            // volume with exactly one issue total (a one-shot or a single-book TPB), which is the book
            // regardless of what its own number field says. This can only be decided once pagination is
            // actually exhausted, not from any single page's count.
            if (allSummaries.Count == 1)
            {
                return await _comicVine.GetIssueDetailsAsync(allSummaries[0].Id, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        catch (ComicVineException)
        {
            return null;
        }
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

    /// <summary>CE's overwrite / ignore-blank gate plus the enabled-field toggle.</summary>
    private bool ShouldWrite(ScrapeField field, bool hasCurrentValue, bool hasNewValue)
    {
        if (!_settings.EnabledScrapeFields.Contains(field))
        {
            return false;
        }

        if (_settings.IgnoreBlankValues && !hasNewValue)
        {
            return false;
        }

        return _settings.OverwriteExisting || !hasCurrentValue;
    }

    private bool ShouldWrite(ScrapeField field, string? currentValue, string? newValue) =>
        ShouldWrite(field, !string.IsNullOrEmpty(currentValue), !string.IsNullOrEmpty(newValue));

    private static string BookLabel(Issue issue) => $"{issue.Series?.Name} #{issue.EffectiveNumber()}";

    private static int? ParseIssueNumber(string? number) =>
        int.TryParse(number, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : null;
}
