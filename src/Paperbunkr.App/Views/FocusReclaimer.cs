using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Views;

/// <summary>
/// Puts focus back inside a view whenever it should have some and doesn't. First found and fixed for Reading Lists
/// (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md's keyboard-operability follow-up), in two shapes:
/// <list type="bullet">
/// <item>A view that stays permanently attached - only <c>IsVisible</c>/opacity toggle, e.g. <c>ReadingListsScreen</c>'s gallery/list pair,
/// kept that way on purpose so scroll position survives a trip away and back - never regains focus when shown again, because nothing put it
/// there in the first place.</item>
/// <item>A view whose bound content is swapped <em>in place</em> while it stays visible and attached throughout - a folder navigation, a
/// list switch, any reset of the <c>ItemsSource</c> collection its focus lived in. The previously-focused control is detached along with
/// the old content, and nothing takes its place.</item>
/// </list>
/// Either way arrow keys then silently go nowhere, or a stray focus change sends the FocusManager looking elsewhere in the window (the
/// app's nav rail, observed for the second case above) instead of back into the view. One instance per view; call <see cref="Reclaim"/>
/// from every point that can cause either gap - the view's own <c>OnAttachedToVisualTree</c>, its <c>IsVisible</c> flipping true, and any
/// content-reset signal from the view model (most simply, the relevant <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>'s
/// own <c>CollectionChanged</c>).
/// </summary>
internal sealed class FocusReclaimer
{
    private readonly Control _region;
    private readonly Func<bool> _isReady;
    private readonly Action _focusFallback;

    /// <param name="region">The view (or the region within it) focus should live inside.</param>
    /// <param name="isReady">Whether there is real content to focus right now (e.g. the VM has a list open, or has any tiles) - Reclaim is a
    /// no-op otherwise, so it's safe to call speculatively.</param>
    /// <param name="focusFallback">Puts focus on whatever the view considers its default target (the last-used row, the first tile, ...).
    /// Called only when nothing inside <paramref name="region"/> already holds focus.</param>
    public FocusReclaimer(Control region, Func<bool> isReady, Action focusFallback)
    {
        _region = region;
        _isReady = isReady;
        _focusFallback = focusFallback;
    }

    /// <summary>Deferred past this frame's layout (so a just-attached or just-repopulated container has a chance to realize), focuses the
    /// fallback only if nothing inside the region already holds focus and there's something ready to focus.</summary>
    public void Reclaim() => Post(condition: null, onlyIfFocusLost: false);

    /// <summary>Content-change variant: reclaims only if, at the moment of the signal, focus was inside the region or nowhere at all - so a
    /// list reload triggered while the user is in a sibling region (a sidebar, the nav rail) never pulls focus away from it.</summary>
    public void ReclaimIfFocusWithinOrNowhere() => Post(IsInside(_region) || FocusedElement(_region) is null, onlyIfFocusLost: false);

    /// <summary>For a region that can't tell from the signal alone whether focus lived in it (the list's own handler may already have
    /// detached the focused row): reclaims only if, once deferred, focus is genuinely gone (nothing focused, or the focused element
    /// hidden or detached). The caller decides whether the region is the one that lost it.</summary>
    public void ReclaimIfFocusLost() => Post(condition: null, onlyIfFocusLost: true);

    private void Post(bool? condition, bool onlyIfFocusLost) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (condition == false || !_region.IsEffectivelyVisible || !_isReady())
            {
                return;
            }

            if (onlyIfFocusLost ? FocusedElement(_region) is Visual { IsEffectivelyVisible: true } live && TopLevel.GetTopLevel(live) is not null
                                : HoldsUsableFocus(_region))
            {
                return;
            }

            _focusFallback();
        }, DispatcherPriority.Loaded);

    // A focused element that has itself become invisible (its row was hidden or collapsed, not detached) still counts as "inside" by
    // ancestry but can't receive keys, so it is treated as focus lost.
    private static bool HoldsUsableFocus(Visual region) =>
        IsInside(region) && FocusedElement(region) is Visual { IsEffectivelyVisible: true };

    private static object? FocusedElement(Visual anchor) => TopLevel.GetTopLevel(anchor)?.FocusManager?.GetFocusedElement();

    /// <summary>Whether the FocusManager's currently-focused element sits inside <paramref name="region"/> (itself included).</summary>
    public static bool IsInside(Visual region) =>
        FocusedElement(region) is Visual focused
        && focused.GetSelfAndVisualAncestors().Any(a => ReferenceEquals(a, region));

    /// <summary>Focuses the first focusable, enabled, effectively visible <see cref="Button"/> under <paramref name="scope"/>, preferring one
    /// that satisfies <paramref name="prefer"/>. Returns whether anything was focused.</summary>
    public static bool FocusFirstButton(Visual scope, Predicate<Button>? prefer = null)
    {
        var buttons = scope.GetSelfAndVisualDescendants().OfType<Button>()
            .Where(b => b.Focusable && b.IsEffectivelyEnabled && b.IsEffectivelyVisible)
            .ToList();
        var target = (prefer is null ? null : buttons.FirstOrDefault(b => prefer(b))) ?? buttons.FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        target.Focus(NavigationMethod.Directional);
        return true;
    }

    /// <summary>Up/Down arrow on a row Button: moves focus to the nearest focusable element above or below, the way an arrow key walks a
    /// list. Returns whether the key was consumed.</summary>
    public static bool TryStepVertically(Visual from, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Up or Key.Down)
            || TopLevel.GetTopLevel(from)?.FocusManager?.FindNextElement(e.Key == Key.Up ? NavigationDirection.Up : NavigationDirection.Down) is not { } next)
        {
            return false;
        }

        next.Focus(NavigationMethod.Directional);
        if (next is Control moved)
        {
            BringIntoViewWithRing(moved);
        }
        return true;
    }

    /// <summary>Screen-wide arrow-key fallback: an arrow press that nothing inside the region handled moves focus to the nearest focusable
    /// element in that direction, but only while the target stays inside <paramref name="region"/> (arrows never escape into the nav rail).
    /// Controls that use the arrows themselves (sliders, dropdowns, lists, and text boxes for Left/Right or when multi-line) are left alone. Returns whether the key was consumed.</summary>
    public static bool TryMoveDirectionally(Control region, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None
            || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)
            || e.Source is not Visual source
            || source.GetSelfAndVisualAncestors().TakeWhile(a => !ReferenceEquals(a, region))
                .Any(a => a is TextBox box ? e.Key is Key.Left or Key.Right || box.AcceptsReturn
                                           : a is Slider or ComboBox or ListBox or Avalonia.Controls.Primitives.ScrollBar or Controls.SuggestBox { IsDropDownOpen: true }))
        {
            return false;
        }

        var direction = e.Key switch
        {
            Key.Left => NavigationDirection.Left,
            Key.Right => NavigationDirection.Right,
            Key.Up => NavigationDirection.Up,
            _ => NavigationDirection.Down,
        };

        var manager = TopLevel.GetTopLevel(region)?.FocusManager;
        bool vertical = direction is NavigationDirection.Up or NavigationDirection.Down;
        Control? Find()
        {
            var spatial = manager?.FindNextElement(direction, InRegion(region)) is Control c && c.GetSelfAndVisualAncestors().Any(a => ReferenceEquals(a, region)) ? c : null;
            if (!vertical)
            {
                return spatial;
            }

            // The spatial search only weighs controls that overlap the focused one horizontally, so it can step over a whole row whose buttons sit in other columns (or find nothing at all).
            // Take the row-based answer when it is a nearer row.
            var byRows = FindByRows(region, source, direction);
            return spatial is null || (byRows is not null && RowGap(region, source, byRows, direction) < RowGap(region, source, spatial, direction) - 8) ? byRows ?? spatial : spatial;
        }

        var next = Find();
        if (next is null && vertical && ScrollTowards(source, direction, region))
        {
            // A virtualizing list only has the rows near the viewport: scrolling realises the ones beyond it, so the search can be run again.
            next = Find();
            if (next is null)
            {
                return true;       // scrolled; nothing new to land on yet (the next press finds the rows that just appeared)
            }
        }

        if (next is null)
        {
            return false;
        }

        next.Focus(NavigationMethod.Directional);
        BringIntoViewWithRing(next);
        return true;
    }

    /// <summary>
    /// Scrolls the nearest scroll viewer around <paramref name="from"/> half a viewport up or down, when it has room to go that way, and runs a layout pass so a virtualizing panel realises the rows
    /// that came into range. False when there is nothing to scroll.
    /// </summary>
    private static bool ScrollTowards(Visual from, NavigationDirection direction, Control region)
    {
        bool down = direction == NavigationDirection.Down;
        var viewer = from.GetSelfAndVisualAncestors().TakeWhile(a => !ReferenceEquals(a, region)).OfType<ScrollViewer>()
            .FirstOrDefault(v => down ? v.Offset.Y < v.Extent.Height - v.Viewport.Height - 0.5 : v.Offset.Y > 0.5);
        if (viewer is null)
        {
            return false;
        }

        double step = Math.Max(48, viewer.Viewport.Height * 0.5) * (down ? 1 : -1);
        var before = viewer.Offset;
        viewer.Offset = new Vector(before.X, Math.Clamp(before.Y + step, 0, Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height)));
        if (viewer.Offset == before)
        {
            return false;
        }

        region.UpdateLayout();
        return true;
    }

    /// <summary>How far, vertically between centres, <paramref name="target"/> is from <paramref name="from"/> in <paramref name="direction"/>; large when it cannot be worked out.</summary>
    private static double RowGap(Control region, Visual from, Control target, NavigationDirection direction)
    {
        if (from is not Control source || source.TranslatePoint(default, region) is not { } a || target.TranslatePoint(default, region) is not { } b)
        {
            return double.MaxValue;
        }

        double sourceY = a.Y + (source.Bounds.Height / 2);
        double targetY = b.Y + (target.Bounds.Height / 2);
        return direction == NavigationDirection.Down ? targetY - sourceY : sourceY - targetY;
    }

    /// <summary>
    /// The control on the next row up or down, whichever column it is in. Avalonia's directional search only considers controls that overlap the focused one horizontally, so on a list whose
    /// rows keep their buttons in different columns (a row with a button at the far right above a row with one at the far left) Up and Down found nothing and did nothing. This picks the
    /// nearest row in the direction, then the control in it closest to the current column.
    /// </summary>
    internal static Control? FindByRows(Control region, Visual from, NavigationDirection direction)
    {
        if (from is not Control source || source.TranslatePoint(default, region) is not { } origin)
        {
            return null;
        }

        var sourceRect = new Rect(origin, source.Bounds.Size);
        bool down = direction == NavigationDirection.Down;
        Control? best = null;
        double bestGap = double.MaxValue;
        double bestDx = double.MaxValue;
        foreach (var candidate in region.GetVisualDescendants().OfType<Control>())
        {
            if (!candidate.Focusable || !candidate.IsTabStop || !candidate.IsEffectivelyEnabled || !candidate.IsEffectivelyVisible || candidate.Bounds.Width <= 0
                || ReferenceEquals(candidate, source) || source.IsVisualAncestorOf(candidate) || candidate.IsVisualAncestorOf(source)
                || candidate.TranslatePoint(default, region) is not { } position)
            {
                continue;
            }

            var rect = new Rect(position, candidate.Bounds.Size);
            double gap = down ? rect.Center.Y - sourceRect.Center.Y : sourceRect.Center.Y - rect.Center.Y;
            if (gap <= 4)
            {
                continue;
            }

            double dx = Math.Abs(rect.Center.X - sourceRect.Center.X);
            bool nearerRow = gap < bestGap - 8;
            bool sameRowCloserColumn = Math.Abs(gap - bestGap) <= 8 && dx < bestDx;
            if (best is null || nearerRow || sameRowCloserColumn)
            {
                best = candidate;
                bestGap = Math.Min(gap, bestGap);
                bestDx = dx;
            }
        }

        return best;
    }

    /// <summary>Search options for a directional move made inside <paramref name="region"/>: only the region's own controls are candidates
    /// (never the nav rail, status bar or a hidden overlay), and controls scrolled out of view still count - the move scrolls them in.</summary>
    public static FindNextElementOptions InRegion(InputElement region) => new() { SearchRoot = region, IgnoreOcclusivity = true };

    /// <summary>Scrolls <paramref name="control"/> into view with room for its focus ring (PbGlowRing spreads 4px, the tile pop adds ~2px), so
    /// a move to the edge of a shelf or the page never leaves the ring cut off at the viewport edge.</summary>
    public static void BringIntoViewWithRing(Control control) => control.BringIntoView(new Rect(control.Bounds.Size).Inflate(12));
}
