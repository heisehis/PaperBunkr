using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Keeps a text box from trapping the keyboard when it is reached with the arrow keys or a controller (docs/superpowers/specs/2026-10-03-input-service-design.md §14.4). A box focused by
/// <see cref="NavigationMethod.Directional"/> starts out <em>browsing</em>: it does not take typing, its caret does not take the arrows (so the next arrow press moves on), and it is
/// edited only after Enter or F2 (or a click), the way a dropdown opens only on Enter or Space. Esc puts it back to browsing, and leaving it resets it. Tab and a click enter a box ready
/// to type, as before, so filling in a form is unchanged.
/// <para>
/// A browsing box is made read-only for the time (so typing, paste and delete do nothing without any key being swallowed), and the arrow keys never reach it: they are handed to the box's parent
/// as if from a control that has no use for them, so the screen's own directional move (and the shell's, towards the nav rail) runs. A read-only box has no caret worth moving, so it gets the same.
/// </para>
/// </summary>
public static class TextEntryMode
{
    private sealed class State
    {
        public bool Browsing;
        public bool EnteredFromBrowse;
        public bool WasReadOnly;
    }

    private static readonly ConditionalWeakTable<TextBox, State> States = new();
    private static bool _installed;

    [ThreadStatic]
    private static int _redirecting;

    /// <summary>True while an arrow key is being handed on from a text box (the input host must not process it a second time).</summary>
    public static bool IsRedirecting => _redirecting > 0;

    /// <summary>True when <paramref name="box"/> is focused but not taking input.</summary>
    public static bool IsBrowsing(TextBox box) => States.TryGetValue(box, out var state) && state.Browsing;

    /// <summary>Hooks every text box in the application. Safe to call more than once.</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        InputElement.GotFocusEvent.AddClassHandler<TextBox>((box, e) =>
        {
            if (ReferenceEquals(e.Source, box) && e.NavigationMethod == NavigationMethod.Directional && !box.IsReadOnly && box.IsEnabled)
            {
                StartBrowsing(box);
            }
        });
        InputElement.LostFocusEvent.AddClassHandler<TextBox>((box, _) => Reset(box));
    }

    /// <summary>Starts browsing: no typing, and the arrows move on.</summary>
    public static void StartBrowsing(TextBox box)
    {
        var state = States.GetOrCreateValue(box);
        if (!state.Browsing)
        {
            state.WasReadOnly = box.IsReadOnly;
            box.SetCurrentValue(TextBox.IsReadOnlyProperty, true);
        }

        state.Browsing = true;
        state.EnteredFromBrowse = false;
    }

    /// <summary>Starts editing a browsing box: typing works again and the caret goes to the end of the text.</summary>
    public static void StartEditing(TextBox box)
    {
        if (!States.TryGetValue(box, out var state) || !state.Browsing)
        {
            return;
        }

        box.SetCurrentValue(TextBox.IsReadOnlyProperty, state.WasReadOnly);
        state.Browsing = false;
        state.EnteredFromBrowse = true;
        box.CaretIndex = box.Text?.Length ?? 0;
    }

    private static void Reset(TextBox box)
    {
        if (!States.TryGetValue(box, out var state))
        {
            return;
        }

        if (state.Browsing)
        {
            box.SetCurrentValue(TextBox.IsReadOnlyProperty, state.WasReadOnly);
        }

        state.Browsing = false;
        state.EnteredFromBrowse = false;
    }

    /// <summary>
    /// The window's key handler for text boxes, run before the input service sees the key. Returns true when the key was used.
    /// </summary>
    public static bool HandleKeyDown(TopLevel top, KeyEventArgs e)
    {
        if (_redirecting > 0 || e.KeyModifiers != KeyModifiers.None || top.FocusManager?.GetFocusedElement() is not TextBox box)
        {
            return false;
        }

        if (States.TryGetValue(box, out var state) && state.Browsing)
        {
            if (e.Key is Key.Enter or Key.F2)
            {
                StartEditing(box);
                return true;
            }

            return IsArrow(e.Key) && Redirect(top, box, e.Key);
        }

        // Entered with Enter and now editing: Esc goes back to browsing instead of closing whatever is behind the box.
        if (state is { EnteredFromBrowse: true } && e.Key == Key.Escape && !box.IsReadOnly)
        {
            StartBrowsing(box);
            return true;
        }

        // A read-only box (a strict dropdown's display) has no caret worth moving, so the arrows go on, except while its list is open and uses them.
        if (box.IsReadOnly && IsArrow(e.Key) && !IsInsideOpenDropDown(box))
        {
            return Redirect(top, box, e.Key);
        }

        return false;
    }

    /// <summary>A press inside a browsing box is a click to edit it: the click places the caret as usual.</summary>
    public static void HandlePointerPressed(TopLevel top)
    {
        if (top.FocusManager?.GetFocusedElement() is TextBox box && States.TryGetValue(box, out var state) && state.Browsing)
        {
            StartEditing(box);
        }
    }

    private static bool IsArrow(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down;

    private static bool IsInsideOpenDropDown(TextBox box)
    {
        for (var v = box.GetVisualParent(); v is not null; v = v.GetVisualParent())
        {
            if (v is SuggestBox suggest)
            {
                return suggest.IsDropDownOpen;
            }
        }

        return false;
    }

    private static bool Redirect(TopLevel top, TextBox box, Key key)
    {
        if (box.GetVisualParent() is not InputElement parent)
        {
            return false;
        }

        _redirecting++;
        try
        {
            var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.None, Source = parent, KeyDeviceType = KeyDeviceType.Keyboard };
            parent.RaiseEvent(args);
            if (!args.Handled)
            {
                UiNavigation.Move(top, key switch
                {
                    Key.Left => NavigationDirection.Left,
                    Key.Right => NavigationDirection.Right,
                    Key.Up => NavigationDirection.Up,
                    _ => NavigationDirection.Down,
                });
            }
        }
        finally
        {
            _redirecting--;
        }

        return true;
    }
}
