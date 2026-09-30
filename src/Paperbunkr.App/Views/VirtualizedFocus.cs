using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Views;

/// <summary>
/// Establishes keyboard focus at a given index of a (possibly virtualized) <see cref="ItemsControl"/> - the "focus
/// by index" half of <see cref="FocusReclaimer"/>'s job. Extracted from <c>ReadingListKeyboard</c>
/// (docs/superpowers/specs/2026-09-28-keyboard-focus-reclaim-design.md, Phase 1) - proven against a
/// <c>VirtualizingStackPanel</c> and a custom <c>VirtualizingWrapPanel</c> there; <c>ReadingListKeyboard.FocusedIndex</c>/
/// <c>FocusIndex</c> now just forward here, so no existing call site or test changes. Distinct from
/// <see cref="GridKeyboardNavigation"/>, which only computes movement *from* an already-focused item and has no
/// notion of establishing focus with nothing focused yet.
/// </summary>
internal static class VirtualizedFocus
{
    /// <summary>The index of the item holding keyboard focus in <paramref name="list"/>, or -1.</summary>
    public static int FocusedIndex(ItemsControl list)
    {
        var focused = TopLevel.GetTopLevel(list)?.FocusManager?.GetFocusedElement() as Visual;
        for (var v = focused; v is not null && !ReferenceEquals(v, list); v = v.GetVisualParent())
        {
            if (v is Control c && list.IndexFromContainer(c) is var index and >= 0)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Focus item <paramref name="target"/> (clamped); <paramref name="direction"/> (+1/-1) decides which way to skip unfocusable
    /// items. When a target's container is realized but nothing inside it is focusable *yet* (its own nested content hasn't realized - e.g.
    /// Library's grouped view modes nest an independently-virtualized panel inside each group), retries that same index a few more ticks
    /// before moving on, rather than skipping to the next index immediately.</summary>
    public static void FocusIndex(ItemsControl list, int target, int direction)
    {
        int count = list.ItemCount;
        if (count == 0)
        {
            return;
        }

        target = Math.Clamp(target, 0, count - 1);
        list.ScrollIntoView(target);
        Dispatcher.UIThread.Post(() => TryFocus(list, target, direction, count, outerTries: 0, innerTries: 0), DispatcherPriority.Loaded);
    }

    private const int MaxOuterTries = 50;
    private const int MaxInnerTries = 5;

    private static void TryFocus(ItemsControl list, int index, int direction, int count, int outerTries, int innerTries)
    {
        if (index < 0 || index >= count || outerTries >= MaxOuterTries)
        {
            return;
        }

        list.ScrollIntoView(index);
        if (list.ContainerFromIndex(index) is { } container)
        {
            if (FirstFocusable(container) is { } control)
            {
                control.Focus(Avalonia.Input.NavigationMethod.Directional);
                control.BringIntoView();
                return;
            }

            if (innerTries < MaxInnerTries)
            {
                Dispatcher.UIThread.Post(() => TryFocus(list, index, direction, count, outerTries, innerTries + 1), DispatcherPriority.Loaded);
                return;
            }
        }

        int next = index + (direction == 0 ? 1 : direction);
        Dispatcher.UIThread.Post(() => TryFocus(list, next, direction, count, outerTries + 1, innerTries: 0), DispatcherPriority.Loaded);
    }

    private static Control? FirstFocusable(Control container) =>
        container.GetSelfAndVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c is Button && c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible);
}
