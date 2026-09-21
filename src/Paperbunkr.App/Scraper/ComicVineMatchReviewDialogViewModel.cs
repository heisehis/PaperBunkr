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

/// <summary>One ranked candidate row (docs/superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-
/// design.md §2 - select-then-confirm, not click-to-resolve).</summary>
public sealed partial class ComicVineMatchCandidateViewModel : ObservableObject
{
    private readonly Action<ComicVineMatchCandidateViewModel> _select;

    public ComicVineVolumeSearchResult Volume { get; }
    public double Score { get; }

    /// <summary>True only for the single highest-scored candidate - the "Best match" vs.
    /// "Other results" split is rank-based (index 0 of the already-sorted list), not a score
    /// threshold, since CE has no confidence cutoff anywhere in its own scoring (design doc §2).</summary>
    public bool IsTopMatch { get; internal set; }

    /// <summary>True only for the first candidate after the top match - the view uses this to draw
    /// the "Other results" section header exactly once, right above this row.</summary>
    public bool IsFirstOtherResult { get; internal set; }

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayLabel =>
        $"{Volume.Name} ({Volume.StartYear ?? "?"}) - {Volume.Publisher ?? "Unknown publisher"}";

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
    private bool _revertingProvider;

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
        Func<ComicProvider, bool>? switchProvider = null)
    {
        BookLabel = bookLabel;
        _providerText = ComicProviderFactory.DisplayName(provider);
        _switchProvider = switchProvider;
        _searchQuery = initialQuery;
        _search = search;
        _resolve = resolve;
        _loadIssues = loadIssues;
        SetCandidates(initialCandidates);
    }

    private void SetCandidates(IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)> ranked)
    {
        Candidates.Clear();
        SelectedCandidate = null;
        int index = 0;
        foreach (var candidate in ranked.OrderByDescending(c => c.Score))
        {
            var vm = new ComicVineMatchCandidateViewModel(candidate.Volume, candidate.Score, Select)
            {
                IsTopMatch = index == 0,
                IsFirstOtherResult = index == 1,
            };
            Candidates.Add(vm);
            index++;
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

    private void Select(ComicVineMatchCandidateViewModel candidate)
    {
        foreach (ComicVineMatchCandidateViewModel c in Candidates)
        {
            c.IsSelected = ReferenceEquals(c, candidate);
        }

        SelectedCandidate = candidate;
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
            BookLabel, issues, preSelected: null, readOnlyPeek: true, _ => IssuePeek = null);
    }
}
