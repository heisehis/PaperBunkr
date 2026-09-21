using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>A tracked ComicVine or Metron series on the Series tab.</summary>
public sealed partial class WatchedSeriesRowViewModel : ObservableObject
{
    public WatchedSeriesRowViewModel(bool watchFutureReleases) => _watchFutureReleases = watchFutureReleases;

    public required int Id { get; init; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _subtitle;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanOpen))]
    private int? _seriesId;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CountsText))]
    private int _missingCount;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CountsText))]
    private int _wantedCount;

    [ObservableProperty] private bool _isMetron;
    [ObservableProperty] private RemoteCoverSource _cover = new(null);

    private string? _coverUrl;

    public bool CanOpen => SeriesId is not null;
    public string CountsText => $"{MissingCount} missing · {WantedCount} wanted";

    /// <summary>"Follow": request future issues automatically. The initial value is set through the field, so loading never fires <see cref="FollowChanged"/>.</summary>
    [ObservableProperty]
    private bool _watchFutureReleases;

    public event Action<WatchedSeriesRowViewModel>? FollowChanged;

    private bool _loading;

    partial void OnWatchFutureReleasesChanged(bool value)
    {
        if (!_loading)
        {
            FollowChanged?.Invoke(this);
        }
    }

    /// <summary>Takes what the database says now without firing <see cref="FollowChanged"/> (a reload must never look like the user flipping the switch).</summary>
    internal void Update(string name, string? subtitle, int? seriesId, bool isMetron, string? coverUrl, int missing, int wanted, bool follow)
    {
        Name = name;
        Subtitle = subtitle;
        SeriesId = seriesId;
        IsMetron = isMetron;
        MissingCount = missing;
        WantedCount = wanted;
        if (!string.Equals(_coverUrl, coverUrl, StringComparison.Ordinal))
        {
            _coverUrl = coverUrl;
            Cover = new RemoteCoverSource(coverUrl);
        }

        _loading = true;
        try
        {
            WatchFutureReleases = follow;
        }
        finally
        {
            _loading = false;
        }
    }
}

/// <summary>A series found by the "track a series" search.</summary>
public sealed class VolumeResultViewModel
{
    public required ComicVineVolume Volume { get; init; }
    public string Title => Volume.StartYear is int year ? $"{Volume.Name} ({year})" : Volume.Name;
    public string Subtitle => $"{Volume.Publisher ?? "Unknown publisher"} · {Volume.CountOfIssues} issues";
    public bool IsTracked { get; init; }

    /// <summary>The top-ranked result, worth a nudge when the list is long.</summary>
    public bool IsBestMatch { get; init; }

    public RemoteCoverSource Cover { get; init; } = new(null);

    /// <summary>How many ranked results a picker shows at first.</summary>
    public const int PageSize = 15;
}

/// <summary>The Series tab: what is tracked and followed, and the search that tracks more.</summary>
public sealed partial class WantedScreenViewModel
{
    public ObservableCollection<WatchedSeriesRowViewModel> SeriesRows { get; } = new();
    public ObservableCollection<VolumeResultViewModel> SearchResults { get; } = new();

    public bool HasNoSeries => SeriesRows.Count == 0;
    public bool HasSearchResults => SearchResults.Count > 0;

    /// <summary>Which source "Track a series" searches ("ComicVine" or "Metron").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeriesSearchWatermark), nameof(HasSeriesProviderCredentials))]
    private string _seriesProviderText = "ComicVine";

    public static IReadOnlyList<string> ProviderNames => SeriesMissingIssuesViewModel.ProviderNames;

    public string SeriesSearchWatermark => $"Search {ComicProviderFactory.DisplayName(ComicProviderFactory.Parse(SeriesProviderText))}, e.g. Spawn";

    public bool HasSeriesProviderCredentials
    {
        get
        {
            using var context = _createContext();
            return ComicProviderFactory.IsAvailable(context, ComicProviderFactory.Parse(SeriesProviderText));
        }
    }

    [ObservableProperty]
    private string _seriesSearchText = string.Empty;

    /// <summary>What the search flyout says about the last search (no results, a bad key). Lives in the flyout, where the user is looking, not in a toast behind it.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSeriesSearchMessage))]
    private string _seriesSearchMessage = string.Empty;

    public bool HasSeriesSearchMessage => !string.IsNullOrEmpty(SeriesSearchMessage);

    private void LoadSeries(PaperbunkrDbContext context)
    {
        var known = SeriesRows.ToDictionary(r => r.Id);
        var desired = new List<WatchedSeriesRowViewModel>();
        var all = context.WatchedSeries.OrderBy(w => w.Name).ToList();

        // A series with no cover of its own borrows its first wanted issue's, then (for Metron, which has none) one from the weekly list's cache.
        var issueCovers = context.WantedIssues.Where(w => w.CoverImageUrl != null).OrderBy(w => w.Id).AsEnumerable()
            .GroupBy(w => w.WatchedSeriesId).ToDictionary(g => g.Key, g => g.First().CoverImageUrl);
        var cachedCovers = PullListService.CachedCovers(context, all.Where(w => w.Provider == ComicProvider.Metron).Select(w => w.ExternalVolumeId));

        foreach (var watched in all)
        {
            string subtitle = string.Join(" · ", new[] { watched.Publisher, watched.StartYear?.ToString(CultureInfo.InvariantCulture) }.Where(s => !string.IsNullOrEmpty(s)));
            int missing = WantedService.GetMissing(context, watched).Count;
            int wanted = context.WantedIssues.Count(w => w.WatchedSeriesId == watched.Id && w.Status == WantedIssueStatus.Wanted);
            if (!known.Remove(watched.Id, out var row))
            {
                row = new WatchedSeriesRowViewModel(watched.WatchFutureReleases) { Id = watched.Id };
                row.FollowChanged += OnFollowChanged;
            }

            row.Update(watched.Name, subtitle, watched.SeriesId, watched.Provider == ComicProvider.Metron,
                watched.CoverImageUrl ?? issueCovers.GetValueOrDefault(watched.Id) ?? cachedCovers.GetValueOrDefault(watched.ExternalVolumeId),
                missing, wanted, watched.WatchFutureReleases);
            desired.Add(row);
        }

        foreach (var gone in known.Values)
        {
            gone.FollowChanged -= OnFollowChanged;
        }

        SyncList(SeriesRows, desired);
        OnPropertyChanged(nameof(HasNoSeries));
    }

    [RelayCommand]
    private void OpenSeries(WatchedSeriesRowViewModel row)
    {
        if (row.SeriesId is int seriesId)
        {
            _openSeries(seriesId);
        }
    }

    [RelayCommand]
    private async Task SearchSeriesAsync(CancellationToken cancellationToken)
    {
        var query = SeriesSearchText.Trim();
        if (query.Length == 0)
        {
            return;
        }

        SeriesSearchMessage = string.Empty;
        var provider = ComicProviderFactory.Parse(SeriesProviderText);
        HashSet<int> tracked;
        using (var context = _createContext())
        {
            if (!ComicProviderFactory.IsAvailable(context, provider))
            {
                SeriesSearchMessage = ComicProviderFactory.MissingCredentialsMessage(provider).Replace("first.", "to search for series.");
                return;
            }

            tracked = context.WatchedSeries.Where(w => w.Provider == provider).Select(w => w.ExternalVolumeId).ToHashSet();
        }

        try
        {
            // Ranked by name match (there is no local series to compare with here), and more than ComicVine's first 25 by issue count.
            var volumes = await VolumeSearchService.SearchAndRankAsync(_createProvider(provider), query, new LocalSeriesHints(query), cancellationToken);
            if (provider == ComicProvider.Metron && volumes.Count > 0)
            {
                // Metron has no series covers: borrow one from the weekly list's cache where the series is in it.
                using var coverContext = _createContext();
                var covers = PullListService.CachedCovers(coverContext, volumes.Take(VolumeResultViewModel.PageSize).Select(r => r.Volume.Id));
                volumes = volumes.Select(r => covers.TryGetValue(r.Volume.Id, out var url) ? r with { Volume = r.Volume with { ImageUrl = url } } : r).ToList();
            }

            Fill(SearchResults, volumes.Take(VolumeResultViewModel.PageSize).Select((r, i) => new VolumeResultViewModel { Volume = r.Volume, IsTracked = tracked.Contains(r.Volume.Id), IsBestMatch = i == 0 && volumes.Count > 1, Cover = new RemoteCoverSource(r.Volume.ImageUrl) }));
            OnPropertyChanged(nameof(HasSearchResults));
            SeriesSearchMessage = volumes.Count == 0 ? $"No {ComicProviderFactory.DisplayName(provider)} series found for \"{query}\"." : string.Empty;
        }
        catch (ComicVineException ex)
        {
            SeriesSearchMessage = ex.Message;
        }
    }

    /// <summary>Starts tracking a volume (not following it) and caches its issue list so its missing issues can be shown.</summary>
    [RelayCommand]
    private async Task TrackVolumeAsync(VolumeResultViewModel result, CancellationToken cancellationToken)
    {
        // Results are only ever shown for the source that was searched, so the selector still names the right one.
        var provider = ComicProviderFactory.Parse(SeriesProviderText);
        using (var check = _createContext())
        {
            if (!ComicProviderFactory.IsAvailable(check, provider))
            {
                SeriesSearchMessage = ComicProviderFactory.MissingCredentialsMessage(provider);
                return;
            }
        }

        try
        {
            var issues = await _createProvider(provider).GetVolumeIssuesAsync(result.Volume.Id, cancellationToken);

            int missing;
            using (var context = _createContext())
            {
                int? seriesId = context.Series.AsEnumerable().FirstOrDefault(s => SeriesNames.Same(s.Name, result.Volume.Name))?.Id;
                var watched = WantedService.TrackVolume(context, result.Volume, seriesId, watchFutureReleases: false, provider);
                WantedService.RefreshCatalog(context, watched, issues);
                missing = WantedService.GetMissing(context, watched).Count;
            }

            // The Track button is inside the results list this replaces, and it may still be routing its click.
            _post(() =>
            {
                Refresh();
                Fill(SearchResults, SearchResults.Select(r => new VolumeResultViewModel { Volume = r.Volume, IsTracked = r.IsTracked || r.Volume.Id == result.Volume.Id, IsBestMatch = r.IsBestMatch, Cover = r.Cover }));
                Notify($"Tracking {result.Title}: {missing} of {issues.Count} issues missing.", isError: false);
            });
        }
        catch (ComicVineException ex)
        {
            SeriesSearchMessage = ex.Message;
        }
    }

    /// <summary>Stops tracking a series: it, its wants and its cached issue list are removed. Issues already in the library are untouched.</summary>
    [RelayCommand]
    private async Task UntrackSeriesAsync(WatchedSeriesRowViewModel row)
    {
        string wanted = row.WantedCount switch
        {
            0 => string.Empty,
            1 => " Its 1 wanted issue is dropped.",
            var n => $" Its {n} wanted issues are dropped.",
        };
        if (!await _confirm($"Stop tracking {row.Name}?{wanted} Issues already in your library are not touched.", "Untrack"))
        {
            return;
        }

        _post(() =>
        {
            using (var context = _createContext())
            {
                var watched = context.WatchedSeries.FirstOrDefault(w => w.Id == row.Id);
                if (watched is not null)
                {
                    context.WatchedSeries.Remove(watched);
                    context.SaveChanges();
                }
            }

            Refresh();
            Notify($"No longer tracking {row.Name}.", isError: false);
        });
    }

    private void OnFollowChanged(WatchedSeriesRowViewModel row)
    {
        using var context = _createContext();
        WantedService.SetWatchFutureReleases(context, row.Id, row.WatchFutureReleases);
        Notify(row.WatchFutureReleases
            ? $"Following {row.Name}: new issues will be requested automatically."
            : $"No longer following {row.Name}.", isError: false);
    }
}
