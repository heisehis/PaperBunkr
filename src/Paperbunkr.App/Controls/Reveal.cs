using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Theming.Motion;

namespace Paperbunkr.App.Controls;

/// <summary>
/// The Reveal open/close lifecycle (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md §5.1–5.2), shared by
/// <see cref="OverlayShell"/> and anything that sets <c>controls:Reveal.IsOpen</c> (the Activity drawer, the Reading Lists drawer).
///
/// Open: visible, interactive, class <c>open</c> - the styles animate in on the Reveal tokens. Close never waits: in the same tick the
/// control stops taking pointer input, leaves tab traversal, gives up focus and gets class <c>closing</c> (styles swap in the faster exit
/// and the accelerating curve); it collapses (<c>IsVisible = false</c>) only once the Reveal exit has elapsed. Reopening mid-exit bumps a
/// generation so the pending collapse is dropped and the transitions reverse from where they are. A zero exit (Motion Off), a control not
/// in a window, or one already hidden collapses synchronously. Torn down mid-exit, it finishes the close at once. The collapse runs from
/// a timer, never inside a routed event, so closing from a button inside the surface cannot detach that button mid-Click (CLAUDE.md).
/// </summary>
public static class Reveal
{
    /// <summary><c>bool?</c> so the first bound value - even <c>false</c> - always arrives as a change and sets the initial state.</summary>
    public static readonly AttachedProperty<bool?> IsOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool?>("IsOpen", typeof(Reveal));

    /// <summary>Where focus goes when the surface closes with focus inside it - the button that opened it (Motion v2 E, Q26). Unset, or
    /// not focusable at that moment, and focus is simply released.</summary>
    public static readonly AttachedProperty<Control?> ReturnFocusToProperty =
        AvaloniaProperty.RegisterAttached<Control, Control?>("ReturnFocusTo", typeof(Reveal));

    public static Control? GetReturnFocusTo(Control control) => control.GetValue(ReturnFocusToProperty);

    public static void SetReturnFocusTo(Control control, Control? value) => control.SetValue(ReturnFocusToProperty, value);

    /// <summary>Runs the collapse after the exit. A seam because the headless test dispatcher never fires timers.</summary>
    internal static Func<Action, TimeSpan, IDisposable> ScheduleAfter { get; set; } = (action, delay) => DispatcherTimer.RunOnce(action, delay);

    private sealed class State
    {
        public int Generation;
        public IDisposable? Pending;
    }

    private static readonly ConditionalWeakTable<Control, State> States = new();

    static Reveal()
    {
        IsOpenProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is bool open)
            {
                Apply(control, open);
            }
        });
    }

    public static bool? GetIsOpen(Control control) => control.GetValue(IsOpenProperty);

    public static void SetIsOpen(Control control, bool? value) => control.SetValue(IsOpenProperty, value);

    /// <summary>True between a close and the end of its exit: still drawn, no longer interactive.</summary>
    public static bool IsClosing(Control control) => control.Classes.Contains("closing");

    /// <summary>Drives <paramref name="control"/> to the open or closed state. <paramref name="beforeOpenClass"/> runs once the control is
    /// visible and before <c>open</c> is added (OverlayShell realizes deferred content there).</summary>
    public static void Apply(Control control, bool open, Action? beforeOpenClass = null)
    {
        var state = States.GetOrCreateValue(control);
        state.Generation++;
        Cancel(state);

        if (open)
        {
            control.Classes.Remove("closing");
            KeyboardNavigation.SetTabNavigation(control, KeyboardNavigationMode.Continue);
            control.IsHitTestVisible = true;
            control.IsVisible = true;
            beforeOpenClass?.Invoke();

            // Already in the tree with its closed-state values styled, so the transitions see the change and animate.
            control.Classes.Add("open");
            return;
        }

        control.Classes.Remove("open");
        TimeSpan exit = MotionPolicy.Resolve(MotionRole.Reveal).Exit;
        if (exit <= TimeSpan.Zero || !control.IsAttachedToVisualTree() || !control.IsVisible)
        {
            // Hiding takes focus with it; if it was inside, it still goes back to the opener.
            bool hadFocus = FocusIsInside(control);
            Collapse(control);
            if (hadFocus)
            {
                ReturnFocus(control);
            }

            return;
        }

        // Interactive state ends now; only the drawing lingers for the exit.
        control.Classes.Add("closing");
        control.IsHitTestVisible = false;
        KeyboardNavigation.SetTabNavigation(control, KeyboardNavigationMode.None);
        ReleaseFocusInside(control);

        int generation = state.Generation;
        state.Pending = ScheduleAfter(() =>
        {
            if (generation == state.Generation)
            {
                state.Pending = null;
                Collapse(control);
            }
        }, exit);

        control.DetachedFromVisualTree -= OnDetachedMidExit;
        control.DetachedFromVisualTree += OnDetachedMidExit;
    }

    private static void OnDetachedMidExit(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
        {
            control.DetachedFromVisualTree -= OnDetachedMidExit;
            if (IsClosing(control))
            {
                if (States.TryGetValue(control, out var state))
                {
                    Cancel(state);
                }

                Collapse(control);
            }
        }
    }

    private static void Cancel(State state)
    {
        state.Pending?.Dispose();
        state.Pending = null;
    }

    private static void Collapse(Control control)
    {
        control.DetachedFromVisualTree -= OnDetachedMidExit;
        control.Classes.Remove("closing");
        KeyboardNavigation.SetTabNavigation(control, KeyboardNavigationMode.Continue);
        control.IsHitTestVisible = true;
        control.IsVisible = false;
    }

    /// <summary>
    /// Hiding the surface used to take focus with it (to nothing); a closing surface is still visible, so it gives focus up itself, or a
    /// key press could reach a button in a dialog already on its way out. Avalonia 12 has no public "clear focus" (FocusManager.Focus
    /// ignores null, the KeyboardDevice is not reachable), but disabling an ancestor clears it exactly as hiding does. The disable is an
    /// Animation-priority value released in the same tick: no frame renders in between, so no disabled style flashes, and any IsEnabled
    /// binding is untouched.
    /// </summary>
    private static void ReleaseFocusInside(Control control)
    {
        if (FocusIsInside(control))
        {
            control.SetValue(InputElement.IsEnabledProperty, false, BindingPriority.Animation)?.Dispose();
            ReturnFocus(control);
        }
    }

    private static bool FocusIsInside(Control control) =>
        TopLevel.GetTopLevel(control)?.FocusManager?.GetFocusedElement() is Visual focused
        && (ReferenceEquals(focused, control) || control.IsVisualAncestorOf(focused));

    private static void ReturnFocus(Control control)
    {
        if (GetReturnFocusTo(control) is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } opener)
        {
            opener.Focus(NavigationMethod.Tab);
        }
    }
}
