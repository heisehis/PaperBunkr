using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Daemon.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

public enum WantedTab
{
    Queue,
    Series,
    Releases,
}

/// <summary>
/// The Wanted screen (docs/superpowers/specs/2026-09-21-wanted-screen-redesign-design.md): three tabs - Queue (everything wanted, upcoming, downloading or failed, grouped by
/// series), Series (what is tracked and followed) and Releases (the weekly pull list) - and "Search now". Split by tab: this file is the shell and the shared plumbing,
/// <c>.Queue.cs</c>, <c>.Series.cs</c> and <c>.Releases.cs</c> hold each tab.
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
    private readonly Action<string, bool> _notify;
    private readonly Func<string, string, Task<bool>> _confirm;
    private readonly Func<DateTime, DateTime, CancellationToken, Task<bool>> _fetchReleases;

    /// <param name="notify">Shows a transient result as a toast (message, isError). Nothing is kept on the screen itself.</param>
    /// <param name="confirm">Asks before a bulk or destructive action (message, confirm label); defaults to yes.</param>
    /// <param name="fetchReleases">Fetches the pull-list releases of a date range the cache doesn't cover, and stores them; false when there is no source.</param>
    public WantedScreenViewModel(
        Func<PaperbunkrDbContext> createContext,
        Func<CancellationToken, Task> searchNow,
        Action<int> openSeries,
        Action openAcquisitionSettings,
        Func<string, Task> copyToClipboard,
        GrabService grab,
        Func<ComicProvider, IComicVineClient>? createProvider = null,
        Action<Action>? post = null,
        Func<DateTime>? today = null,
        Action<string, bool>? notify = null,
        Func<string, string, Task<bool>>? confirm = null,
        Func<DateTime, DateTime, CancellationToken, Task<bool>>? fetchReleases = null)
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
        _notify = notify ?? ((_, _) => { });
        _confirm = confirm ?? ((_, _) => Task.FromResult(true));
        _fetchReleases = fetchReleases ?? ((_, _, _) => Task.FromResult(false));
        _releaseWeekStart = WeekStart(_today());
        _calendarMonth = new DateTime(_releaseWeekStart.AddDays(3).Year, _releaseWeekStart.AddDays(3).Month, 1);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueueTab), nameof(IsSeriesTab), nameof(IsReleasesTab))]
    private WantedTab _activeTab = WantedTab.Queue;

    public bool IsQueueTab => ActiveTab == WantedTab.Queue;
    public bool IsSeriesTab => ActiveTab == WantedTab.Series;
    public bool IsReleasesTab => ActiveTab == WantedTab.Releases;

    [RelayCommand] private void GoQueue() => ActiveTab = WantedTab.Queue;
    [RelayCommand] private void GoSeries() => ActiveTab = WantedTab.Series;
    [RelayCommand] private void GoReleases() => ActiveTab = WantedTab.Releases;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _hasComicVineKey;

    /// <summary>qBittorrent is set up, so candidates can be grabbed (otherwise they can only be copied).</summary>
    [ObservableProperty] private bool _hasDownloadClient;
    [ObservableProperty] private bool _isSearching;

    /// <summary>The persistent problem worth a banner: automatic searching is off, so nothing here moves on its own. Dismissed for the session.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSearchOffBanner))]
    private bool _searchOffBannerDismissed;

    public bool ShowSearchOffBanner => !IsEnabled && !SearchOffBannerDismissed;

    partial void OnIsEnabledChanged(bool value) => OnPropertyChanged(nameof(ShowSearchOffBanner));

    [RelayCommand] private void DismissSearchOffBanner() => SearchOffBannerDismissed = true;

    /// <summary>Reloads every tab from the database. Cheap: a handful of small queries; the Queue reconciles in place so a reload never moves what the user is looking at.</summary>
    public void Refresh()
    {
        using var context = _createContext();
        var today = _today();

        IsEnabled = context.GetOrCreateAcquisitionSettings().Enabled;
        HasComicVineKey = !string.IsNullOrEmpty(CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey));
        OnPropertyChanged(nameof(HasSeriesProviderCredentials));
        HasDownloadClient = !string.IsNullOrWhiteSpace(context.GetOrCreateAcquisitionSettings().QBittorrentUrl);

        LoadQueue(context, today);
        LoadSeries(context);
        LoadReleases(context, today);
        OnPropertyChanged(nameof(HasNoReleases));
    }

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

    [RelayCommand]
    private void OpenAcquisitionSettings() => _openAcquisitionSettings();

    /// <summary>Transient results are toasts (Activity Center's rule for feedback); nothing lingers on the screen.</summary>
    private void Notify(string message, bool isError)
    {
        if (!string.IsNullOrEmpty(message))
        {
            _notify(message, isError);
        }
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    /// <summary>
    /// Makes <paramref name="target"/> match <paramref name="desired"/> with the fewest changes - removing what is gone, moving and inserting what is new - so items that
    /// stay keep their place and their realized visuals (a reload that clears and refills would rebuild every row and lose the scroll position).
    /// </summary>
    private static void SyncList<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        var keep = new HashSet<T>(desired);
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (int i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i]))
            {
                continue;
            }

            int at = -1;
            for (int j = i + 1; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], desired[i]))
                {
                    at = j;
                    break;
                }
            }

            if (at >= 0)
            {
                target.Move(at, i);
            }
            else
            {
                target.Insert(i, desired[i]);
            }
        }
    }
}
