using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.ComicVine.Scraping;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Scraper;

/// <summary>One issue row (docs/superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-design.md
/// §3) - no score/tiering, unlike <see cref="ComicVineMatchCandidateViewModel"/>: issues within one
/// already-chosen volume aren't ranked against each other, just listed.</summary>
public sealed partial class ComicVineIssueCandidateViewModel : ObservableObject
{
    private readonly Action<ComicVineIssueCandidateViewModel> _select;

    public ComicVineIssueSummary Issue { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayLabel => $"#{Issue.IssueNumber ?? "?"} - {Issue.Name ?? "Untitled"}";

    public string? CoverImageUrl => Issue.ImageUrl;

    public ComicVineIssueCandidateViewModel(ComicVineIssueSummary issue, Action<ComicVineIssueCandidateViewModel> select)
    {
        Issue = issue;
        _select = select;
    }

    [RelayCommand]
    private void Select() => _select(this);
}

/// <summary>
/// Backs <see cref="ComicVineIssueReviewDialogView"/> (docs/superpowers/specs/2026-09-13-cluster-
/// scraper-ui-redesign-design.md §3) - CE's second dialog ("Choose a Comic Book Issue"), which
/// Paperbunkr never had at all until this pass (verified: <c>ScrapeOrchestrator.FindIssueDetailsAsync</c>
/// matched the issue number silently, no dialog existed anywhere in the codebase for this step).
///
/// Same list+cover-pane shape as <see cref="ComicVineMatchReviewDialogViewModel"/> for visual
/// consistency, applied to <see cref="ComicVineIssueSummary"/> rows instead of volumes - no
/// source/sort toolbar here, since issues within one already-chosen volume don't need either.
///
/// <see cref="ReadOnlyPeek"/> backs the series dialog's "Show issues" link (relevant specifically
/// when <c>ConfirmIssueMatch</c> is off): only a <see cref="CloseCommand"/> is available, and closing
/// never carries a real decision back to the orchestrator - the caller wires its own no-op resolve
/// for that case (see <c>OrganizerScraperPlugin</c>).
/// </summary>
public sealed partial class ComicVineIssueReviewDialogViewModel : ObservableObject
{
    private readonly Action<ComicVineIssueReviewResult> _resolve;

    public string BookLabel { get; }

    public bool ReadOnlyPeek { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private ComicVineIssueCandidateViewModel? _selectedCandidate;

    public ObservableCollection<ComicVineIssueCandidateViewModel> Candidates { get; } = new();

    public ComicVineIssueReviewDialogViewModel(
        string bookLabel,
        IReadOnlyList<ComicVineIssueSummary> issues,
        ComicVineIssueSummary? preSelected,
        bool readOnlyPeek,
        Action<ComicVineIssueReviewResult> resolve)
    {
        BookLabel = bookLabel;
        ReadOnlyPeek = readOnlyPeek;
        _resolve = resolve;

        ComicVineIssueCandidateViewModel? toSelect = null;
        foreach (ComicVineIssueSummary issue in issues)
        {
            var vm = new ComicVineIssueCandidateViewModel(issue, Select);
            Candidates.Add(vm);
            if (preSelected is not null && issue.Id == preSelected.Id)
            {
                toSelect = vm;
            }
        }

        // Falls back to the first issue when nothing auto-matched (design doc §3: confirming the
        // common "the automatic match is right" case is a single click either way).
        Select(toSelect ?? Candidates.FirstOrDefault());
    }

    private void Select(ComicVineIssueCandidateViewModel? candidate)
    {
        foreach (ComicVineIssueCandidateViewModel c in Candidates)
        {
            c.IsSelected = ReferenceEquals(c, candidate);
        }

        SelectedCandidate = candidate;
    }

    private bool CanConfirm() => SelectedCandidate is not null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => _resolve(ComicVineIssueReviewResult.Confirmed(SelectedCandidate!.Issue));

    /// <summary>Apply volume-level fields only, no per-issue fields - same resilience as an
    /// unattended run finding no number match at all.</summary>
    [RelayCommand]
    private void Skip() => _resolve(ComicVineIssueReviewResult.Skipped);

    /// <summary>Re-shows the series dialog for this same book (CE parity).</summary>
    [RelayCommand]
    private void GoBack() => _resolve(ComicVineIssueReviewResult.WentBack);

    [RelayCommand]
    private void Close() => _resolve(ComicVineIssueReviewResult.WentBack);
}
