using System.Collections.Generic;
using Paperbunkr.App.ContextMenus;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Right-click, Menu key and the tiles' "…" button all ask here (<see cref="Controls.ContextMenuHost"/>): the same actions the visible buttons offer, for whichever row
/// the pointer or focus is on.
/// </summary>
public sealed partial class WantedScreenViewModel : IContextMenuProvider
{
    public IReadOnlyList<ContextMenuEntry>? BuildContextMenu(object? target) => target switch
    {
        ReleaseRowViewModel release => ReleaseMenu(release),
        QueueIssueViewModel issue => IssueMenu(issue),
        QueueGroupViewModel group => GroupMenu(group),
        WatchedSeriesRowViewModel series => SeriesMenu(series),
        _ => null,
    };

    private IReadOnlyList<ContextMenuEntry> SeriesMenu(WatchedSeriesRowViewModel row)
    {
        var entries = new List<ContextMenuEntry>();
        if (row.CanOpen)
        {
            entries.Add(ContextMenuEntry.Item("Open series", OpenSeriesCommand, row));
        }

        entries.Add(ContextMenuEntry.Item("Stop tracking", UntrackSeriesCommand, row, isDanger: true));
        return entries;
    }

    private IReadOnlyList<ContextMenuEntry> ReleaseMenu(ReleaseRowViewModel row)
    {
        var entries = new List<ContextMenuEntry>();
        if (row.CanRequest)
        {
            entries.Add(ContextMenuEntry.Item("Request this issue", RequestReleaseCommand, row));
        }

        if (row.CanFollow)
        {
            entries.Add(ContextMenuEntry.Item("Follow this series", FollowReleaseSeriesCommand, row));
        }

        if (row.CanHide)
        {
            entries.Add(ContextMenuEntry.Item("Hide from the list", HideReleaseCommand, row));
        }

        if (row.IsHidden)
        {
            entries.Add(ContextMenuEntry.Item("Restore to the list", RestoreReleaseCommand, row));
        }

        return entries;
    }

    private IReadOnlyList<ContextMenuEntry> IssueMenu(QueueIssueViewModel issue)
    {
        var entries = new List<ContextMenuEntry>();
        if (issue.Stage == QueueStage.Wanted && issue.Candidates.Count > 0)
        {
            entries.Add(ContextMenuEntry.Item(issue.IsExpanded ? "Hide candidates" : $"Show {issue.CandidateText}", ToggleCandidatesCommand, issue));
        }

        if (issue.IsFailed)
        {
            entries.Add(ContextMenuEntry.Item("Try again", RetryDownloadCommand, issue));
        }

        if (issue.CanEdit)
        {
            entries.Add(ContextMenuEntry.Item("I have this", IgnoreCommand, issue));
            entries.Add(ContextMenuEntry.Item("Stop wanting this", RemoveCommand, issue, isDanger: true));
        }

        if (issue.IsDownloading)
        {
            entries.Add(ContextMenuEntry.Item("Cancel download", CancelDownloadCommand, issue, isDanger: true));
        }

        return entries;
    }

    private IReadOnlyList<ContextMenuEntry> GroupMenu(QueueGroupViewModel group)
    {
        var entries = new List<ContextMenuEntry> { ContextMenuEntry.Item(group.IsExpanded ? "Collapse" : "Expand", ToggleGroupCommand, group) };
        if (group.CanBulk)
        {
            entries.Add(ContextMenuEntry.Item("I have all of these", IgnoreGroupCommand, group));
            entries.Add(ContextMenuEntry.Item("Stop wanting all of these", RemoveGroupCommand, group, isDanger: true));
        }

        return entries;
    }
}
