using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Issues/Related/Details/Activity tab strip, ported from DetailTabs.dc.html (Claude Design
/// project 43c40b25). Owns the sample issue/related data shown across those tabs.
/// </summary>
public partial class DetailTabsViewModel : ViewModelBase
{
    public DetailTabsViewModel()
    {
        var brass = SeriesCardSample.Gradient("#442a1c", "#c9803f");

        Issues = new ObservableCollection<IssueCardSample>();
        for (var i = 0; i < 8; i++)
        {
            Issues.Add(new IssueCardSample
            {
                CoverBrush = brass,
                Title = $"#{i + 9}",
                IsUnread = i % 3 == 0,
            });
        }

        Related = new ObservableCollection<RelatedSeriesSample>
        {
            new() { CoverBrush = SeriesCardSample.Gradient("#26313f", "#4a6b8a"), Title = "KILO STATION", Name = "Kilo Station", Note = "Shared universe" },
            new() { CoverBrush = SeriesCardSample.Gradient("#1f2a1c", "#5c8a4a"), Title = "IRONCLAD REQUIEM", Name = "Ironclad Requiem", Note = "Same writer" },
            new() { CoverBrush = SeriesCardSample.Gradient("#3a2f45", "#8a4a2e"), Title = "THE CARTOGRAPHER'S VAULT", Name = "The Cartographer's Vault", Note = "Frequent crossover" },
            new() { CoverBrush = SeriesCardSample.Gradient("#2a2333", "#6a5ca3"), Title = "PAPER MOTH", Name = "Paper Moth", Note = "Readers also liked" },
        };
    }

    public ObservableCollection<IssueCardSample> Issues { get; }
    public ObservableCollection<RelatedSeriesSample> Related { get; }

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
