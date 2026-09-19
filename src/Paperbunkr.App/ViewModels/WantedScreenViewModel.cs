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
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

public enum WantedTab
{
    Wanted,
    Upcoming,
    Candidates,
    Series,
}

/// <summary>One wanted or upcoming issue.</summary>
public sealed class WantedRowViewModel
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string StatusText { get; init; }
    public int CandidateCount { get; init; }
    public bool HasCandidates => CandidateCount > 0;
    public string CandidateText => CandidateCount == 1 ? "1 candidate" : $"{CandidateCount} candidates";
}

/// <summary>One release found for a wanted issue.</summary>
public sealed class CandidateRowViewModel
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string ScoreText { get; init; }
    public bool IsPack { get; init; }
    public required string DownloadUrl { get; init; }
}

public sealed class CandidateGroupViewModel
{
    public required string Title { get; init; }
    public required IReadOnlyList<CandidateRowViewModel> Candidates { get; init; }
}

/// <summary>A tracked ComicVine volume on the Series tab.</summary>
public sealed partial class WatchedSeriesRowViewModel : ObservableObject
{
    public WatchedSeriesRowViewModel(bool watchFutureReleases) => _watchFutureReleases = watchFutureReleases;

    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? Subtitle { get; init; }
    public int? SeriesId { get; init; }
    public bool CanOpen => SeriesId is not null;
    public int MissingCount { get; init; }
    public int WantedCount { get; init; }
    public string CountsText => $"{MissingCount} missing · {WantedCount} wanted";

    /// <summary>"Follow": request future issues automatically. The initial value is set through the field, so loading never fires <see cref="FollowChanged"/>.</summary>
    [ObservableProperty]
    private bool _watchFutureReleases;

    public event Action<WatchedSeriesRowViewModel>? FollowChanged;

    partial void OnWatchFutureReleasesChanged(bool value) => FollowChanged?.Invoke(this);
}

/// <summary>A ComicVine volume found by the "track a series" search.</summary>
public sealed class VolumeResultViewModel
{
    public required ComicVineVolume Volume { get; init; }
    public string Title => Volume.StartYear is int year ? $"{Volume.Name} ({year})" : Volume.Name;
    public string Subtitle => $"{Volume.Publisher ?? "Unknown publisher"} · {Volume.CountOfIssues} issues";
    public bool IsTracked { get; init; }
}

/// <summary>
/// The Wanted screen (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §7, layout A): stage tabs Wanted / Upcoming /
/// Candidates / Series, and "Search now". Slice 1 is search-only, so Candidates is a review list (copy a link to use in any client);
/// approving a candidate for download arrives with the download client in slice 2.
/// </summary>
public sealed partial class WantedScreenViewModel : ViewModelBase
{
    private readonly Func<PaperbunkrDbContext> _createContext;
    private readonly Func<CancellationToken, Task> _searchNow;
    private readonly Action<int> _openSeries;
    private readonly Action _openAcquisitionSettings;
    private readonly Func<string, IComicVineClient> _createComicVine;
    private readonly Func<string, Task> _copyToClipboard;
    private readonly Action<Action> _post;
    private readonly Func<DateTime> _today;

    public WantedScreenViewModel(
        Func<PaperbunkrDbContext> createContext,
        Func<CancellationToken, Task> searchNow,
        Action<int> openSeries,
        Action openAcquisitionSettings,
        Func<string, Task> copyToClipboard,
        Func<string, IComicVineClient>? createComicVine = null,
        Action<Action>? post = null,
        Func<DateTime>? today = null)
    {
        _createContext = createContext;
        _searchNow = searchNow;
        _openSeries = openSeries;
        _openAcquisitionSettings = openAcquisitionSettings;
        _copyToClipboard = copyToClipboard;
        _createComicVine = createComicVine ?? (key => new ComicVineClient(key, ComicVineRequestPriority.High));
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _today = today ?? (() => DateTime.Today);
    }

    public ObservableCollection<WantedRowViewModel> WantedRows { get; } = new();
    public ObservableCollection<WantedRowViewModel> UpcomingRows { get; } = new();
    public ObservableCollection<CandidateGroupViewModel> CandidateGroups { get; } = new();
    public ObservableCollection<WatchedSeriesRowViewModel> SeriesRows { get; } = new();
    public ObservableCollection<VolumeResultViewModel> SearchResults { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWantedTab), nameof(IsUpcomingTab), nameof(IsCandidatesTab), nameof(IsSeriesTab))]
    private WantedTab _activeTab = WantedTab.Wanted;

    public bool IsWantedTab => ActiveTab == WantedTab.Wanted;
    public bool IsUpcomingTab => ActiveTab == WantedTab.Upcoming;
    public bool IsCandidatesTab => ActiveTab == WantedTab.Candidates;
    public bool IsSeriesTab => ActiveTab == WantedTab.Series;

    public bool HasNoWanted => WantedRows.Count == 0;
    public bool HasNoUpcoming => UpcomingRows.Count == 0;
    public bool HasNoCandidates => CandidateGroups.Count == 0;
    public bool HasNoSeries => SeriesRows.Count == 0;
    public int CandidateCount => CandidateGroups.Sum(g => g.Candidates.Count);

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _hasComicVineKey;
    [ObservableProperty] private bool _isSearching;

    [ObservableProperty]
    private string _seriesSearchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus), nameof(HasErrorStatus), nameof(HasInfoStatus))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorStatus), nameof(HasInfoStatus))]
    private bool _statusIsError;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Two flags, because a plain binding can't combine "has a message" with "is / isn't an error".</summary>
    public bool HasErrorStatus => !string.IsNullOrEmpty(StatusMessage) && StatusIsError;

    public bool HasInfoStatus => !string.IsNullOrEmpty(StatusMessage) && !StatusIsError;
    public bool HasSearchResults => SearchResults.Count > 0;

    /// <summary>Reloads every tab from the database. Cheap: a handful of small queries.</summary>
    public void Refresh()
    {
        using var context = _createContext();
        var today = _today();

        IsEnabled = context.GetOrCreateAcquisitionSettings().Enabled;
        HasComicVineKey = !string.IsNullOrEmpty(CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey));

        var candidateCounts = context.ReleaseCandidates
            .GroupBy(c => c.WantedIssueId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionary(x => x.Key, x => x.Count);

        Fill(WantedRows, WantedService.Due(context, today).Include(w => w.WatchedSeries).OrderBy(w => w.StoreDate ?? DateTime.MinValue).ThenBy(w => w.CreatedAt).ToList()
            .Select(w => ToRow(w, candidateCounts, upcoming: false)));
        Fill(UpcomingRows, WantedService.Upcoming(context, today).Include(w => w.WatchedSeries).OrderBy(w => w.StoreDate).ToList()
            .Select(w => ToRow(w, candidateCounts, upcoming: true)));

        var groups = context.WantedIssues
            .Include(w => w.WatchedSeries)
            .Include(w => w.Candidates)
            .Where(w => w.Candidates.Any())
            .OrderBy(w => w.WatchedSeries!.Name).ThenBy(w => w.IssueNumber)
            .ToList()
            .Select(w => new CandidateGroupViewModel
            {
                Title = $"{w.WatchedSeries?.Name} #{w.IssueNumber}",
                Candidates = w.Candidates.OrderByDescending(c => c.Score).Select(ToCandidateRow).ToList(),
            });
        Fill(CandidateGroups, groups);

        var series = context.WatchedSeries.OrderBy(w => w.Name).ToList();
        var rows = new List<WatchedSeriesRowViewModel>();
        foreach (var watched in series)
        {
            var row = new WatchedSeriesRowViewModel(watched.WatchFutureReleases)
            {
                Id = watched.Id,
                Name = watched.Name,
                Subtitle = string.Join(" · ", new[] { watched.Publisher, watched.StartYear?.ToString(CultureInfo.InvariantCulture) }.Where(s => !string.IsNullOrEmpty(s))),
                SeriesId = watched.SeriesId,
                MissingCount = WantedService.GetMissing(context, watched).Count,
                WantedCount = context.WantedIssues.Count(w => w.WatchedSeriesId == watched.Id && w.Status == WantedIssueStatus.Wanted),
            };
            row.FollowChanged += OnFollowChanged;
            rows.Add(row);
        }

        foreach (var old in SeriesRows)
        {
            old.FollowChanged -= OnFollowChanged;
        }

        Fill(SeriesRows, rows);

        OnPropertyChanged(nameof(HasNoWanted));
        OnPropertyChanged(nameof(HasNoUpcoming));
        OnPropertyChanged(nameof(HasNoCandidates));
        OnPropertyChanged(nameof(HasNoSeries));
        OnPropertyChanged(nameof(CandidateCount));
    }

    [RelayCommand] private void GoWanted() => ActiveTab = WantedTab.Wanted;
    [RelayCommand] private void GoUpcoming() => ActiveTab = WantedTab.Upcoming;
    [RelayCommand] private void GoCandidates() => ActiveTab = WantedTab.Candidates;
    [RelayCommand] private void GoSeries() => ActiveTab = WantedTab.Series;

    [RelayCommand]
    private async Task SearchNowAsync(CancellationToken cancellationToken)
    {
        IsSearching = true;
        try
        {
            await _searchNow(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // app closing
        }
        finally
        {
            IsSearching = false;
            Refresh();
        }
    }

    /// <summary>"I have this": stops wanting it without ever searching. Deferred a tick - the row's own button is still routing its click.</summary>
    [RelayCommand]
    private void Ignore(WantedRowViewModel row) => _post(() =>
    {
        using var context = _createContext();
        var wanted = context.WantedIssues.FirstOrDefault(w => w.Id == row.Id);
        if (wanted is not null)
        {
            wanted.Status = WantedIssueStatus.Ignored;
            context.SaveChanges();
        }

        Refresh();
    });

    /// <summary>Stops wanting an issue; it goes back to the series' "Missing" list. Deferred a tick, like <see cref="Ignore"/>.</summary>
    [RelayCommand]
    private void Remove(WantedRowViewModel row) => _post(() =>
    {
        using var context = _createContext();
        var wanted = context.WantedIssues.FirstOrDefault(w => w.Id == row.Id);
        if (wanted is not null)
        {
            context.WantedIssues.Remove(wanted);
            context.SaveChanges();
        }

        Refresh();
    });

    [RelayCommand]
    private async Task CopyLinkAsync(CandidateRowViewModel candidate)
    {
        await _copyToClipboard(candidate.DownloadUrl);
        SetStatus("Link copied. Paste it into your download client.", isError: false);
    }

    [RelayCommand]
    private void OpenAcquisitionSettings() => _openAcquisitionSettings();

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

        string? key;
        HashSet<int> tracked;
        using (var context = _createContext())
        {
            key = CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey);
            tracked = context.WatchedSeries.Select(w => w.ComicVineVolumeId).ToHashSet();
        }

        if (string.IsNullOrEmpty(key))
        {
            SetStatus("Add your ComicVine API key under Preferences → Connections to search for series.", isError: true);
            return;
        }

        try
        {
            var volumes = await _createComicVine(key).SearchVolumesAsync(query, cancellationToken);
            Fill(SearchResults, volumes.Select(v => new VolumeResultViewModel { Volume = v, IsTracked = tracked.Contains(v.Id) }));
            OnPropertyChanged(nameof(HasSearchResults));
            SetStatus(volumes.Count == 0 ? $"No ComicVine series found for \"{query}\"." : string.Empty, isError: false);
        }
        catch (ComicVineException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>Starts tracking a volume (not following it) and caches its issue list so its missing issues can be shown.</summary>
    [RelayCommand]
    private async Task TrackVolumeAsync(VolumeResultViewModel result, CancellationToken cancellationToken)
    {
        string? key;
        using (var context = _createContext())
        {
            key = CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey);
        }

        if (string.IsNullOrEmpty(key))
        {
            SetStatus("Add your ComicVine API key under Preferences → Connections first.", isError: true);
            return;
        }

        try
        {
            var issues = await _createComicVine(key).GetVolumeIssuesAsync(result.Volume.Id, cancellationToken);

            using var context = _createContext();
            int? seriesId = context.Series.AsEnumerable().FirstOrDefault(s => SeriesNames.Same(s.Name, result.Volume.Name))?.Id;
            var watched = WantedService.TrackVolume(context, result.Volume, seriesId, watchFutureReleases: false);
            WantedService.RefreshCatalog(context, watched, issues);
            int missing = WantedService.GetMissing(context, watched).Count;

            Refresh();
            Fill(SearchResults, SearchResults.Select(r => new VolumeResultViewModel { Volume = r.Volume, IsTracked = r.IsTracked || r.Volume.Id == result.Volume.Id }));
            SetStatus($"Tracking {result.Title}: {missing} of {issues.Count} issues missing.", isError: false);
        }
        catch (ComicVineException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private void OnFollowChanged(WatchedSeriesRowViewModel row)
    {
        using var context = _createContext();
        WantedService.SetWatchFutureReleases(context, row.Id, row.WatchFutureReleases);
        SetStatus(row.WatchFutureReleases
            ? $"Following {row.Name}: new issues will be requested automatically."
            : $"No longer following {row.Name}.", isError: false);
    }

    private WantedRowViewModel ToRow(WantedIssue wanted, IReadOnlyDictionary<int, int> candidateCounts, bool upcoming) => new()
    {
        Id = wanted.Id,
        Title = $"{wanted.WatchedSeries?.Name} #{wanted.IssueNumber}",
        Subtitle = upcoming && wanted.StoreDate is DateTime due
            ? $"Arrives {due.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}"
            : string.Join(" · ", new[] { wanted.WatchedSeries?.Publisher, wanted.Name }.Where(s => !string.IsNullOrEmpty(s))),
        StatusText = upcoming ? "Upcoming" : wanted.LastSearchedAt is null ? "Not searched yet" : "Wanted",
        CandidateCount = candidateCounts.TryGetValue(wanted.Id, out int count) ? count : 0,
    };

    private static CandidateRowViewModel ToCandidateRow(ReleaseCandidate candidate) => new()
    {
        Title = candidate.Title,
        Detail = string.Join(" · ", new[]
        {
            FormatSize(candidate.SizeBytes),
            $"{candidate.Seeders} seeders",
            candidate.Indexer,
        }.Where(s => !string.IsNullOrEmpty(s))),
        ScoreText = candidate.Score.ToString("0", CultureInfo.InvariantCulture),
        IsPack = candidate.IsPack,
        DownloadUrl = candidate.DownloadUrl,
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        <= 0 => string.Empty,
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
    };

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }
}
