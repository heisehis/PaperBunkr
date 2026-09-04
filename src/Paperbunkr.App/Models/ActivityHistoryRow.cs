using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// One row in the Activity Center's History tab - a read-only projection of a persisted
/// <see cref="ActivityRun"/> (docs/superpowers/specs/2026-09-03-activity-center-design.md).
/// </summary>
public sealed class ActivityHistoryRow
{
    public required ActivityJobKind Kind { get; init; }

    public required string Title { get; init; }

    public required ActivityTrigger Trigger { get; init; }

    public required DateTime StartedUtc { get; init; }

    public DateTime? FinishedUtc { get; init; }

    public required ActivityRunStatus Status { get; init; }

    public string? ResultSummary { get; init; }

    public ActivityLink? ResultLink { get; init; }

    public int? ItemsFailed { get; init; }

    public bool HasLink => ResultLink is not null;

    public bool Failed => Status == ActivityRunStatus.Failed || ItemsFailed is > 0;

    public string TriggerLabel => Trigger switch
    {
        ActivityTrigger.DragDrop => "drag-drop",
        ActivityTrigger.Startup => "startup",
        ActivityTrigger.Scheduled => "scheduled",
        ActivityTrigger.Plugin => "plugin",
        ActivityTrigger.Watch => "folder-watch",
        _ => "manual",
    };

    public static ActivityHistoryRow FromRun(ActivityRun run)
    {
        ActivityLink? link = null;
        if (!string.IsNullOrEmpty(run.ResultLinkKind) && Enum.TryParse<ActivityLinkKind>(run.ResultLinkKind, out var kind))
        {
            link = new ActivityLink(kind, run.ResultLinkPayload ?? string.Empty);
        }

        return new ActivityHistoryRow
        {
            Kind = run.Kind,
            Title = run.Title,
            Trigger = run.Trigger,
            StartedUtc = run.StartedUtc,
            FinishedUtc = run.FinishedUtc,
            Status = run.Status,
            ResultSummary = run.ResultSummary,
            ResultLink = link,
            ItemsFailed = run.ItemsFailed,
        };
    }
}
