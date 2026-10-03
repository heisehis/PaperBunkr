using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// What the "every screen" actions (<see cref="InputActionIds.FocusUp"/>, <see cref="InputActionIds.Activate"/>, ...) do (docs/superpowers/specs/2026-10-03-input-service-design.md §14). They
/// do not know about any screen: each sends the focused control the key it already understands (Enter opens a tile, Delete removes a row, F2 renames, the arrows move through a
/// grid), so a controller button, a mouse button or a user-chosen key reaches exactly the code the keyboard does. When nothing takes the key, moving falls back to a plain
/// directional focus move so the D-pad still gets somewhere on a screen with no arrow handling of its own.
/// </summary>
public static class UiNavigation
{
    /// <summary>Pixels per second a full right-stick deflection scrolls.</summary>
    public const double ScrollSpeed = 1100;

    [ThreadStatic]
    private static int _sending;

    /// <summary>
    /// True while a key sent by this class is being routed. A user who binds the real Enter key to <see cref="InputActionIds.Activate"/> would otherwise have the sent Enter resolve to the
    /// action again; handlers of the "every screen" actions return without claiming while this is set, and the key then reaches the control as the keyboard would deliver it.
    /// </summary>
    public static bool IsSending => _sending > 0;

    /// <summary>
    /// Runs the "every screen" action in <paramref name="e"/> against <paramref name="top"/>: move, activate, toggle, rename, delete, context menu and the two scroll axes. Returns true when
    /// it did something (so the action counts as claimed); false for any other action, or while one of these is already being sent (see <see cref="IsSending"/>).
    /// </summary>
    public static bool TryHandle(TopLevel top, InputActionEventArgs e)
    {
        if (IsSending)
        {
            return false;
        }

        return e.Action.Id switch
        {
            InputActionIds.FocusUp => Move(top, NavigationDirection.Up),
            InputActionIds.FocusDown => Move(top, NavigationDirection.Down),
            InputActionIds.FocusLeft => Move(top, NavigationDirection.Left),
            InputActionIds.FocusRight => Move(top, NavigationDirection.Right),
            InputActionIds.Activate => SendKey(top, Key.Enter),
            InputActionIds.ToggleSelect => SendKey(top, Key.Space),
            InputActionIds.RenameItem => SendKey(top, Key.F2),
            InputActionIds.DeleteItem => SendKey(top, Key.Delete),
            InputActionIds.ContextMenu => SendKey(top, Key.Apps),
            InputActionIds.FocusFirst => SendKey(top, Key.Home),
            InputActionIds.FocusLast => SendKey(top, Key.End),
            InputActionIds.ItemPageUp => SendKey(top, Key.PageUp),
            InputActionIds.ItemPageDown => SendKey(top, Key.PageDown),
            InputActionIds.ScrollVertical => Scroll(top, 0, e.Value, e.Elapsed),
            InputActionIds.ScrollHorizontal => Scroll(top, e.Value, 0, e.Elapsed),
            _ => false,
        };
    }

    /// <summary>
    /// Sends <paramref name="key"/> (key down then key up) to the focused element of <paramref name="top"/>. Returns true when something handled the key down. Does nothing, and returns
    /// false, when no element has focus.
    /// </summary>
    public static bool SendKey(TopLevel top, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (top.FocusManager?.GetFocusedElement() is not InputElement target)
        {
            return false;
        }

        _sending++;
        try
        {
            var down = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = target, KeyDeviceType = KeyDeviceType.Keyboard };
            target.RaiseEvent(down);
            var up = new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers, Source = target, KeyDeviceType = KeyDeviceType.Keyboard };
            target.RaiseEvent(up);
            return down.Handled;
        }
        finally
        {
            _sending--;
        }
    }

    /// <summary>
    /// One step in <paramref name="direction"/>. A control that uses the arrow itself (a grid, a list, a slider, a dropdown) gets the key; otherwise focus moves to the nearest element that
    /// way. A text box keeps Left and Right for its caret only when the key is typed, so the pad always leaves it. With nothing focused, focus lands on the first element instead.
    /// </summary>
    public static bool Move(TopLevel top, NavigationDirection direction)
    {
        var manager = top.FocusManager;
        if (manager is null)
        {
            return false;
        }

        var focused = manager.GetFocusedElement();
        if (focused is null)
        {
            return FocusFirst(top);
        }

        if (focused is not TextBox)
        {
            var key = direction switch
            {
                NavigationDirection.Left => Key.Left,
                NavigationDirection.Right => Key.Right,
                NavigationDirection.Up => Key.Up,
                _ => Key.Down,
            };
            if (SendKey(top, key))
            {
                return true;
            }
        }

        if (manager.FindNextElement(direction, new FindNextElementOptions { IgnoreOcclusivity = true }) is Control next)
        {
            next.Focus(NavigationMethod.Directional);
            FocusReclaimer.BringIntoViewWithRing(next);
            return true;
        }

        return false;
    }

    /// <summary>Puts focus on the first focusable element (what a D-pad press does before anything has focus).</summary>
    public static bool FocusFirst(TopLevel top)
    {
        if (top.FocusManager?.FindNextElement(NavigationDirection.Next) is Control first)
        {
            first.Focus(NavigationMethod.Directional);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Scrolls the nearest scroll viewer around the focused element (or, with nothing focused, the first one on screen) by <paramref name="dx"/> and <paramref name="dy"/>, each a stick
    /// deflection from -1 to 1, over <paramref name="elapsed"/>. Returns true when there was one that could move that way.
    /// </summary>
    public static bool Scroll(TopLevel top, double dx, double dy, TimeSpan elapsed)
    {
        var viewer = FindScrollViewer(top, dx != 0, dy != 0);
        if (viewer is null)
        {
            return false;
        }

        double seconds = Math.Clamp(elapsed.TotalSeconds, 0.001, 0.1);
        var before = viewer.Offset;
        viewer.Offset = new Vector(before.X + dx * ScrollSpeed * seconds, before.Y + dy * ScrollSpeed * seconds);
        return viewer.Offset != before;
    }

    private static ScrollViewer? FindScrollViewer(TopLevel top, bool horizontal, bool vertical)
    {
        bool CanScroll(ScrollViewer v) =>
            v.IsEffectivelyVisible
            && ((vertical && v.Extent.Height > v.Viewport.Height + 0.5) || (horizontal && v.Extent.Width > v.Viewport.Width + 0.5));

        if (top.FocusManager?.GetFocusedElement() is Visual focused)
        {
            return focused.GetSelfAndVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(CanScroll);
        }

        return top.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(CanScroll);
    }
}
