using System;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Which sub-state of a scope an action is reachable in. Generalises the old <c>ConflictContext</c>: the same key can mean "turn the page" (unzoomed),
/// "pan" (zoomed) or "scroll" (continuous), and each of those is a separate action tagged with the state it applies to. The values are bit flags so a
/// state test is one AND: a scope reports its <em>current</em> state (always a single paged-or-continuous state), an action declares the state(s) it
/// accepts, and the action is a candidate when the two share a bit. See <see cref="InputContextExtensions.Overlaps"/>.
/// </summary>
[Flags]
public enum InputContext
{
    /// <summary>No state at all; matches nothing, not even <see cref="Always"/>.</summary>
    None = 0,

    /// <summary>Paged reading with the page fitted (nothing to pan).</summary>
    PagedUnzoomed = 1,

    /// <summary>Paged reading zoomed in past the viewport (there is something to pan).</summary>
    PagedZoomed = 2,

    /// <summary>Continuous (scrolling) reading.</summary>
    Continuous = 4,

    /// <summary>Every paged state, zoomed or not; for reading-order commands such as next page. Overlaps both paged states, never <see cref="Continuous"/>.</summary>
    Paged = PagedUnzoomed | PagedZoomed,

    /// <summary>Reachable in every state; overlaps everything, so sharing a binding with any other action in the same scope is a real conflict.</summary>
    Always = Paged | Continuous,
}

public static class InputContextExtensions
{
    /// <summary>True when the two contexts can be active at the same time, i.e. share at least one state. Equivalent to the old <c>ConflictContexts.MayOverlap</c>.</summary>
    public static bool Overlaps(this InputContext a, InputContext b) => (a & b) != 0;
}
