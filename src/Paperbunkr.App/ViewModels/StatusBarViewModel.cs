using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Backs the persistent bottom status bar (docs/superpowers/specs/2026-09-03-activity-center-
/// design.md). Left region: a static library total. Right region: the activity indicator, which
/// mirrors <see cref="IActivityService"/> aggregate state and toggles the Activity Center.
/// </summary>
public sealed partial class StatusBarViewModel : ViewModelBase
{
    private readonly IActivityService _activity;
    private readonly Func<(int Comics, long Bytes)> _libraryStatsProvider;
    private DateTime _lastStatsRefreshUtc = DateTime.MinValue;

    public StatusBarViewModel(IActivityService activity, Func<(int Comics, long Bytes)> libraryStatsProvider, Action toggleActivityCenter)
    {
        _activity = activity;
        _libraryStatsProvider = libraryStatsProvider;
        _activity.Changed += (_, _) => Recompute();
        ToggleActivityCenterCommand = new RelayCommand(toggleActivityCenter);
        RefreshLibraryStats();
        Recompute();
    }

    [ObservableProperty]
    private string _contextText = "";

    /// <summary>True when any non-upkeep job is running - drives the indicator's pulse + visibility beyond the idle dot.</summary>
    [ObservableProperty]
    private bool _hasActiveJobs;

    /// <summary>"340 / 1,200", "2 running", or "" when idle.</summary>
    [ObservableProperty]
    private string _indicatorText = "";

    [ObservableProperty]
    private int _unreadAlertCount;

    [ObservableProperty]
    private bool _hasAlerts;

    public IRelayCommand ToggleActivityCenterCommand { get; }

    /// <summary>Re-query the library total. Cheap aggregate; throttled to once / 3s. Runs on a background thread.</summary>
    public void RefreshLibraryStats(bool force = false)
    {
        if (!force && DateTime.UtcNow - _lastStatsRefreshUtc < TimeSpan.FromSeconds(3))
        {
            return;
        }

        _lastStatsRefreshUtc = DateTime.UtcNow;
        _ = Task.Run(() =>
        {
            try
            {
                var (comics, bytes) = _libraryStatsProvider();
                string text = $"{comics:N0} comic{(comics == 1 ? "" : "s")} · {FormatBytes(bytes)}";
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ContextText = text);
            }
            catch
            {
                // A status-line number is never worth surfacing a failure for.
            }
        });
    }

    private void Recompute()
    {
        var running = _activity.ActiveJobs.Where(j => !j.IsUpkeep && j.IsRunning).ToList();
        HasActiveJobs = running.Count > 0;

        if (running.Count == 0)
        {
            IndicatorText = "";
        }
        else if (running.Any(j => j.IsIndeterminate))
        {
            IndicatorText = $"{running.Count} running";
        }
        else
        {
            int done = running.Sum(j => j.Done ?? 0);
            int total = running.Sum(j => j.Total ?? 0);
            IndicatorText = total > 0 ? $"{done:N0} / {total:N0}" : $"{running.Count} running";
        }

        UnreadAlertCount = _activity.Alerts.Count;
        HasAlerts = UnreadAlertCount > 0;

        // A finished scan/import changes the library total.
        RefreshLibraryStats();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MB";
        }

        double gb = bytes / 1024d / 1024d / 1024d;
        if (gb >= 1)
        {
            return $"{gb:0.0} GB";
        }

        double mb = bytes / 1024d / 1024d;
        return $"{mb:0} MB";
    }
}
