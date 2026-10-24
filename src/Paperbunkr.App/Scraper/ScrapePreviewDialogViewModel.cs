using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;

namespace Paperbunkr.App.Scraper;

/// <summary>One comic in the preview: keep it or not, and what it would change.</summary>
public sealed partial class ScrapePreviewRow(ScrapeProposal proposal) : ObservableObject
{
    public ScrapeProposal Proposal { get; } = proposal;

    public string BookLabel => Proposal.BookLabel;

    public string MatchText => $"{ComicProviderFactory.DisplayName(Proposal.Provider)} · {Proposal.Volume.Name}{(Proposal.Volume.StartYear is { Length: > 0 } y ? $" ({y})" : "")}"
        + (Proposal.Details?.IssueNumber is { Length: > 0 } n ? $" #{n}" : " · series only");

    public string ChangeCountText => Proposal.Changes.Count switch
    {
        0 => "no changes",
        1 => "1 change",
        int n => $"{n} changes",
    };

    /// <summary>A series-level match only (no issue found): worth a look before applying.</summary>
    public bool IsSeriesOnly => Proposal.Details is null;

    [ObservableProperty]
    private bool _isKept = true;
}

/// <summary>
/// "What this scrape would change" (docs/superpowers/specs/2026-10-21-scraper-organizer-c-automation-ux-design.md section 3): every matched comic
/// with a check box and, for the selected one, each field's before and after. Apply writes the kept comics; Apply nothing writes none.
/// Space toggles the focused comic (the list's own key), as a check box would.
/// </summary>
public sealed partial class ScrapePreviewDialogViewModel : ObservableObject
{
    private readonly Action<IReadOnlyCollection<int>?> _resolve;

    public ScrapePreviewDialogViewModel(IReadOnlyList<ScrapeProposal> proposals, Action<IReadOnlyCollection<int>?> resolve)
    {
        _resolve = resolve;
        foreach (var proposal in proposals)
        {
            var row = new ScrapePreviewRow(proposal);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ScrapePreviewRow.IsKept))
                {
                    OnPropertyChanged(nameof(ApplyLabel));
                    ApplyCommand.NotifyCanExecuteChanged();
                }
            };
            Rows.Add(row);
        }

        SelectedRow = Rows.FirstOrDefault();
    }

    public ObservableCollection<ScrapePreviewRow> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedChanges), nameof(HasSelectedChanges))]
    private ScrapePreviewRow? _selectedRow;

    public IReadOnlyList<ScrapeFieldChange> SelectedChanges => SelectedRow?.Proposal.Changes ?? Array.Empty<ScrapeFieldChange>();

    public bool HasSelectedChanges => SelectedChanges.Count > 0;

    public string Subtitle
    {
        get
        {
            int seriesOnly = Rows.Count(r => r.IsSeriesOnly);
            return $"{Rows.Count} comics matched. Nothing has been written yet." + (seriesOnly > 0 ? $" {seriesOnly} matched the series only (no issue found)." : string.Empty);
        }
    }

    public string ApplyLabel => $"Apply {Rows.Count(r => r.IsKept)}";

    [RelayCommand]
    private void KeepAll()
    {
        foreach (var row in Rows)
        {
            row.IsKept = true;
        }
    }

    [RelayCommand]
    private void KeepNone()
    {
        foreach (var row in Rows)
        {
            row.IsKept = false;
        }
    }

    /// <summary>Unticks the comics that matched only a series, the ones most likely to be wrong.</summary>
    [RelayCommand]
    private void DropSeriesOnly()
    {
        foreach (var row in Rows.Where(r => r.IsSeriesOnly))
        {
            row.IsKept = false;
        }
    }

    [RelayCommand]
    private void ToggleSelected()
    {
        if (SelectedRow is not null)
        {
            SelectedRow.IsKept = !SelectedRow.IsKept;
        }
    }

    private bool CanApply() => Rows.Any(r => r.IsKept);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => _resolve(Rows.Where(r => r.IsKept).Select(r => r.Proposal.IssueId).ToList());

    [RelayCommand]
    private void ApplyNothing() => _resolve(Array.Empty<int>());
}
