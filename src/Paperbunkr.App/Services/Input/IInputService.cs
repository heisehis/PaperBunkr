using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// The single place physical input becomes an <see cref="InputAction"/> (docs/superpowers/specs/2026-10-03-input-service-design.md §5.5). <c>MainWindow</c>
/// forwards its Tunnel key, wheel and pointer-pressed events here, the gamepad poller forwards controller snapshots, and screens, ViewModels and controls
/// <see cref="Register"/> handlers for the actions they own. Nothing outside this service matches keys, wheel deltas or buttons against bindings.
/// </summary>
public interface IInputService
{
    /// <summary>
    /// Raised once for every action that resolves, after its handlers have run, for observers that only want to watch (status line, usage tracking).
    /// <see cref="InputActionEventArgs.Handled"/> shows whether any handler claimed it. Handlers that act on an action should use <see cref="Register"/>.
    /// </summary>
    event EventHandler<InputActionEventArgs>? ActionTriggered;

    /// <summary>Raised after any binding is added, changed or reset, including a reload from disk.</summary>
    event EventHandler? BindingsChanged;

    /// <summary>The actions the app knows about (built-in and plugin-registered).</summary>
    IInputActionCatalog Actions { get; }

    /// <summary>Thresholds and timings in force (swipe size, stick and trigger dead zones, repeat rate); the controller poller reads them too.</summary>
    InputTuning Tuning { get; }

    // ----- Avalonia entry points: MainWindow forwards its Tunnel events here. Each sets e.Handled when an action is claimed. -----

    /// <summary>Resolves a key press. Ignored while a focused control suppresses hotkeys (see <see cref="IInputSuppressor"/>). Returns true when an action claimed it.</summary>
    bool ProcessKeyDown(KeyEventArgs e);

    /// <summary>
    /// Resolves a key that did not arrive as an Avalonia event: one forwarded from an embedded web view, whose keyboard Avalonia never sees. No control is focused as far as the service
    /// can tell, so nothing is suppressed. Returns true when an action claimed it.
    /// </summary>
    bool ProcessKey(Key key, KeyModifiers modifiers);

    /// <summary>Resolves a wheel turn or horizontal tilt (e.g. Ctrl+wheel for zoom). Returns true when an action claimed it.</summary>
    bool ProcessPointerWheel(PointerWheelEventArgs e);

    /// <summary>Resolves a mouse-button press (the thumb buttons, extra buttons, double-clicks). Returns true when an action claimed it.</summary>
    bool ProcessPointerPressed(PointerPressedEventArgs e);

    // ----- Gamepad entry point: the poller (or a test) hands over one controller snapshot at a time. -----

    /// <summary>
    /// Runs one controller snapshot through edge detection and key-repeat, resolves buttons like any other binding, and emits axis actions (stick, triggers)
    /// with a continuous <see cref="InputActionEventArgs.Value"/> while non-zero. Returns true when any action was claimed.
    /// </summary>
    bool ProcessGamepad(GamepadState state, TimeSpan elapsed);

    /// <summary>Forgets held pad buttons and repeat timers; the poller calls it when the controller disappears or it stops, so nothing sticks.</summary>
    void ResetGamepad();

    // ----- Programmatic dispatch: toolbar buttons, the command palette, plugins, future devices. -----

    /// <summary>Fires <paramref name="action"/> as if its binding had been pressed, through the same scopes, contexts and handlers. Returns true when claimed.</summary>
    bool Dispatch(InputAction action, InputPayload payload = default);

    // ----- Scope activation and handler attachment, in one call. -----

    /// <summary>
    /// Attaches <paramref name="handler"/> to <paramref name="scope"/> and keeps that scope active until the returned token is disposed. This is the only
    /// lifecycle call: a screen, ViewModel or control holds one <see cref="IDisposable"/> and has nothing else to keep in step. A scope is active while it
    /// has at least one live registration, so tokens may be disposed in any order, and more than once.
    /// </summary>
    /// <param name="scope">The scope the handler belongs to.</param>
    /// <param name="handler">Called for each candidate action in the scope; set <see cref="InputActionEventArgs.Handled"/> to take it.</param>
    /// <param name="context">
    /// Reports the scope's current <see cref="InputContext"/> (e.g. the reader's paged/zoomed/continuous state), read at resolution time; omit when the
    /// scope has no sub-states, which counts as <see cref="InputContext.Always"/>. Returning <see cref="InputContext.None"/> makes the registration
    /// <em>dormant</em> for that resolution: it neither activates its scope nor receives anything. That is how a screen that is hidden rather than destroyed stays
    /// registered for its whole life yet is never asked while it is off screen (<c>() =&gt; IsEffectivelyVisible ? InputContext.Always : InputContext.None</c>).
    /// </param>
    IDisposable Register(InputScope scope, Action<InputActionEventArgs> handler, Func<InputContext>? context = null);

    // ----- Dynamic binding management: Preferences, import and export. -----

    /// <summary>The action's current bindings: the user's override if there is one, otherwise the defaults. Empty when it is deliberately unbound.</summary>
    IReadOnlyList<InputBinding> GetBindings(InputAction action);

    /// <summary>Replaces all of the action's bindings (an empty list unbinds it) and saves.</summary>
    void SetBindings(InputAction action, IReadOnlyList<InputBinding> bindings);

    /// <summary>Drops the user's override for one action, restoring its default bindings.</summary>
    void ResetBindings(InputAction action);

    /// <summary>Drops every override, restoring the default keymap.</summary>
    void ResetAll();

    /// <summary>Other actions that already use <paramref name="binding"/> and could be active together with <paramref name="action"/> (same scope reachable at once, overlapping contexts).</summary>
    IReadOnlyList<InputConflict> FindConflicts(InputAction action, InputBinding binding);
}
