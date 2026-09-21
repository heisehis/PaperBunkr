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
using Paperbunkr.Daemon.Services;
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
    Releases,
}

/// <summary>One wanted or upcoming issue.</summary>
public sealed class WantedRowViewModel
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string StatusText { get; init; }
    public RemoteCoverSource Cover { get; init; } = new(null);
    public bool IsMetron { get; init; }
    public int CandidateCount { get; init; }
    public bool HasCandidates => CandidateCount > 0;
    public string CandidateText => CandidateCount == 1 ? "1 candidate" : $"{CandidateCount} candidates";
}

/// <summary>One release found for a wanted issue.</summary>
public sealed class CandidateRowViewModel
{
    public required int Id { get; init; }
    public required int WantedIssueId { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string ScoreText { get; init; }
    public bool IsPack { get; init; }
    public required string DownloadUrl { get; init; }
}

/// <summary>An imported issue whose ComicVine details could not be added (a failed scrape-on-import); the row stays until the user retries or dismisses it.</summary>
public sealed class ScrapeReviewRowViewModel
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public required string Reason { get; init; }

    /// <summary>Retrying without the user changing something is pointless (no such issue upstream, no key).</summary>
    public bool NeedsYourAction { get; init; }
    public string Hint => NeedsYourAction ? "Needs your attention" : "Paperbunkr will retry on its own";
    public RemoteCoverSource Cover { get; init; } = new(null);
}

/// <summary>An issue that has been sent to the download client: downloading, or failed and waiting for the user.</summary>
public sealed class DownloadRowViewModel
{
    public required int Id { get; init; }
    public RemoteCoverSource Cover { get; init; } = new(null);
    public required string Title { get; init; }
    public required string StatusText { get; init; }
    public string? Detail { get; init; }

    /// <summary>0..100 for the progress bar.</summary>
    public double ProgressPercent { get; init; }

    public bool IsFailed { get; init; }
    public bool ShowProgress => !IsFailed;
    public string ProgressText => $"{ProgressPercent:0}%";
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
    public bool IsMetron { get; init; }
    public RemoteCoverSource Cover { get; init; } = new(null);
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

    /// <summary>The top-ranked result, worth a nudge when the list is long.</summary>
    public bool IsBestMatch { get; init; }

    public RemoteCoverSource Cover { get; init; } = new(null);

    /// <summary>How many ranked results a picker shows at first.</summary>
    public const int PageSize = 15;
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
    private readonly Func<ComicProvider, IComicVineClient> _createProvider;
    private readonly Func<string, Task> _copyToClipboard;
    private readonly GrabService _grab;
    private readonly Action<Action> _post;
    private readonly Func<DateTime> _today;

    public WantedScreenViewModel(
        Func<PaperbunkrDbContext> createContext,
        Func<CancellationToken, Task> searchNow,
        Action<int> openSeries,
        Action openAcquisitionSettings,
        Func<string, Task> copyToClipboard,
        GrabService grab,
        Func<ComicProvider, IComicVineClient>? createProvider = null,
        Action<Action>? post = null,
        Func<DateTime>? today = null)
    {
        _createContext = createContext;
        _searchNow = searchNow;
        _openSeries = openSeries;
        _openAcquisitionSettings = openAcquisitionSettings;
        _copyToClipboard = copyToClipboard;
        _grab = grab;
        _createProvider = createProvider ?? (provider =>
        {
            using var context = createContext();
            return ComicProviderFactory.Create(context, provider)!;
        });
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _today = today ?? (() => DateTime.Today);
    }

    public ObservableCollection<WantedRowViewModel> WantedRows { get; } = new();
    public ObservableCollection<WantedRowViewModel> UpcomingRows { get; } = new();
    public ObservableCollection<DownloadRowViewModel> DownloadRows { get; } = new();

    /// <summary>Imported issues still missing their details (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md 6.2).</summary>
    public ObservableCollection<ScrapeReviewRowViewModel> ScrapeReviewRows { get; } = new();

    public bool HasScrapeReview => ScrapeReviewRows.Count > 0;

    public string ScrapeReviewHeading => ScrapeReviewRows.Count == 1 ? "NEEDS ATTENTION · 1 issue is missing its details" : $"NEEDS ATTENTION · {ScrapeReviewRows.Count} issues are missing their details";
    public ObservableCollection<CandidateGroupViewModel> CandidateGroups { get; } = new();
    public ObservableCollection<WatchedSeriesRowViewModel> SeriesRows { get; } = new();
    public ObservableCollection<VolumeResultViewModel> SearchResults { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWantedTab), nameof(IsUpcomingTab), nameof(IsCandidatesTab), nameof(IsSeriesTab), nameof(IsReleasesTab))]
    private WantedTab _activeTab = WantedTab.Wanted;

    public bool IsWantedTab => ActiveTab == WantedTab.Wanted;
    public bool IsUpcomingTab => ActiveTab == WantedTab.Upcoming;
    public bool IsCandidatesTab => ActiveTab == WantedTab.Candidates;
    public bool IsSeriesTab => ActiveTab == WantedTab.Series;
    public bool IsReleasesTab => ActiveTab == WantedTab.Releases;

    public bool HasNoWanted => WantedRows.Count == 0 && DownloadRows.Count == 0;
    public bool HasNoDownloads => DownloadRows.Count == 0;

    /// <summary>What the Wanted tab counts: issues still to find plus those already on their way.</summary>
    public int WantedTabCount => WantedRows.Count + DownloadRows.Count;
    public bool HasNoUpcoming => UpcomingRows.Count == 0;
    public bool HasNoCandidates => CandidateGroups.Count == 0;
    public bool HasNoSeries => SeriesRows.Count == 0;
    public int CandidateCount => CandidateGroups.Sum(g => g.Candidates.Count);

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _hasComicVineKey;

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

    /// <summary>qBittorrent is set up, so candidates can be grabbed (otherwise they can only be copied).</summary>
    [ObservableProperty] private bool _hasDownloadClient;
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
        OnPropertyChanged(nameof(HasSeriesProviderCredentials));

        HasDownloadClient = !string.IsNullOrWhiteSpace(context.GetOrCreateAcquisitionSettings().QBittorrentUrl);

        Fill(DownloadRows, context.WantedIssues.Include(w => w.WatchedSeries)
            .Where(w => w.Status == WantedIssueStatus.Snatched || w.Status == WantedIssueStatus.Downloading || w.Status == WantedIssueStatus.Failed)
            .OrderBy(w => w.Status == WantedIssueStatus.Failed ? 0 : 1).ThenBy(w => w.CreatedAt)
            .ToList().Select(ToDownloadRow));

        Fill(ScrapeReviewRows, context.WantedIssues.Include(w => w.WatchedSeries)
            .Where(w => w.ScrapeStatus == ScrapeStatus.Failed)
            .OrderBy(w => w.ScrapeLastAttemptAt)
            .ToList().Select(w => new ScrapeReviewRowViewModel
            {
                Id = w.Id,
                Title = $"{w.WatchedSeries?.Name} #{w.IssueNumber}",
                Reason = w.ScrapeError ?? "Couldn't add details.",
                NeedsYourAction = w.ScrapeFailureIsTerminal,
                Cover = CoverFor(w),
            }));

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
                IsMetron = watched.Provider == ComicProvider.Metron,
                Cover = new RemoteCoverSource(watched.CoverImageUrl),
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
        LoadReleases(context, today);
        OnPropertyChanged(nameof(HasNoReleases));

        OnPropertyChanged(nameof(HasNoWanted));
        OnPropertyChanged(nameof(HasNoDownloads));
        OnPropertyChanged(nameof(HasScrapeReview));
        OnPropertyChanged(nameof(ScrapeReviewHeading));
        OnPropertyChanged(nameof(WantedTabCount));
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

    /// <summary>Tries a failed scrape again now. Deferred a tick: the row's own button is still routing its click, and the row is about to leave the list.</summary>
    [RelayCommand]
    private void RetryScrape(ScrapeReviewRowViewModel row) => _post(() =>
    {
        using (var context = _createContext())
        {
            Paperbunkr.Daemon.Services.ScrapeSweeper.Requeue(context, row.Id);
        }

        Refresh();
    });

    /// <summary>Gives up on one issue's ComicVine details; the issue stays in the library as it is.</summary>
    [RelayCommand]
    private void DismissScrape(ScrapeReviewRowViewModel row) => _post(() =>
    {
        using (var context = _createContext())
        {
            Paperbunkr.Daemon.Services.ScrapeSweeper.Dismiss(context, row.Id);
        }

        Refresh();
    });

    /// <summary>One click for a whole failed batch (a bad key fixed, ComicVine back up): every failed issue goes back in the queue.</summary>
    [RelayCommand]
    private void RetryAllScrapes() => _post(() =>
    {
        int count;
        using (var context = _createContext())
        {
            var ids = context.WantedIssues.Where(w => w.ScrapeStatus == ScrapeStatus.Failed).Select(w => w.Id).ToList();
            count = ids.Count(id => Paperbunkr.Daemon.Services.ScrapeSweeper.Requeue(context, id));
        }

        SetStatus(count == 0 ? string.Empty : $"Trying again for {count} issue{(count == 1 ? string.Empty : "s")}.", isError: false);
        Refresh();
    });

    [RelayCommand]
    private void DismissAllScrapes() => _post(() =>
    {
        using (var context = _createContext())
        {
            foreach (var id in context.WantedIssues.Where(w => w.ScrapeStatus == ScrapeStatus.Failed).Select(w => w.Id).ToList())
            {
                Paperbunkr.Daemon.Services.ScrapeSweeper.Dismiss(context, id);
            }
        }

        Refresh();
    });

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

    /// <summary>Approves a candidate: sends it to qBittorrent. Progress and the import then follow on their own.</summary>
    [RelayCommand]
    private async Task GrabAsync(CandidateRowViewModel candidate, CancellationToken cancellationToken)
    {
        var result = await _grab.GrabAsync(candidate.Id, automatic: false, cancellationToken);
        SetStatus(result.Message, isError: !result.Success);
        Refresh();
    }

    /// <summary>Rejects a candidate: it is blocklisted and never offered again. Deferred a tick - its own button is still routing the click.</summary>
    [RelayCommand]
    private void RejectCandidate(CandidateRowViewModel candidate) => _post(() =>
    {
        _grab.Reject(candidate.Id);
        Refresh();
    });

    /// <summary>Puts a failed issue back to Wanted for another search.</summary>
    [RelayCommand]
    private void RetryDownload(DownloadRowViewModel row) => _post(() =>
    {
        _grab.Retry(row.Id);
        Refresh();
    });

    /// <summary>Stops a grab (removing its torrent, only ever one in the Paperbunkr category) and returns the issue to Wanted.</summary>
    [RelayCommand]
    private async Task CancelDownloadAsync(DownloadRowViewModel row, CancellationToken cancellationToken)
    {
        var result = await _grab.CancelAsync(row.Id, deleteFiles: true, cancellationToken);
        SetStatus(result.Message, isError: !result.Success);
        Refresh();
    }

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

        var provider = ComicProviderFactory.Parse(SeriesProviderText);
        HashSet<int> tracked;
        using (var context = _createContext())
        {
            if (!ComicProviderFactory.IsAvailable(context, provider))
            {
                SetStatus(ComicProviderFactory.MissingCredentialsMessage(provider).Replace("first.", "to search for series."), isError: true);
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
            SetStatus(volumes.Count == 0 ? $"No {ComicProviderFactory.DisplayName(provider)} series found for \"{query}\"." : string.Empty, isError: false);
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
        // Results are only ever shown for the source that was searched, so the selector still names the right one.
        var provider = ComicProviderFactory.Parse(SeriesProviderText);
        using (var check = _createContext())
        {
            if (!ComicProviderFactory.IsAvailable(check, provider))
            {
                SetStatus(ComicProviderFactory.MissingCredentialsMessage(provider), isError: true);
                return;
            }
        }

        try
        {
            var issues = await _createProvider(provider).GetVolumeIssuesAsync(result.Volume.Id, cancellationToken);

            using var context = _createContext();
            int? seriesId = context.Series.AsEnumerable().FirstOrDefault(s => SeriesNames.Same(s.Name, result.Volume.Name))?.Id;
            var watched = WantedService.TrackVolume(context, result.Volume, seriesId, watchFutureReleases: false, provider);
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
        IsMetron = wanted.Provider == ComicProvider.Metron,
        CandidateCount = candidateCounts.TryGetValue(wanted.Id, out int count) ? count : 0,
        Cover = CoverFor(wanted),
    };

    private static RemoteCoverSource CoverFor(WantedIssue wanted) => new(wanted.CoverImageUrl ?? wanted.WatchedSeries?.CoverImageUrl);

    private static DownloadRowViewModel ToDownloadRow(WantedIssue wanted) => new()
    {
        Id = wanted.Id,
        Cover = CoverFor(wanted),
        Title = $"{wanted.WatchedSeries?.Name} #{wanted.IssueNumber}",
        IsFailed = wanted.Status == WantedIssueStatus.Failed,
        StatusText = wanted.Status switch
        {
            WantedIssueStatus.Failed => "Failed",
            WantedIssueStatus.Snatched => "Sent to qBittorrent",
            _ => wanted.DownloadProgress is >= 0.9999 ? "Importing…" : "Downloading",
        },
        Detail = wanted.Status == WantedIssueStatus.Failed ? wanted.FailureReason : wanted.GrabbedTitle,
        ProgressPercent = (wanted.DownloadProgress ?? 0) * 100,
    };

    private static CandidateRowViewModel ToCandidateRow(ReleaseCandidate candidate) => new()
    {
        Id = candidate.Id,
        WantedIssueId = candidate.WantedIssueId,
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
