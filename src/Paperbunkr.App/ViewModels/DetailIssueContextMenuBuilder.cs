using System.Collections.Generic;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds the comic Detail screen's issue-tile right-click menu (Issues tab and Specials tab both -
/// they share <c>IssuePosterTileTemplate</c>) as plain <see cref="ContextMenuEntry"/> data
/// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md). Not actually a from-scratch
/// new menu as originally scoped - a broader sweep found an already-dead
/// <c>&lt;ContextMenu x:Key="IssueContextMenu"&gt;</c> resource in <c>DetailTabs.axaml</c>'s own
/// <c>UserControl.Resources</c> (missed in the original per-screen dead-menu inventory since it's a
/// keyed resource, not an inline <c>Button.ContextMenu</c>/<c>Border.ContextMenu</c>), applied via
/// <c>ContextMenu="{StaticResource IssueContextMenu}"</c> - same non-rendering plain
/// <c>ContextMenu</c> problem as the other four. This builder ports that resource's full content
/// (Show in Explorer, Mark Read/Unread, Set/Reset Cover) verbatim rather than the smaller 3-entry
/// menu first assumed, plus adds Open in Reader as the one genuinely new entry - mirroring
/// <c>LibraryContextMenuBuilder</c>'s shape.
/// </summary>
public sealed class DetailIssueContextMenuBuilder
{
    private readonly DetailTabsViewModel _vm;

    public DetailIssueContextMenuBuilder(DetailTabsViewModel vm) => _vm = vm;

    public IReadOnlyList<ContextMenuEntry>? Build(object? target) => target switch
    {
        IssueCardSample issue => new[]
        {
            ContextMenuEntry.Item("Edit Properties", _vm.EditIssuePropertiesCommand, issue),
            ContextMenuEntry.Item("Open in Reader", _vm.OpenIssueInReaderCommand, issue),
            ContextMenuEntry.Item("Show in Explorer", _vm.RevealIssueCommand, issue, isEnabled: issue.HasFile),
            ContextMenuEntry.Separator,
            ContextMenuEntry.Item("Mark as Read", _vm.MarkIssueReadCommand, issue),
            ContextMenuEntry.Item("Mark as Unread", _vm.MarkIssueUnreadCommand, issue),
            ContextMenuEntry.Item("Quick Rate…", _vm.QuickRateCommand, issue),
            ContextMenuEntry.Separator,
            ContextMenuEntry.Item("Set Cover…", _vm.ChangeIssueCoverCommand, issue),
            ContextMenuEntry.Item("Reset Cover", _vm.ResetIssueCoverCommand, issue),
        },
        _ => null,
    };
}
