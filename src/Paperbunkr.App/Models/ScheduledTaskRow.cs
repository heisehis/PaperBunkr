using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// One row in the Automation Preferences tab - a catalog descriptor joined with its live
/// <see cref="ScheduledTaskState"/> plus derived display strings
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
/// Rebuilt by <c>AutomationSectionViewModel</c> whenever the scheduler raises <c>Changed</c>.
/// </summary>
public sealed partial class ScheduledTaskRow : ObservableObject
{
    public required string TaskId { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private ScheduleMode _mode;

    [ObservableProperty]
    private int _intervalHours;

    /// <summary>Bound to a TimePicker - the wall-clock time for <see cref="ScheduleMode.DailyAt"/>.</summary>
    [ObservableProperty]
    private TimeSpan _dailyAtTime;

    [ObservableProperty]
    private DateTime? _lastRunUtc;

    [ObservableProperty]
    private ScheduledRunStatus? _lastRunStatus;

    [ObservableProperty]
    private string _nextRunLabel = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isQueued;

    public bool IsIntervalMode => Mode == ScheduleMode.Interval;

    public bool IsDailyMode => Mode == ScheduleMode.DailyAt;

    public string LastRunLabel => LastRunUtc is DateTime t
        ? RelativeTime(t) + (LastRunStatus == ScheduledRunStatus.Failed ? " (failed)" : "")
        : "Never run";

    /// <summary>Set by the VM; runs the task immediately via <see cref="Scheduling.ISchedulerService.RunNowAsync"/>.</summary>
    public IAsyncRelayCommand? RunNowCommand { get; set; }

    partial void OnModeChanged(ScheduleMode value)
    {
        OnPropertyChanged(nameof(IsIntervalMode));
        OnPropertyChanged(nameof(IsDailyMode));
    }

    partial void OnLastRunUtcChanged(DateTime? value) => OnPropertyChanged(nameof(LastRunLabel));

    partial void OnLastRunStatusChanged(ScheduledRunStatus? value) => OnPropertyChanged(nameof(LastRunLabel));

    private static string RelativeTime(DateTime utc)
    {
        var delta = DateTime.UtcNow - utc;
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{(int)delta.TotalMinutes} min ago";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"{(int)delta.TotalHours} h ago";
        }

        return $"{(int)delta.TotalDays} d ago";
    }
}
