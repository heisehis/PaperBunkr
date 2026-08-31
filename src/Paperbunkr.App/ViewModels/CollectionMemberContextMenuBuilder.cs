using System.Collections.Generic;
using Paperbunkr.App.ContextMenus;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Builds the Collection editor's member-row right-click menu as plain <see cref="ContextMenuEntry"/>
/// data (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md) - a new menu, this screen
/// had none before. Trivial: <see cref="CollectionMemberRowViewModel"/> already owns its own
/// <c>RemoveCommand</c> (gated on <c>!IsRuleMatched</c> - a smart-collection rule-matched row can't
/// be removed), so this just surfaces it; no parameter needed since the command is already bound to
/// the row itself.
/// </summary>
public sealed class CollectionMemberContextMenuBuilder
{
    public IReadOnlyList<ContextMenuEntry>? Build(object? target) => target switch
    {
        CollectionMemberRowViewModel row => new[]
        {
            ContextMenuEntry.Item("Remove from Collection", row.RemoveCommand),
        },
        _ => null,
    };
}
