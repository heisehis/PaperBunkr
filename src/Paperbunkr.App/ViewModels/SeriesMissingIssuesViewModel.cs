using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>One issue of a tracked series that the library doesn't have and nobody has asked for yet.</summary>
public sealed class MissingIssueRowViewModel
{
    public required int CatalogIssueId { get; init; }
    public required string Number { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public bool IsUpcoming { get; init; }
    public RemoteCoverSource Cover { get; init; } = new(null);
}

/// <summary>
/// The series Detail screen's "Missing Issues (n)" section (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §7, the
/// Omnibus pattern): the ComicVine issue list minus what the library owns and what is already wanted, each with a Request button, plus
/// "Request all" and a Follow toggle. A series that isn't tracked yet offers to find its ComicVine volume first. "Request all" asks for
/// confirmation inline (a second click) rather than via a dialog, so this section stays self-contained.
/// </summary>
public sealed partial class SeriesMissingIssuesViewModel : ViewModelBase
{
    private readonly Func<PaperbunkrDbContext> _createContext;
    private readonly Func<ComicProvider, IComicVineClient> _createProvider;
    private readonly Action<Action> _post;
    private bool _loading;
    private int _seriesId;
    private int _watchedSeriesId;

    public SeriesMissingIssuesViewModel(Func<PaperbunkrDbContext> createContext, Func<ComicProvider, IComicVineClient>? createProvider = null, Action<Action>? post = null)
    {
        _createContext = createContext;
        _createProvider = createProvider ?? (provider =>
        {
            using var context = createContext();
            return ComicProviderFactory.Create(context, provider)!;
        });
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
    }

    public ObservableCollection<MissingIssueRowViewModel> MissingRows { get; } = new();
    public ObservableCollection<VolumeResultViewModel> SearchResults { get; } = new();

    private IReadOnlyList<RankedVolume> _ranked = Array.Empty<RankedVolume>();

    /// <summary>What to look for on ComicVine. Starts as the series name and can be edited (add a subtitle, drop "The", fix a spelling) before searching again.</summary>
    [ObservableProperty] private string _searchText = string.Empty;

    public static IReadOnlyList<string> ProviderNames { get; } = ComicProviderFactory.All.Select(ComicProviderFactory.DisplayName).ToList();

    /// <summary>Which source the search below uses ("ComicVine" or "Metron"; a text value for the suggest box). A tracked series always uses the source it was tracked with.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchWatermark), nameof(SearchLabel))]
    private string _providerText = "ComicVine";

    private ComicProvider SelectedProvider => IsTracked ? _trackedProvider : ComicProviderFactory.Parse(ProviderText);
    private ComicProvider _trackedProvider;

    /// <summary>The source's name (ComicVine / Metron) for messages.</summary>
    private string ProviderName => ComicProviderFactory.DisplayName(SelectedProvider);

    public string SearchWatermark => $"Series name on {ComicProviderFactory.DisplayName(ComicProviderFactory.Parse(ProviderText))}";
    public string SearchLabel => $"Search {ComicProviderFactory.DisplayName(ComicProviderFactory.Parse(ProviderText))} for this series";

    /// <summary>True when the tracked series comes from Metron (shows a small "Metron" chip; ComicVine, the default, shows nothing).</summary>
    public bool IsMetronTracked => IsTracked && _trackedProvider == ComicProvider.Metron;

    [ObservableProperty] private bool _hasProviderCredentials;

    partial void OnProviderTextChanged(string value)
    {
        if (!_loading)
        {
            using var context = _createContext();
            HasProviderCredentials = ComicProviderFactory.IsAvailable(context, ComicProviderFactory.Parse(value));
        }
    }

    /// <summary>How many of the ranked results are showing; "Show more" reveals another page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowMore), nameof(ShowMoreLabel))]
    private int _shownCount;

    public bool CanShowMore => _ranked.Count > ShownCount;

    public string ShowMoreLabel => $"Show more ({_ranked.Count - ShownCount} more)";

    public static IReadOnlyList<string> SortNames { get; } = new[] { "Best match", "Most issues", "Newest", "Oldest", "Name A–Z" };

    /// <summary>How the fetched results are ordered (a text value for the suggest box; the app has no ComboBox). Changing it re-orders what is already here, with no new request.</summary>
    [ObservableProperty] private string _sortText = "Best match";

    partial void OnSortTextChanged(string value)
    {
        if (_ranked.Count == 0)
        {
            return;
        }

        ShownCount = Math.Min(VolumeResultViewModel.PageSize, _ranked.Count);
        RebuildResults();
    }

    private bool IsBestMatchSort => !SortNames.Contains(SortText, StringComparer.Ordinal) || SortText == "Best match";

    private IEnumerable<RankedVolume> Sorted() => SortText switch
    {
        "Most issues" => _ranked.OrderByDescending(r => r.Volume.CountOfIssues).ThenByDescending(r => r.Score),
        "Newest" => _ranked.OrderByDescending(r => r.Volume.StartYear ?? int.MinValue).ThenByDescending(r => r.Score),
        "Oldest" => _ranked.OrderBy(r => r.Volume.StartYear ?? int.MaxValue).ThenByDescending(r => r.Score),
        "Name A–Z" => _ranked.OrderBy(r => r.Volume.Name, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.Score),
        _ => _ranked,
    };

    /// <summary>"Best of 187 ComicVine series - matched on name, start year, issue count and publisher." Empty when there is no search.</summary>
    [ObservableProperty] private string _searchSummary = string.Empty;

    [ObservableProperty] private string _seriesName = string.Empty;
    [ObservableProperty] private bool _isTracked;
    [ObservableProperty] private bool _hasComicVineKey;

    /// <summary>"Follow": request this series' future issues automatically.</summary>
    [ObservableProperty] private bool _watchFutureReleases;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotConfirming))]
    private bool _isConfirmingRequestAll;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus), nameof(HasErrorStatus), nameof(HasInfoStatus))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorStatus), nameof(HasInfoStatus))]
    private bool _statusIsError;

    public bool IsNotConfirming => !IsConfirmingRequestAll;
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool HasErrorStatus => HasStatus && StatusIsError;
    public bool HasInfoStatus => HasStatus && !StatusIsError;

    public int MissingCount => MissingRows.Count;
    public bool HasMissing => MissingRows.Count > 0;
    public bool IsAllCaughtUp => IsTracked && MissingRows.Count == 0;
    public bool IsNotTracked => !IsTracked;
    public bool HasSearchResults => SearchResults.Count > 0;
    public string Heading => $"Missing Issues ({MissingRows.Count})";
    public string ConfirmText => MissingRows.Count == 1 ? "Request this issue?" : $"Request all {MissingRows.Count} issues?";

    /// <summary>Reloads the section for a series. Cheap: two small queries, no network.</summary>
    public void Load(int seriesId, string seriesName)
    {
        _seriesId = seriesId;
        SeriesName = seriesName;
        SearchText = seriesName;
        _ranked = Array.Empty<RankedVolume>();
        ProviderText = "ComicVine";
        SortText = "Best match";
        SearchSummary = string.Empty;
        SearchResults.Clear();
        IsConfirmingRequestAll = false;
        StatusMessage = string.Empty;
        Reload();
    }

    private void Reload()
    {
        _loading = true;
        try
        {
            using var context = _createContext();
            HasComicVineKey = !string.IsNullOrEmpty(CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey));

            var watched = context.WatchedSeries.FirstOrDefault(w => w.SeriesId == _seriesId);
            _trackedProvider = watched?.Provider ?? ComicProvider.ComicVine;
            HasProviderCredentials = ComicProviderFactory.IsAvailable(context, watched is not null ? watched.Provider : ComicProviderFactory.Parse(ProviderText));
            IsTracked = watched is not null;
            OnPropertyChanged(nameof(IsMetronTracked));
            _watchedSeriesId = watched?.Id ?? 0;
            WatchFutureReleases = watched?.WatchFutureReleases ?? false;

            MissingRows.Clear();
            if (watched is not null)
            {
                var today = DateTime.Today;
                foreach (var issue in WantedService.GetMissing(context, watched))
                {
                    MissingRows.Add(new MissingIssueRowViewModel
                    {
                        CatalogIssueId = issue.Id,
                        Number = issue.IssueNumber,
                        Title = string.IsNullOrWhiteSpace(issue.Name) ? $"{SeriesName} #{issue.IssueNumber}" : $"#{issue.IssueNumber} · {issue.Name}",
                        Subtitle = issue.StoreDate is DateTime date ? date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) : null,
                        IsUpcoming = issue.StoreDate is DateTime d && d.Date > today,
                        Cover = new RemoteCoverSource(issue.CoverImageUrl),
                    });
                }
            }
        }
        finally
        {
            _loading = false;
        }

        NotifyCounts();
    }

    partial void OnWatchFutureReleasesChanged(bool value)
    {
        if (_loading || _watchedSeriesId == 0)
        {
            return;
        }

        using var context = _createContext();
        WantedService.SetWatchFutureReleases(context, _watchedSeriesId, value);
        SetStatus(value ? "Following: new issues will be requested automatically." : "No longer following.", isError: false);
    }

    /// <summary>Marks one issue wanted. The row leaves the list one dispatcher tick later (its own button is still routing the click).</summary>
    [RelayCommand]
    private void Request(MissingIssueRowViewModel row) => _post(() =>
    {
        using (var context = _createContext())
        {
            var watched = context.WatchedSeries.FirstOrDefault(w => w.Id == _watchedSeriesId);
            var issue = context.CatalogIssues.FirstOrDefault(c => c.Id == row.CatalogIssueId);
            if (watched is not null && issue is not null)
            {
                WantedService.Request(context, watched, issue);
            }
        }

        Reload();
        SetStatus($"Requested #{row.Number}. It will be searched on the next check — or press Search now on the Wanted screen.", isError: false);
    });

    /// <summary>"I have this": removes an issue from Missing without searching for it.</summary>
    [RelayCommand]
    private void Ignore(MissingIssueRowViewModel row) => _post(() =>
    {
        using (var context = _createContext())
        {
            var watched = context.WatchedSeries.FirstOrDefault(w => w.Id == _watchedSeriesId);
            var issue = context.CatalogIssues.FirstOrDefault(c => c.Id == row.CatalogIssueId);
            if (watched is not null && issue is not null)
            {
                WantedService.Ignore(context, watched, issue);
            }
        }

        Reload();
    });

    /// <summary>First click of "Request all": asks "Request all N issues?" in place. Nothing is requested yet.</summary>
    [RelayCommand]
    private void RequestAll()
    {
        if (MissingRows.Count > 0)
        {
            IsConfirmingRequestAll = true;
        }
    }

    [RelayCommand]
    private void ConfirmRequestAll() => _post(() =>
    {
        int count;
        using (var context = _createContext())
        {
            var watched = context.WatchedSeries.FirstOrDefault(w => w.Id == _watchedSeriesId);
            count = watched is null ? 0 : WantedService.RequestAllMissing(context, watched);
        }

        IsConfirmingRequestAll = false;
        Reload();
        SetStatus($"Requested {count} issue{(count == 1 ? "" : "s")}.", isError: false);
    });

    [RelayCommand]
    private void CancelRequestAll() => IsConfirmingRequestAll = false;

    /// <summary>Re-reads this volume's issue list from ComicVine (one or more requests), then reloads.</summary>
    [RelayCommand]
    private async Task RefreshFromComicVineAsync(CancellationToken cancellationToken)
    {
        if (!TryGetProvider(out var provider))
        {
            return;
        }

        try
        {
            int volumeId;
            using (var context = _createContext())
            {
                volumeId = context.WatchedSeries.First(w => w.Id == _watchedSeriesId).ExternalVolumeId;
            }

            var issues = await _createProvider(provider).GetVolumeIssuesAsync(volumeId, cancellationToken);
            using (var context = _createContext())
            {
                WantedService.RefreshCatalog(context, context.WatchedSeries.First(w => w.Id == _watchedSeriesId), issues);
            }

            Reload();
            SetStatus($"Updated from {ProviderName}: {issues.Count} issues, {MissingRows.Count} missing.", isError: false);
        }
        catch (ComicVineException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>For a series that isn't tracked yet: searches ComicVine for its volume by name.</summary>
    [RelayCommand]
    private async Task FindOnComicVineAsync(CancellationToken cancellationToken)
    {
        if (!TryGetProvider(out var provider))
        {
            return;
        }

        var query = string.IsNullOrWhiteSpace(SearchText) ? SeriesName : SearchText.Trim();
        try
        {
            var hints = BuildHints(query);
            _ranked = WithCachedCovers(provider, await VolumeSearchService.SearchAndRankAsync(_createProvider(provider), query, hints, cancellationToken));
            ShownCount = Math.Min(VolumeResultViewModel.PageSize, _ranked.Count);
            RebuildResults();

            SearchSummary = _ranked.Count == 0
                ? string.Empty
                : $"Best of {_ranked.Count} {ProviderName} series, ranked by name, start year, issue count and publisher against what you have.";
            SetStatus(_ranked.Count == 0 ? $"No {ProviderName} series found for \"{query}\". Try a shorter or different name." : "Pick the right series below.", isError: _ranked.Count == 0);
        }
        catch (ComicVineException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>Reveals another page of the ranked results already fetched (no new request).</summary>
    [RelayCommand]
    private void ShowMore()
    {
        ShownCount = Math.Min(ShownCount + VolumeResultViewModel.PageSize * 2, _ranked.Count);
        RebuildResults();
    }

    /// <summary>Metron has no series covers, so its results borrow one from the weekly list's cache when the series is in it.</summary>
    private IReadOnlyList<RankedVolume> WithCachedCovers(ComicProvider provider, IReadOnlyList<RankedVolume> ranked)
    {
        if (provider != ComicProvider.Metron || ranked.Count == 0)
        {
            return ranked;
        }

        using var context = _createContext();
        var covers = PullListService.CachedCovers(context, ranked.Select(r => r.Volume.Id));
        return ranked.Select(r => covers.TryGetValue(r.Volume.Id, out var url) && string.IsNullOrEmpty(r.Volume.ImageUrl) ? r with { Volume = r.Volume with { ImageUrl = url } } : r).ToList();
    }

    private void RebuildResults()
    {
        SearchResults.Clear();
        foreach (var (ranked, index) in Sorted().Take(ShownCount).Select((r, i) => (r, i)))
        {
            SearchResults.Add(new VolumeResultViewModel
            {
                Volume = ranked.Volume,
                IsBestMatch = IsBestMatchSort && index == 0 && _ranked.Count > 1,
                Cover = new RemoteCoverSource(ranked.Volume.ImageUrl),
            });
        }

        OnPropertyChanged(nameof(HasSearchResults));
    }

    /// <summary>What the library knows about this series: its earliest year, highest whole issue number and publisher, so ranking can tell a 2016 series from a 1968 one.</summary>
    private LocalSeriesHints BuildHints(string query)
    {
        using var context = _createContext();
        var issues = context.Issues.Where(i => i.SeriesId == _seriesId && !i.IsPlaceholder).ToList();

        int? year = issues.Select(i => i.Year).Where(y => y is > 0).Min();
        int? highest = issues
            .Select(i => int.TryParse(i.Number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : (int?)null)
            .Where(n => n is > 0).Max();
        var publisher = issues.Select(i => i.Publisher).Where(p => !string.IsNullOrWhiteSpace(p))
            .GroupBy(p => p!, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

        return new LocalSeriesHints(query, year, highest, publisher);
    }

    /// <summary>Links this local series to the chosen ComicVine volume and caches its issue list. Tracking is not following.</summary>
    [RelayCommand]
    private async Task TrackAsync(VolumeResultViewModel result, CancellationToken cancellationToken)
    {
        if (!TryGetProvider(out var provider))
        {
            return;
        }

        try
        {
            var issues = await _createProvider(provider).GetVolumeIssuesAsync(result.Volume.Id, cancellationToken);

            using (var context = _createContext())
            {
                var watched = WantedService.TrackVolume(context, result.Volume, _seriesId, watchFutureReleases: false, provider);
                if (watched.SeriesId != _seriesId)
                {
                    // That volume is already tracked and linked to a different local series; keep that link, tell the user.
                    SetStatus($"{result.Title} is already tracked for another series in your library.", isError: true);
                    return;
                }

                WantedService.RefreshCatalog(context, watched, issues);
            }

            SearchResults.Clear();
            OnPropertyChanged(nameof(HasSearchResults));
            Reload();
            SetStatus($"Tracking {result.Title}: {MissingRows.Count} of {issues.Count} issues missing.", isError: false);
        }
        catch (ComicVineException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private bool TryGetProvider(out ComicProvider provider)
    {
        provider = SelectedProvider;
        using var context = _createContext();
        if (!ComicProviderFactory.IsAvailable(context, provider))
        {
            SetStatus(ComicProviderFactory.MissingCredentialsMessage(provider).Replace("first.", "to find missing issues."), isError: true);
            return false;
        }

        return true;
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(HasMissing));
        OnPropertyChanged(nameof(IsAllCaughtUp));
        OnPropertyChanged(nameof(IsNotTracked));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(ConfirmText));
    }

    partial void OnIsTrackedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotTracked));
        OnPropertyChanged(nameof(IsAllCaughtUp));
    }

    private void SetStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }
}
