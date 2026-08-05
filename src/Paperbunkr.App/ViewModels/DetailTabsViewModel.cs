using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Issues/Related/Details/Activity tab strip, ported from DetailTabs.dc.html (Claude Design
/// project 43c40b25). Populated from the real <see cref="Series"/> passed to
/// <see cref="LoadSeries"/> rather than the wireframe's static sample data.
/// </summary>
public partial class DetailTabsViewModel : ViewModelBase
{
    public DetailTabsViewModel()
    {
        Issues = new ObservableCollection<IssueCardSample>();
        Related = new ObservableCollection<RelatedSeriesSample>();
    }

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
        foreach (var issue in series.Issues.OrderByNumber())
        {
            Issues.Add(new IssueCardSample
            {
                Title = string.IsNullOrWhiteSpace(issue.Number) ? "#?" : $"#{issue.Number}",
                IsUnread = issue.LastPageRead is null or 0,
                CoverBrush = coverBrush,
            });
        }

        Publisher = string.IsNullOrWhiteSpace(series.Publisher) ? "Unknown" : series.Publisher;
        ReadingModeLabel = series.ReadingMode switch
        {
            ReadingMode.RightToLeft => "Right to Left",
            ReadingMode.VerticalContinuous => "Vertical (Continuous)",
            ReadingMode.HorizontalContinuous => "Horizontal (Continuous)",
            _ => "Left to Right",
        };
        OnPropertyChanged(nameof(Publisher));
        OnPropertyChanged(nameof(ReadingModeLabel));

        ActiveTab = "issues";
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
}
