using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// One background job tracked by the Activity Center (docs/superpowers/specs/2026-09-03-activity-
/// center-design.md). Mutated in place while it runs (an <see cref="ObservableObject"/>, like
/// <c>IssueBookmarkSummary</c>) so the status bar, peek and drawer all update live off a single
/// instance - the owning caller drives it through an <c>IActivityJobHandle</c>, never directly.
/// </summary>
public sealed partial class ActivityJob : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    public required ActivityJobKind Kind { get; init; }

    public required string Title { get; init; }

    public ActivityTrigger Trigger { get; init; } = ActivityTrigger.Manual;

    /// <summary>
    /// When this job's completion should surface a transient toast. Normal jobs use
    /// <see cref="ActivityToastPolicy.Always"/>; scheduled maintenance jobs set this from the
    /// user's notification-level preference so routine upkeep doesn't nag.
    /// </summary>
    public ActivityToastPolicy ToastPolicy { get; init; } = ActivityToastPolicy.Always;

    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>The single ambient rollup row - excluded from aggregate progress, "Clear finished", and never settles.</summary>
    public bool IsUpkeep { get; init; }

    /// <summary>For the upkeep row only: true while it is actually doing something (watching/decoding), false when idle.</summary>
    [ObservableProperty]
    private bool _upkeepActive;

    [ObservableProperty]
    private ActivityJobStatus _status = ActivityJobStatus.Queued;

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    private int? _done;

    [ObservableProperty]
    private int? _total;

    [ObservableProperty]
    private DateTime? _finishedUtc;

    [ObservableProperty]
    private string? _resultSummary;

    [ObservableProperty]
    private ActivityLink? _resultLink;

    /// <summary>True when there is no meaningful done/total - the panel shows a marquee bar and the count form in the aggregate.</summary>
    public bool IsIndeterminate => Status == ActivityJobStatus.Running && (Done is null || Total is null or 0);

    public bool IsRunning => Status == ActivityJobStatus.Running;

    public bool IsFinished => Status is ActivityJobStatus.Succeeded or ActivityJobStatus.Failed or ActivityJobStatus.Cancelled;

    public double Fraction => Total is > 0 ? Math.Clamp((double)(Done ?? 0) / Total.Value, 0, 1) : 0;

    partial void OnStatusChanged(ActivityJobStatus value)
    {
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFinished));
    }

    partial void OnDoneChanged(int? value)
    {
        OnPropertyChanged(nameof(Fraction));
        OnPropertyChanged(nameof(IsIndeterminate));
    }

    partial void OnTotalChanged(int? value)
    {
        OnPropertyChanged(nameof(Fraction));
        OnPropertyChanged(nameof(IsIndeterminate));
    }
}
