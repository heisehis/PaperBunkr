using System;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Series Detail screen, "stacked" layout variant (the default selected in the parent
/// "Paperbunkr App" wireframe), ported from DetailScreen.dc.html. Loads a real
/// <see cref="Series"/> by id via <see cref="LoadSeries"/> instead of the wireframe's
/// hardcoded "Brass Horizon" sample content.
/// </summary>
public partial class DetailScreenViewModel : ViewModelBase
{
    public DetailScreenViewModel(Action goBack)
    {
        _goBack = goBack;
        CoverBrush = SeriesCardSample.Gradient("#442a1c", "#c9803f");
        Tabs = new DetailTabsViewModel();
        Meta = new DetailMetaViewModel();
        Pills = new DetailPillsViewModel();
    }

    private readonly Action _goBack;

    public DetailTabsViewModel Tabs { get; }
    public DetailMetaViewModel Meta { get; }
    public DetailPillsViewModel Pills { get; }

    [ObservableProperty]
    private IBrush _coverBrush;

    [ObservableProperty]
    private string _seriesTitle = string.Empty;

    [ObservableProperty]
    private string _coverTitle = string.Empty;

    [ObservableProperty]
    private string _contentTypeLabel = string.Empty;

    [ObservableProperty]
    private string _statusLabel = string.Empty;

    [ObservableProperty]
    private string _issueCountLabel = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _continueLabel = "Start Reading";

    /// <summary>Loads the series with the given id from the database and refreshes every bound field.</summary>
    public void LoadSeries(int seriesId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Include(s => s.Issues).FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return;
        }

        var card = SeriesCardSample.FromSeries(series);
        CoverBrush = card.CoverBrush;
        SeriesTitle = series.Name;
        CoverTitle = series.Name.ToUpperInvariant();
        ContentTypeLabel = series.ContentType.ToString();
        StatusLabel = series.IsComplete ? "Complete" : "Ongoing";
        IssueCountLabel = $"{series.Issues.Count} Issues";
        Summary = string.IsNullOrWhiteSpace(series.Summary) ? "No summary available." : series.Summary;

        var nextUnread = series.Issues
            .Where(i => i.LastPageRead is null or 0)
            .OrderByNumber()
            .FirstOrDefault();
        ContinueLabel = nextUnread is null
            ? "Start Reading"
            : $"Continue — Issue #{nextUnread.Number}";

        Tabs.LoadSeries(series);
        Meta.LoadSeries(series);
        Pills.LoadSeries(series);
    }

    [RelayCommand]
    private void GoBack() => _goBack();
}
