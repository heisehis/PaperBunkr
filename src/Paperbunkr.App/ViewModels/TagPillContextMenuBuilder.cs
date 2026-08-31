using System.Collections.Generic;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds a tag pill's weight-picker right-click menu as plain <see cref="ContextMenuEntry"/> data
/// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md) - ports a menu that used to
/// live in two separate dead <c>Button.ContextMenu</c>/<c>ContextMenu</c> elements with identical
/// content (<c>ReadingScreen.axaml</c>'s tag pills and the shared <c>DetailBand.axaml</c>'s tag
/// chips - found during a broader sweep for dead menus beyond the original four). One shared builder
/// since both bind the same <see cref="TagPillViewModel"/> with the same
/// <c>SetWeightCommand</c>/<c>CanReweight</c> shape - fixing it once here fixes both, and
/// automatically covers <c>DetailBand</c>'s three consuming screens (Detail/MangaDetail/BookDetail)
/// at once since they all share that one control. No parent ViewModel needed - the row's own
/// <c>SetWeightCommand</c> is already bound to itself, same as <see cref="CollectionMemberContextMenuBuilder"/>.
/// </summary>
public sealed class TagPillContextMenuBuilder
{
    public IReadOnlyList<ContextMenuEntry>? Build(object? target) => target switch
    {
        // The original dead menu was IsVisible="{Binding CanReweight}" on the whole ContextMenu, not
        // per-item - a non-reweightable pill (CanReweight false) gets no menu at all, not a disabled one.
        TagPillViewModel { CanReweight: true } pill => new[]
        {
            Weight(pill, IssueTagWeight.Incidental, "Incidental"),
            Weight(pill, IssueTagWeight.Recurrent, "Recurrent"),
            Weight(pill, IssueTagWeight.Defining, "Defining"),
            Weight(pill, IssueTagWeight.Core, "Core"),
        },
        _ => null,
    };

    private static ContextMenuEntry Weight(TagPillViewModel pill, IssueTagWeight weight, string header) =>
        ContextMenuEntry.Item(header, pill.SetWeightCommand, weight, isChecked: pill.Weight == weight);
}
