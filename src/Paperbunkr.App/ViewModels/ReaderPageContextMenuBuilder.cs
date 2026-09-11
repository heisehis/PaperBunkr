using System.Collections.Generic;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds the Reader's page-thumbnail right-click menu as plain <see cref="ContextMenuEntry"/> data
/// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md) - ports a menu that used to
/// live in a dead <c>Border.ContextMenu</c>/<c>ContextMenu</c> element (confirmed via
/// <c>ContextMenuHost</c>'s own doc comment: a plain <c>ContextMenu</c> popup does not render at all
/// in this Avalonia 12 + FluentAvalonia build). First builder to use nested submenus
/// (<see cref="ContextMenuEntry.SubMenu"/>) - already-supported recursion in
/// <c>ContextMenuHost.Build</c>, exercised here for the first time by a new/fixed builder.
/// </summary>
public sealed class ReaderPageContextMenuBuilder
{
    private readonly ReaderScreenViewModel _vm;

    public ReaderPageContextMenuBuilder(ReaderScreenViewModel vm) => _vm = vm;

    public IReadOnlyList<ContextMenuEntry>? Build(object? target) => target switch
    {
        ReaderThumbnailSample thumbnail => BuildForThumbnail(thumbnail),
        _ => null,
    };

    /// <summary>
    /// "Spread position" (docs/superpowers/specs/2026-09-10-reader-backlog-batch-b-design.md Item
    /// 2) is only meaningful in paged double-page mode - continuous/webtoon scroll never pairs
    /// pages, so the submenu is omitted entirely there rather than shown disabled.
    /// </summary>
    private IReadOnlyList<ContextMenuEntry> BuildForThumbnail(ReaderThumbnailSample thumbnail)
    {
        var entries = new List<ContextMenuEntry>
        {
            ContextMenuEntry.SubMenu("Page Type", new[]
            {
                ContextMenuEntry.Item("Story", _vm.SetPageTypeStoryCommand, thumbnail),
                ContextMenuEntry.Item("Cover", _vm.SetPageTypeCoverCommand, thumbnail),
                ContextMenuEntry.Item("Advertisement", _vm.SetPageTypeAdvertisementCommand, thumbnail),
                ContextMenuEntry.Item("Deleted", _vm.SetPageTypeDeletedCommand, thumbnail),
            })!,
            ContextMenuEntry.SubMenu("Rotate", new[]
            {
                ContextMenuEntry.Item("No rotation", _vm.SetPageRotation0Command, thumbnail),
                ContextMenuEntry.Item("90°", _vm.SetPageRotation90Command, thumbnail),
                ContextMenuEntry.Item("180°", _vm.SetPageRotation180Command, thumbnail),
                ContextMenuEntry.Item("270°", _vm.SetPageRotation270Command, thumbnail),
            })!,
        };

        var spreadMenu = ContextMenuEntry.SubMenu("Spread position", new[]
        {
            ContextMenuEntry.Item("Automatic", _vm.SetSpreadPositionDefaultCommand, thumbnail),
            ContextMenuEntry.Item("Near side (leading)", _vm.SetSpreadPositionNearCommand, thumbnail),
            ContextMenuEntry.Item("Far side (trailing)", _vm.SetSpreadPositionFarCommand, thumbnail),
        }, isVisible: !_vm.IsContinuousMode);
        if (spreadMenu is not null)
        {
            entries.Add(spreadMenu);
        }

        return entries;
    }
}
