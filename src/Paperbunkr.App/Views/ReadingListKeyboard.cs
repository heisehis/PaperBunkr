using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Views;

/// <summary>
/// Arrow-key movement through a Reading Lists items control (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §11): the
/// path's rows, the cover wall and the gallery's tiles. Works with virtualization - the target is scrolled into view first, then its
/// first enabled, focusable control (a row's title button, a missing row's "Find &amp; link", a tile) takes directional focus. Items with
/// nothing focusable are skipped in the direction of travel.
/// </summary>
internal static class ReadingListKeyboard
{
    /// <summary>The index of the item holding keyboard focus in <paramref name="list"/>, or -1. Forwards to <see cref="VirtualizedFocus"/>
    /// (docs/superpowers/specs/2026-09-28-keyboard-focus-reclaim-design.md) - kept here so no existing call site needs to change.</summary>
    public static int FocusedIndex(ItemsControl list) => VirtualizedFocus.FocusedIndex(list);

    /// <summary>Focus item <paramref name="target"/> (clamped); <paramref name="direction"/> (+1/-1) decides which way to skip unfocusable
    /// items. Forwards to <see cref="VirtualizedFocus"/> - see <see cref="FocusedIndex"/>'s own doc comment.</summary>
    public static void FocusIndex(ItemsControl list, int target, int direction) => VirtualizedFocus.FocusIndex(list, target, direction);

    /// <summary>How many uniform tiles fit per row of a wrap panel.</summary>
    public static int Columns(ItemsControl list, double tileWidthWithSpacing)
    {
        var panel = list.ItemsPanelRoot;
        double width = panel?.Bounds.Width is > 0 and var w ? w : list.Bounds.Width;
        return Math.Max(1, (int)Math.Floor((width + 1) / tileWidthWithSpacing));
    }

    /// <summary>
    /// Handles ↑/↓ (a list) or ←/→/↑/↓ (a grid of <paramref name="columns"/>), PageUp/PageDown (±10 rows) and Home/End. Returns whether the
    /// key was used. <paramref name="startIndex"/> is where movement begins when nothing in the list has focus yet.
    /// </summary>
    public static bool HandleNavigationKey(ItemsControl list, KeyEventArgs e, int columns, int startIndex)
    {
        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
        {
            return false;
        }

        int current = FocusedIndex(list);
        int step = e.Key switch
        {
            Key.Down => columns,
            Key.Up => -columns,
            Key.Right when columns > 1 => 1,
            Key.Left when columns > 1 => -1,
            Key.PageDown => 10 * columns,
            Key.PageUp => -10 * columns,
            _ => 0,
        };

        if (e.Key == Key.Home)
        {
            FocusIndex(list, 0, 1);
            return true;
        }

        if (e.Key == Key.End)
        {
            FocusIndex(list, list.ItemCount - 1, -1);
            return true;
        }

        if (step == 0)
        {
            return false;
        }

        FocusIndex(list, current < 0 ? startIndex : current + step, Math.Sign(step));
        return true;
    }
}
