using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Issues/Related/Details/Activity tab strip, ported from DetailTabs.dc.html (Claude Design
/// project 43c40b25). Populated from the real <see cref="Series"/> passed to
/// <see cref="LoadSeries"/> rather than the wireframe's static sample data.
/// </summary>
public partial class DetailTabsViewModel : ViewModelBase
{
    private readonly Action<int> _goToProperties;
    private readonly Action<IReadOnlyList<int>> _goToBulkProperties;
    private readonly Action? _onSelectionChanged;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private int? _seriesId;
    private int? _lastToggledIndex;

    public DetailTabsViewModel(Action<int> goToProperties, Action<IReadOnlyList<int>> goToBulkProperties, Action? onSelectionChanged = null)
        : this(goToProperties, goToBulkProperties, onSelectionChanged, PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal DetailTabsViewModel(Action<int> goToProperties, Action<IReadOnlyList<int>> goToBulkProperties, Action? onSelectionChanged, Func<PaperbunkrDbContext> contextFactory)
    {
        _goToProperties = goToProperties;
        _goToBulkProperties = goToBulkProperties;
        _onSelectionChanged = onSelectionChanged;
        _contextFactory = contextFactory;
        Issues = new ObservableCollection<IssueCardSample>();
        Related = new ObservableCollection<RelatedSeriesSample>();
    }

    /// <summary>Multi-select state for the Issues tab (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md §1).</summary>
    public HashSet<int> SelectedIssueIds { get; } = new();

    public ObservableCollection<IssueCardSample> Issues { get; }

    /// <summary>
    /// Always empty for now - there's no "related series" data/schema yet (only DetailTabs.dc.html's
    /// own sample content had this). Left genuinely empty rather than faked, since that's the real
    /// state of the feature.
    /// </summary>
    public ObservableCollection<RelatedSeriesSample> Related { get; }

    public string Publisher { get; private set; } = "Unknown";
    public string ReadingModeLabel { get; private set; } = "Left to Right";

    public void LoadSeries(Series series)
    {
        var coverBrush = SeriesCardSample.CoverBrushFor(series.Name);

        Issues.Clear();
        SelectedIssueIds.Clear();
        _lastToggledIndex = null;
        foreach (var issue in series.Issues.OrderByNumber())
        {
            Issues.Add(new IssueCardSample
            {
                Id = issue.Id,
                Title = string.IsNullOrWhiteSpace(issue.Number) ? "#?" : $"#{issue.Number}",
                IsUnread = issue.LastPageRead is null or 0,
                CoverBrush = coverBrush,
                CoverImage = CoverImageCache.Get(issue.Id),
            });
        }

        _seriesId = series.Id;
        Publisher = string.IsNullOrWhiteSpace(series.Publisher) ? "Unknown" : series.Publisher;
        SetReadingModeLabel(series.ReadingMode);
        OnPropertyChanged(nameof(Publisher));

        ActiveTab = "issues";
        _onSelectionChanged?.Invoke();
    }

    private void SetReadingModeLabel(ReadingMode mode)
    {
        ReadingModeLabel = mode switch
        {
            ReadingMode.RightToLeft => "Right to Left ▾",
            ReadingMode.VerticalContinuous => "Vertical (Continuous) ▾",
            ReadingMode.HorizontalContinuous => "Horizontal (Continuous) ▾",
            _ => "Left to Right ▾",
        };
        OnPropertyChanged(nameof(ReadingModeLabel));
    }

    /// <summary>
    /// Binary flip only (docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md §5) -
    /// <see cref="Entities.ReadingMode.VerticalContinuous"/>/<see cref="Entities.ReadingMode.HorizontalContinuous"/>
    /// are unreachable via any UI today (only CE migration data produces them, and there's no
    /// scroll-paging implementation behind them yet), so toggling from either of those collapses
    /// to <see cref="Entities.ReadingMode.RightToLeft"/> rather than growing a full mode picker.
    /// </summary>
    [RelayCommand]
    private void ToggleReadingMode()
    {
        if (_seriesId is not int seriesId)
        {
            return;
        }

        using var context = _contextFactory();
        var series = context.Series.FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return;
        }

        series.ReadingMode = series.ReadingMode == ReadingMode.RightToLeft ? ReadingMode.LeftToRight : ReadingMode.RightToLeft;
        context.SaveChanges();

        SetReadingModeLabel(series.ReadingMode);
    }

    [ObservableProperty]
    private string _activeTab = "issues";

    public bool IsIssuesTab => ActiveTab == "issues";
    public bool IsRelatedTab => ActiveTab == "related";
    public bool IsDetailsTab => ActiveTab == "details";
    public bool IsActivityTab => ActiveTab == "activity";

    partial void OnActiveTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsIssuesTab));
        OnPropertyChanged(nameof(IsRelatedTab));
        OnPropertyChanged(nameof(IsDetailsTab));
        OnPropertyChanged(nameof(IsActivityTab));
    }

    [RelayCommand]
    private void GoIssues() => ActiveTab = "issues";

    [RelayCommand]
    private void GoRelated() => ActiveTab = "related";

    [RelayCommand]
    private void GoDetails() => ActiveTab = "details";

    [RelayCommand]
    private void GoActivity() => ActiveTab = "activity";

    /// <summary>
    /// Left-click tile selection (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md
    /// §1): plain click toggles the clicked tile; shift-click additionally selects the contiguous
    /// range from the last-toggled tile to this one, without clearing the existing selection.
    /// </summary>
    public void ToggleIssueSelection(IssueCardSample issue, bool isShiftHeld)
    {
        int index = Issues.IndexOf(issue);
        if (index < 0)
        {
            return;
        }

        if (isShiftHeld && _lastToggledIndex is int lastIndex)
        {
            int start = Math.Min(lastIndex, index);
            int end = Math.Max(lastIndex, index);
            for (int i = start; i <= end; i++)
            {
                Issues[i].IsSelected = true;
                SelectedIssueIds.Add(Issues[i].Id);
            }
        }
        else
        {
            issue.IsSelected = !issue.IsSelected;
            if (issue.IsSelected)
            {
                SelectedIssueIds.Add(issue.Id);
            }
            else
            {
                SelectedIssueIds.Remove(issue.Id);
            }
        }

        _lastToggledIndex = index;
        _onSelectionChanged?.Invoke();
    }

    /// <summary>
    /// Right-click entry point into the Issue Properties editor(s) (docs/superpowers/specs/
    /// 2026-08-07-issue-properties-editor-design.md §2, extended by docs/superpowers/specs/
    /// 2026-08-07-bulk-issue-editing-design.md §2) - operates on the current selection union the
    /// right-clicked tile, so right-clicking a lone unselected tile with nothing else selected
    /// (today's entire pre-existing behavior) still just edits that one issue.
    /// </summary>
    [RelayCommand]
    private void EditIssueProperties(IssueCardSample issue)
    {
        var ids = (SelectedIssueIds.Count > 0 ? SelectedIssueIds.Append(issue.Id) : new[] { issue.Id })
            .Distinct()
            .ToList();

        if (ids.Count == 1)
        {
            _goToProperties(ids[0]);
        }
        else
        {
            _goToBulkProperties(ids);
        }
    }
}
