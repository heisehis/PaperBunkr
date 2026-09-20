using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.ComicVine.Scraping;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Scraper;

/// <summary>
/// Backs <see cref="SeriesScraperPanelView"/> - the comic Detail screen's "Details" tab replacement
/// for the host's generic External Metadata/Trackers block (design note: those target manga
/// metadata sources with little to no real comic coverage; ComicVine is the actual right source
/// for a Comic-typed series). Returned from <c>OrganizerScraperPlugin.CreateSeriesDetailView</c>,
/// hosted inline in the host's own screen rather than a modal - see that interface's own doc
/// comment for why this stays visually plain (host `DynamicResource` keys, no card chrome of its
/// own) rather than following the plugin's other, modal-hosted dialogs' self-contained styling.
/// </summary>
public sealed partial class SeriesScraperPanelViewModel : ObservableObject
{
    private readonly Func<Task<string>> _runScrape;

    public string SeriesName { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    public SeriesScraperPanelViewModel(string seriesName, Func<Task<string>> runScrape)
    {
        SeriesName = seriesName;
        _runScrape = runScrape;
    }

    [RelayCommand]
    private async Task Scrape()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            StatusMessage = await _runScrape().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
