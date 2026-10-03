using System;
using Avalonia;
using Avalonia.Input;

namespace Paperbunkr.App.Services.Input;

/// <summary>Where an action came from.</summary>
public enum InputDevice
{
    Keyboard,
    Mouse,
    Gamepad,

    /// <summary>Dispatched in code (a toolbar button, the command palette, a plugin) rather than by a physical input.</summary>
    Programmatic,
}

/// <summary>
/// The extra data a programmatic <see cref="IInputService.Dispatch"/> can carry. Everything is optional; <c>default</c> means "a plain button press".
/// </summary>
/// <param name="Value">Axis value in -1..1; <see langword="null"/> means 1 (a plain press).</param>
/// <param name="WheelDelta">Wheel or tilt amount, for wheel-shaped actions.</param>
/// <param name="Modifiers">Modifier keys to report with the action.</param>
/// <param name="PositionResolver">Resolves the cursor position relative to a visual, for pointer-shaped actions; see <see cref="InputActionEventArgs.GetPosition"/>.</param>
public readonly record struct InputPayload(
    double? Value = null,
    Vector WheelDelta = default,
    KeyModifiers Modifiers = KeyModifiers.None,
    Func<Visual, Point>? PositionResolver = null);

/// <summary>
/// What a handler receives when an <see cref="InputAction"/> fires (docs/superpowers/specs/2026-10-03-input-service-design.md §5.5). A handler that
/// takes the action sets <see cref="Handled"/>, which stops the search for another handler and lets the service mark the physical event handled; an
/// action nobody claims leaves the event alone, so keys the app does not use keep working.
/// </summary>
public sealed class InputActionEventArgs : EventArgs
{
    private Func<Visual, Point>? _positionResolver;

    /// <param name="action">The action that fired.</param>
    /// <param name="device">What produced it.</param>
    /// <param name="value">Axis value in -1..1; 1 for a plain button press.</param>
    /// <param name="wheelDelta">Wheel or tilt amount; zero for everything else.</param>
    /// <param name="modifiers">Modifier keys held when it fired.</param>
    /// <param name="positionResolver">
    /// For pointer-derived actions, the adapter's <c>e.GetPosition(visual)</c>; <see langword="null"/> for keyboard, gamepad and programmatic dispatch.
    /// </param>
    /// <param name="elapsed">Time since the previous gamepad poll; only meaningful for gamepad axis actions, which scale their effect by it.</param>
    public InputActionEventArgs(
        InputAction action,
        InputDevice device,
        double value = 1,
        Vector wheelDelta = default,
        KeyModifiers modifiers = KeyModifiers.None,
        Func<Visual, Point>? positionResolver = null,
        TimeSpan elapsed = default,
        InputBinding binding = default)
    {
        Action = action;
        Device = device;
        Value = value;
        WheelDelta = wheelDelta;
        Modifiers = modifiers;
        _positionResolver = positionResolver;
        Elapsed = elapsed;
        Binding = binding;
    }

    /// <summary>The physical input that triggered the action (<see langword="default"/> for programmatic dispatch), for handlers that treat devices differently or want to log which key was used.</summary>
    public InputBinding Binding { get; }

    /// <summary>Time since the previous gamepad poll, so a continuous axis (pan, zoom) can scale its effect to the frame; zero for every other source.</summary>
    public TimeSpan Elapsed { get; }

    public InputAction Action { get; }

    public InputDevice Device { get; }

    /// <summary>Axis value in -1..1 for <see cref="InputActionKind.Axis"/> actions; 1 for a button press.</summary>
    public double Value { get; }

    /// <summary>Wheel or horizontal-tilt amount; <see cref="Vector"/> zero for everything else.</summary>
    public Vector WheelDelta { get; }

    public KeyModifiers Modifiers { get; }

    /// <summary>Set by the handler that takes the action. Once set, no further handler is tried and the physical event is marked handled.</summary>
    public bool Handled { get; set; }

    /// <summary>
    /// For pointer-derived actions (mouse button, wheel, tilt): the cursor position in <paramref name="relativeTo"/>'s own coordinate space, so a canvas
    /// passes <c>this</c> and gets its zoom anchor in canvas coordinates with no window-transform arithmetic. <see langword="null"/> for keyboard,
    /// gamepad and programmatic actions. Only valid during the synchronous handler call: once dispatch finishes it returns <see langword="null"/>, so a
    /// handler that defers work (e.g. with <c>Dispatcher.UIThread.Post</c>) must read the position first.
    /// </summary>
    public Point? GetPosition(Visual relativeTo)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);
        return _positionResolver?.Invoke(relativeTo);
    }

    /// <summary>Called by the service when dispatch is over; drops the reference to the physical event so a late call cannot read a stale position.</summary>
    internal void Complete() => _positionResolver = null;
}
