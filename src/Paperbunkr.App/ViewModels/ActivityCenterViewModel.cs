using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

public enum ActivityDrawerTab
{
    Active,
    History,
    Scheduled,
}

/// <summary>One row in the drawer's read-only "Scheduled" tab.</summary>
public sealed record UpcomingTaskRow(string DisplayName, string NextRunLabel);

/// <summary>
/// Backs both tiers of the Activity Center (docs/superpowers/specs/2026-09-03-activity-center-
/// design.md): the peek popover (<see cref="IsPeekOpen"/>) and the full drawer
/// (<see cref="IsDrawerOpen"/>). Projects <see cref="IActivityService"/>'s collections into
/// display-ready lists and owns the History tab's paged DB query.
/// </summary>
public sealed partial class ActivityCenterViewModel : ViewModelBase
{
    private const int HistoryPageSize = 40;

    private readonly IActivityService _activity;
    private readonly Action<ActivityLink> _followLink;
    private readonly Action<Action> _dispatch;
    private int _historyLoaded;
    private CancellationTokenSource? _historyCts;

    /// <param name="dispatch">Marshals the projection rebuild onto the UI thread. Defaults to a <c>Dispatcher.UIThread</c>-aware post; tests pass <c>a =&gt; a()</c>.</param>
    private Services.Scheduling.ISchedulerService? _scheduler;

    public ActivityCenterViewModel(IActivityService activity, Action<ActivityLink> followLink, Services.Scheduling.ISchedulerService? scheduler = null, Action<Action>? dispatch = null)
    {
        _activity = activity;
        _followLink = followLink;
        _scheduler = scheduler;
        _dispatch = dispatch ?? (a =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                a();
            }
            else
            {
                Dispatcher.UIThread.Post(a);
            }
        });
        _activity.Changed += (_, _) => _dispatch(RebuildProjections);
        RebuildProjections();
        if (scheduler is not null)
        {
            AttachScheduler(scheduler);
        }
    }

    /// <summary>The scheduler's enabled tasks, soonest-first - the read-only "Scheduled" drawer tab.</summary>
    public ObservableCollection<UpcomingTaskRow> UpcomingTasks { get; } = new();

    public void AttachScheduler(Services.Scheduling.ISchedulerService scheduler)
    {
        _scheduler = scheduler;
        scheduler.Changed += (_, _) => _dispatch(RebuildUpcoming);
        RebuildUpcoming();
    }

    private void RebuildUpcoming()
    {
        if (_scheduler is null)
        {
            return;
        }

        var rows = _scheduler.Tasks
            .Where(t => t.Enabled)
            .OrderBy(t => t.IsRunning ? 0 : t.IsQueued ? 1 : 2)
            .ThenBy(t => t.NextRunLabel)
            .Select(t => new UpcomingTaskRow(t.DisplayName, t.NextRunLabel))
            .ToList();

        UpcomingTasks.Clear();
        foreach (var r in rows)
        {
            UpcomingTasks.Add(r);
        }

        OnPropertyChanged(nameof(HasUpcoming));
    }

    public bool HasUpcoming => UpcomingTasks.Count > 0;

    public bool IsScheduled => ActiveTab == ActivityDrawerTab.Scheduled;

    [RelayCommand]
    private void ShowScheduledTab() => ActiveTab = ActivityDrawerTab.Scheduled;

    [RelayCommand]
    private void ManageScheduledTasks()
    {
        IsDrawerOpen = false;
        _followLink(new ActivityLink(ActivityLinkKind.Preferences, "Automation"));
    }

    // ---- open state ----

    [ObservableProperty]
    private bool _isPeekOpen;

    [ObservableProperty]
    private bool _isDrawerOpen;

    [ObservableProperty]
    private ActivityDrawerTab _activeTab = ActivityDrawerTab.Active;

    public bool IsOpen => IsPeekOpen || IsDrawerOpen;

    public bool IsHistory => ActiveTab == ActivityDrawerTab.History;

    public bool IsActiveTab => ActiveTab == ActivityDrawerTab.Active;

    public Array HistoryKindOptions { get; } = Enum.GetValues(typeof(ActivityHistoryKindOption));

    public Array HistoryAgeOptions { get; } = Enum.GetValues(typeof(ActivityHistoryAgeOption));

    partial void OnIsPeekOpenChanged(bool value) => SyncPanelOpen();

    partial void OnIsDrawerOpenChanged(bool value)
    {
        SyncPanelOpen();
        if (value && ActiveTab == ActivityDrawerTab.History)
        {
            ReloadHistory();
        }
    }

    partial void OnActiveTabChanged(ActivityDrawerTab value)
    {
        OnPropertyChanged(nameof(IsHistory));
        OnPropertyChanged(nameof(IsScheduled));
        OnPropertyChanged(nameof(IsActiveTab));
        if (value == ActivityDrawerTab.Scheduled)
        {
            RebuildUpcoming();
        }

        if (value == ActivityDrawerTab.History && _historyLoaded == 0)
        {
            ReloadHistory();
        }
    }

    private void SyncPanelOpen()
    {
        _activity.PanelIsOpen = IsOpen;
        OnPropertyChanged(nameof(IsOpen));
    }

    // ---- projections ----

    public ObservableCollection<ActivityJob> RunningJobs { get; } = new();

    public ObservableCollection<ActivityJob> RecentJobs { get; } = new();

    public ObservableCollection<ActivityAlertViewModel> Alerts { get; } = new();

    /// <summary>The peek shows only the newest few finished jobs.</summary>
    public ObservableCollection<ActivityJob> RecentJobsForPeek { get; } = new();

    [ObservableProperty]
    private ActivityJob? _upkeepJob;

    [ObservableProperty]
    private bool _hasRunningJobs;

    [ObservableProperty]
    private bool _hasRecentJobs;

    [ObservableProperty]
    private bool _hasAlerts;

    [ObservableProperty]
    private int _runningCount;

    private void RebuildProjections()
    {
        SyncList(RunningJobs, _activity.ActiveJobs.Where(j => !j.IsUpkeep));
        SyncList(RecentJobs, _activity.RecentJobs);
        SyncList(RecentJobsForPeek, _activity.RecentJobs.Take(3).ToList());

        UpkeepJob = _activity.ActiveJobs.FirstOrDefault(j => j.IsUpkeep);

        Alerts.Clear();
        foreach (var alert in _activity.Alerts)
        {
            Alerts.Add(new ActivityAlertViewModel(alert, _activity.DismissAlert, _followLink));
        }

        HasRunningJobs = RunningJobs.Count > 0;
        HasRecentJobs = RecentJobs.Count > 0;
        HasAlerts = Alerts.Count > 0;
        RunningCount = RunningJobs.Count;
    }

    private static void SyncList(ObservableCollection<ActivityJob> target, System.Collections.Generic.IEnumerable<ActivityJob> source)
    {
        var wanted = source as System.Collections.Generic.IList<ActivityJob> ?? source.ToList();

        if (target.Count == wanted.Count)
        {
            bool same = true;
            for (int i = 0; i < wanted.Count; i++)
            {
                if (!ReferenceEquals(target[i], wanted[i]))
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        target.Clear();
        foreach (var job in wanted)
        {
            target.Add(job);
        }
    }

    // ---- history tab ----

    public ObservableCollection<ActivityHistoryRow> HistoryRows { get; } = new();

    [ObservableProperty]
    private string _historySearch = "";

    [ObservableProperty]
    private ActivityHistoryKindOption _historyKind = ActivityHistoryKindOption.All;

    [ObservableProperty]
    private ActivityHistoryAgeOption _historyAge = ActivityHistoryAgeOption.Last7Days;

    [ObservableProperty]
    private bool _historyFailuresOnly;

    [ObservableProperty]
    private bool _historyHasMore;

    [ObservableProperty]
    private bool _historyIsEmpty;

    partial void OnHistorySearchChanged(string value) => ReloadHistory();

    partial void OnHistoryKindChanged(ActivityHistoryKindOption value) => ReloadHistory();

    partial void OnHistoryAgeChanged(ActivityHistoryAgeOption value) => ReloadHistory();

    partial void OnHistoryFailuresOnlyChanged(bool value) => ReloadHistory();

    private ActivityHistoryFilter CurrentFilter => new(
        Search: string.IsNullOrWhiteSpace(HistorySearch) ? null : HistorySearch,
        Kind: HistoryKind == ActivityHistoryKindOption.All ? null : (ActivityJobKind)(int)HistoryKind,
        MaxAge: HistoryAge switch
        {
            ActivityHistoryAgeOption.Last24Hours => TimeSpan.FromDays(1),
            ActivityHistoryAgeOption.Last7Days => TimeSpan.FromDays(7),
            ActivityHistoryAgeOption.Last30Days => TimeSpan.FromDays(30),
            _ => null,
        },
        FailuresOnly: HistoryFailuresOnly);

    public void ReloadHistory()
    {
        _historyCts?.Cancel();
        var cts = _historyCts = new CancellationTokenSource();
        var filter = CurrentFilter;
        _historyLoaded = 0;

        _ = Task.Run(() =>
        {
            IReadOnlyList<Paperbunkr.Data.Entities.ActivityRun> page;
            try
            {
                page = ActivityHistoryStore.Query(filter, 0, HistoryPageSize);
            }
            catch
            {
                return; // history is best-effort; a read failure just leaves the tab empty
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                HistoryRows.Clear();
                foreach (var run in page)
                {
                    HistoryRows.Add(ActivityHistoryRow.FromRun(run));
                }

                _historyLoaded = page.Count;
                HistoryHasMore = page.Count == HistoryPageSize;
                HistoryIsEmpty = page.Count == 0;
            });
        }, cts.Token);
    }

    [RelayCommand]
    private void LoadMoreHistory()
    {
        var filter = CurrentFilter;
        int skip = _historyLoaded;

        _ = Task.Run(() =>
        {
            IReadOnlyList<Paperbunkr.Data.Entities.ActivityRun> page;
            try
            {
                page = ActivityHistoryStore.Query(filter, skip, HistoryPageSize);
            }
            catch
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var run in page)
                {
                    HistoryRows.Add(ActivityHistoryRow.FromRun(run));
                }

                _historyLoaded += page.Count;
                HistoryHasMore = page.Count == HistoryPageSize;
            });
        });
    }

    // ---- commands ----

    [RelayCommand]
    private void TogglePeek()
    {
        if (IsDrawerOpen)
        {
            IsDrawerOpen = false;
            return;
        }

        IsPeekOpen = !IsPeekOpen;
    }

    [RelayCommand]
    private void Close()
    {
        IsPeekOpen = false;
        IsDrawerOpen = false;
    }

    [RelayCommand]
    private void OpenDrawer()
    {
        IsPeekOpen = false;
        IsDrawerOpen = true;
        ActiveTab = ActivityDrawerTab.Active;
    }

    [RelayCommand]
    private void ShowActiveTab() => ActiveTab = ActivityDrawerTab.Active;

    [RelayCommand]
    private void ShowHistoryTab() => ActiveTab = ActivityDrawerTab.History;

    [RelayCommand]
    private void CancelJob(ActivityJob? job)
    {
        if (job is not null)
        {
            _activity.CancelJob(job.Id);
        }
    }

    [RelayCommand]
    private void StopAll() => _activity.StopAll();

    [RelayCommand]
    private void ClearFinished() => _activity.ClearFinished();

    [RelayCommand]
    private void DismissAllAlerts() => _activity.DismissAllAlerts();

    [RelayCommand]
    private void FollowLink(ActivityLink? link)
    {
        if (link is not null)
        {
            _followLink(link);
            Close();
        }
    }
}

/// <summary>History "type" filter options - values line up with <see cref="ActivityJobKind"/> plus an "All" sentinel of -1.</summary>
public enum ActivityHistoryKindOption
{
    All = -1,
    LibraryScan = ActivityJobKind.LibraryScan,
    BookScan = ActivityJobKind.BookScan,
    GenerateCovers = ActivityJobKind.GenerateCovers,
    SyncMetadata = ActivityJobKind.SyncMetadata,
    TrackerFetch = ActivityJobKind.TrackerFetch,
    Import = ActivityJobKind.Import,
    Update = ActivityJobKind.Update,
    Migration = ActivityJobKind.Migration,
}

public enum ActivityHistoryAgeOption
{
    Last24Hours,
    Last7Days,
    Last30Days,
    AllTime,
}
