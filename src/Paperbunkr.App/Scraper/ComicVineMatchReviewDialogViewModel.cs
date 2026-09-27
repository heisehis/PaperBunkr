using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;
using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Scraper;

/// <summary>One ranked candidate row (docs/superpowers/specs/2026-09-24-scraper-review-tables-and-
/// batch-summary-design.md §1.1 - a real sortable table, CE's own seriesform.py shape, replacing the
/// old "Best match"/"Other results" card-list split; CE's table has no such split either, just one
/// flat list defaulting to Score descending). Select-then-confirm, not click-to-resolve.</summary>
public sealed partial class ComicVineMatchCandidateViewModel : ObservableObject
{
    private readonly Action<ComicVineMatchCandidateViewModel> _select;

    public ComicVineVolumeSearchResult Volume { get; }
    public double Score { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string? CoverImageUrl => Volume.ImageUrl;

    public string IssueCountLabel => Volume.CountOfIssues switch
    {
        1 => "1 issue",
        int count => $"{count} issues",
        null => string.Empty,
    };

    public ComicVineMatchCandidateViewModel(ComicVineVolumeSearchResult volume, double score, Action<ComicVineMatchCandidateViewModel> select)
    {
        Volume = volume;
        Score = score;
        _select = select;
    }

    [RelayCommand]
    private void Select() => _select(this);
}

/// <summary>
/// Backs <see cref="ComicVineMatchReviewDialogView"/> (docs/superpowers/specs/2026-09-13-cluster-
/// scraper-ui-redesign-design.md §2 - a redesign of the original bare-list dialog from
/// docs/superpowers/specs/2026-09-11-cluster-library-manager-design.md §4, same mechanism as
/// <see cref="FileConflictDialogViewModel"/> otherwise: modal-per-book, shown only when auto-choose is
/// off and the run is interactive).
///
/// <see cref="SearchQuery"/> is editable and pre-filled with the series name rather than a fixed,
/// un-editable candidate list (CE's own real flow: the initial automatic guess can miss - different
/// punctuation, an alternate title, a reprint under a different name - and the user needs to be able
/// to retype and search again, including when the automatic pass found nothing at all).
///
/// Selection is now select-then-confirm, not click-to-resolve: clicking a candidate updates
/// <see cref="SelectedCandidate"/> (and the cover-preview pane the view binds to it), and a separate
/// <see cref="ConfirmCommand"/> actually resolves the dialog - CE parity, and required for the cover
/// preview to mean anything (there'd be nothing to preview if a click already committed).
/// </summary>
public sealed partial class ComicVineMatchReviewDialogViewModel : ObservableObject
{
    private readonly Action<ComicVineVolumeSearchResult?> _resolve;
    private readonly ScrapeOrchestrator.SearchAndRankDelegate _search;
    private readonly Func<int, Task<IReadOnlyList<ComicVineIssueSummary>>>? _loadIssues;
    private readonly Func<ComicProvider, bool>? _switchProvider;
    private readonly bool _forceSeriesArt;
    private readonly Func<int, CancellationToken, Task<string?>>? _findIssueCoverUrl;
    private readonly Action? _markPermanentlySkipped;
    private CancellationTokenSource? _coverLookupCts;
    private bool _revertingProvider;

    /// <summary>Docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md Phase 3 -
    /// hides the cover pane entirely (a real "scrape faster on a slow connection" toggle CE had).</summary>
    public bool ShowCovers { get; }

    /// <summary>Code-behind toggles this while Ctrl is held over the Skip button (CE's own visual
    /// affordance, <c>seriesform.py</c>, verified) - the view binds the Skip button's own label to it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkipLabel))]
    private bool _isCtrlHeldOverSkip;

    public string SkipLabel => IsCtrlHeldOverSkip ? "Skip this book (always)" : "Skip this book";

    /// <summary>The cover pane's actual image source - the selected candidate's volume art when
    /// <c>ForceSeriesArt</c> is on (or no lookup delegate was given), or that one issue's own cover
    /// (CE's real off-behavior, <c>seriesform.py</c>, verified) once an async lookup by the book's own
    /// number resolves. Starts each selection showing the volume art immediately (never a blank pane)
    /// and swaps to the issue-specific cover if/when the lookup finds one.</summary>
    [ObservableProperty]
    private string? _resolvedCoverImageUrl;

    public static IReadOnlyList<string> ProviderNames { get; } = ComicProviderFactory.All.Select(ComicProviderFactory.DisplayName).ToList();

    /// <summary>The source being searched ("ComicVine" or "Metron"). Changing it moves the rest of the run to that source and searches it again; it stays put when that source has no login saved.</summary>
    [ObservableProperty]
    private string _providerText = "ComicVine";

    /// <summary>False when the caller gave no way to switch (the choice is then just a label).</summary>
    public bool CanSwitchProvider => _switchProvider is not null;

    partial void OnProviderTextChanged(string oldValue, string newValue)
    {
        if (_revertingProvider || _switchProvider is null)
        {
            return;
        }

        var provider = ComicProviderFactory.Parse(newValue);
        if (!_switchProvider(provider))
        {
            _revertingProvider = true;
            ProviderText = oldValue;
            _revertingProvider = false;
            SearchStatus = ComicProviderFactory.MissingCredentialsMessage(provider);
            return;
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            _ = SearchCommand.ExecuteAsync(null);
        }
    }

    public string BookLabel { get; }

    /// <summary>Non-null while "Show issues" is showing its embedded peek panel in place of the
    /// normal candidate list/preview (see <see cref="ShowIssues"/>'s own doc comment for why this is
    /// an embedded toggle, not a second modal).</summary>
    [ObservableProperty]
    private ComicVineIssueReviewDialogViewModel? _issuePeek;

    [ObservableProperty]
    private string _searchQuery;

    [ObservableProperty]
    private bool _isSearching;

    /// <summary>Non-null only after a search that found zero candidates - distinguishes "haven't
    /// searched this exact query" from "searched, found nothing" for the user.</summary>
    [ObservableProperty]
    private string? _searchStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowIssuesCommand))]
    private ComicVineMatchCandidateViewModel? _selectedCandidate;

    public ObservableCollection<ComicVineMatchCandidateViewModel> Candidates { get; } = new();

    public ComicVineMatchReviewDialogViewModel(
        string bookLabel,
        string initialQuery,
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)> initialCandidates,
        ScrapeOrchestrator.SearchAndRankDelegate search,
        Action<ComicVineVolumeSearchResult?> resolve,
        Func<int, Task<IReadOnlyList<ComicVineIssueSummary>>>? loadIssues = null,
        ComicProvider provider = ComicProvider.ComicVine,
        Func<ComicProvider, bool>? switchProvider = null,
        bool forceSeriesArt = true,
        bool showCovers = true,
        Func<int, CancellationToken, Task<string?>>? findIssueCoverUrl = null,
        Action? markPermanentlySkipped = null)
    {
        BookLabel = bookLabel;
        _providerText = ComicProviderFactory.DisplayName(provider);
        _switchProvider = switchProvider;
        _searchQuery = initialQuery;
        _search = search;
        _resolve = resolve;
        _loadIssues = loadIssues;
        _forceSeriesArt = forceSeriesArt;
        ShowCovers = showCovers;
        _findIssueCoverUrl = findIssueCoverUrl;
        _markPermanentlySkipped = markPermanentlySkipped;
        SetCandidates(initialCandidates);
    }

    private void SetCandidates(IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)> ranked)
    {
        Candidates.Clear();
        SelectedCandidate = null;
        foreach (var candidate in ranked.OrderByDescending(c => c.Score))
        {
            Candidates.Add(new ComicVineMatchCandidateViewModel(candidate.Volume, candidate.Score, Select));
        }

        // Pre-select the top match - CE's own series-choose dialog highlights its top row by default
        // too (design doc §2), so confirming the common "the automatic guess is right" case is a
        // single click on Confirm, not select-then-Confirm.
        if (Candidates.Count > 0)
        {
            Select(Candidates[0]);
        }

        SearchStatus = ranked.Count == 0 ? "No matches found - try a different search." : null;
    }

    private void Select(ComicVineMatchCandidateViewModel candidate) => SelectedCandidate = candidate;

    /// <summary>CommunityToolkit's generated partial hook for <see cref="SelectedCandidate"/> - the
    /// single place selection side effects (row highlight sync, cover refresh) happen, regardless of
    /// whether the change came from <see cref="Select"/> (the row's own command) or directly from the
    /// DataGrid's own <c>SelectedItem</c> two-way binding (docs/superpowers/specs/2026-09-24-scraper-
    /// review-tables-and-batch-summary-design.md §1.1) when the user clicks a row.</summary>
    partial void OnSelectedCandidateChanged(ComicVineMatchCandidateViewModel? oldValue, ComicVineMatchCandidateViewModel? newValue)
    {
        foreach (ComicVineMatchCandidateViewModel c in Candidates)
        {
            c.IsSelected = ReferenceEquals(c, newValue);
        }

        if (newValue is not null)
        {
            _ = RefreshCoverAsync(newValue);
        }
        else
        {
            ResolvedCoverImageUrl = null;
        }
    }

    /// <summary>Resolves <see cref="ResolvedCoverImageUrl"/> for the just-selected candidate. Shows the
    /// volume art immediately (matching <c>ForceSeriesArt</c> on, and never leaving the pane blank
    /// while an off-mode lookup is in flight), then swaps to the specific issue's own cover if the
    /// async lookup finds one before a newer selection cancels it.</summary>
    private async Task RefreshCoverAsync(ComicVineMatchCandidateViewModel candidate)
    {
        _coverLookupCts?.Cancel();
        ResolvedCoverImageUrl = candidate.CoverImageUrl;

        if (_forceSeriesArt || _findIssueCoverUrl is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _coverLookupCts = cts;
        try
        {
            string? issueCoverUrl = await _findIssueCoverUrl(candidate.Volume.Id, cts.Token).ConfigureAwait(true);
            if (!cts.IsCancellationRequested)
            {
                ResolvedCoverImageUrl = issueCoverUrl ?? candidate.CoverImageUrl;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || IsSearching)
        {
            return;
        }

        IsSearching = true;
        SearchStatus = null;
        try
        {
            var results = await _search(SearchQuery, CancellationToken.None).ConfigureAwait(true);
            SetCandidates(results);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanConfirm() => SelectedCandidate is not null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => _resolve(SelectedCandidate!.Volume);

    /// <summary>Skipping resolves with null - the book is left unmatched, no ComicVine fields applied.</summary>
    [RelayCommand]
    private void Skip() => _resolve(null);

    /// <summary>Ctrl-held Skip (CE's real <c>book.skip_forever()</c>, <c>seriesform.py</c>, verified) -
    /// same outcome as a plain <see cref="Skip"/> for this run, plus a durable marker so every future
    /// scrape of this book, interactive or unattended, is silently excluded without asking again.</summary>
    [RelayCommand]
    private void SkipPermanently()
    {
        _markPermanentlySkipped?.Invoke();
        _resolve(null);
    }

    private bool CanShowIssues() => SelectedCandidate is not null && _loadIssues is not null;

    /// <summary>
    /// Shows the issue list for the currently-selected (not-yet-confirmed) candidate as an embedded
    /// panel within THIS dialog - relevant specifically when <c>ConfirmIssueMatch</c> is off, since
    /// that's otherwise the only chance to sanity-check the auto-matched issue before Confirm applies
    /// everything with no further confirmation step.
    ///
    /// Deliberately NOT a second <c>ShowModalAsync</c> call (the first shipped version was, and it
    /// crashed the app): the host's native-plugin modal host has exactly one hosted-content slot, so
    /// a nested call while this dialog is still current gets queued rather than shown, and cancelling
    /// it later (e.g. the user closes the whole overlay via its shared X) throws an unhandled
    /// <c>TaskCanceledException</c> back through this command's own awaited call - confirmed live,
    /// this is what actually happened. Swapping <see cref="IssuePeek"/> in place, inside the same
    /// hosted content, has no modal-stacking question to get wrong at all.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShowIssues))]
    private async Task ShowIssues()
    {
        IReadOnlyList<ComicVineIssueSummary> issues = await _loadIssues!(SelectedCandidate!.Volume.Id).ConfigureAwait(true);
        IssuePeek = new ComicVineIssueReviewDialogViewModel(
            BookLabel, issues, preSelected: null, readOnlyPeek: true, _ => IssuePeek = null, ShowCovers);
    }
}
