using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Services.Input;

/// <summary>One thing the controller did in a poll: a digital input that fired (an edge, or a repeat while held; <see cref="Value"/> is 1) or an analogue axis reading in -1..1.</summary>
public readonly record struct PadSignal(GamepadInput Input, double Value);

/// <summary>
/// Turns successive controller snapshots into <see cref="PadSignal"/>s (docs/superpowers/specs/2026-10-03-input-service-design.md §5.5); the service resolves each signal
/// through the keymap like any other binding. This is <c>GamepadMapper</c>'s logic with the fixed Xbox layout removed: the same edge detection, the same key-repeat for
/// held turn/direction buttons, the same radial stick dead zone and trigger dead zone, with the numbers coming from <see cref="InputTuning"/>. Pure and stateful only for
/// edge detection and repeat timing, so tests drive it with hand-built snapshots.
/// </summary>
public sealed class GamepadInputProcessor
{
    // Digital inputs that repeat while held. Everything else digital (Start, Back, X, Y, the stick clicks) fires once per press.
    private static readonly GamepadInput[] RepeatingInputs =
    [
        GamepadInput.DPadUp, GamepadInput.DPadDown, GamepadInput.DPadLeft, GamepadInput.DPadRight,
        GamepadInput.A, GamepadInput.B, GamepadInput.LeftShoulder, GamepadInput.RightShoulder,
        GamepadInput.LeftStickLeft, GamepadInput.LeftStickRight, GamepadInput.LeftStickUp, GamepadInput.LeftStickDown,
    ];

    private static readonly (GamepadInput Input, GamepadButtons Button)[] EdgeInputs =
    [
        (GamepadInput.Start, GamepadButtons.Start),
        (GamepadInput.Back, GamepadButtons.Back),
        (GamepadInput.X, GamepadButtons.X),
        (GamepadInput.Y, GamepadButtons.Y),
        (GamepadInput.LeftThumb, GamepadButtons.LeftThumb),
        (GamepadInput.RightThumb, GamepadButtons.RightThumb),
    ];

    private sealed class Repeater
    {
        private bool _held;
        private double _heldMs;
        private double _sinceLastMs;

        /// <summary>True on the poll where the input goes down, and again on each repeat while held.</summary>
        public bool Update(bool down, double elapsedMs, InputTuning tuning)
        {
            if (!down)
            {
                _held = false;
                return false;
            }

            if (!_held)
            {
                _held = true;
                _heldMs = 0;
                _sinceLastMs = 0;
                return true;
            }

            _heldMs += elapsedMs;
            if (_heldMs < tuning.RepeatInitialMs)
            {
                return false;
            }

            _sinceLastMs += elapsedMs;
            if (_sinceLastMs < tuning.RepeatIntervalMs)
            {
                return false;
            }

            _sinceLastMs = 0;
            return true;
        }
    }

    private readonly Dictionary<GamepadInput, Repeater> _repeaters = [];
    private GamepadButtons _previousButtons;

    /// <summary>Forgets held buttons (call when the controller changes or disappears, so nothing sticks).</summary>
    public void Reset()
    {
        _repeaters.Clear();
        _previousButtons = GamepadButtons.None;
    }

    /// <summary>Processes one snapshot and returns the signals it produced, digital ones first.</summary>
    public IReadOnlyList<PadSignal> Update(GamepadState state, TimeSpan elapsed, InputTuning tuning)
    {
        var signals = new List<PadSignal>();
        double ms = elapsed.TotalMilliseconds;
        var buttons = state.Buttons;

        double lx = Normalize(state.LeftX);
        double ly = Normalize(state.LeftY);

        foreach (var input in RepeatingInputs)
        {
            if (!_repeaters.TryGetValue(input, out var repeater))
            {
                _repeaters[input] = repeater = new Repeater();
            }

            if (repeater.Update(IsDown(input, buttons, lx, ly, tuning), ms, tuning))
            {
                signals.Add(new PadSignal(input, 1));
            }
        }

        foreach (var (input, button) in EdgeInputs)
        {
            if (buttons.HasFlag(button) && !_previousButtons.HasFlag(button))
            {
                signals.Add(new PadSignal(input, 1));
            }
        }

        _previousButtons = buttons;

        // Analogue axes. Up on a stick is positive in XInput; the axis values here are right and down positive, like the screen.
        var (leftX, leftUp) = ApplyRadialDeadZone(lx, ly, tuning.StickDeadZone);
        var (rightX, rightUp) = ApplyRadialDeadZone(Normalize(state.RightX), Normalize(state.RightY), tuning.StickDeadZone);
        double triggers = TriggerAxis(state.RightTrigger, tuning.TriggerDeadZone) - TriggerAxis(state.LeftTrigger, tuning.TriggerDeadZone);

        AddAxis(signals, GamepadInput.LeftStickX, leftX);
        AddAxis(signals, GamepadInput.LeftStickY, -leftUp);
        AddAxis(signals, GamepadInput.RightStickX, rightX);
        AddAxis(signals, GamepadInput.RightStickY, -rightUp);
        AddAxis(signals, GamepadInput.Triggers, triggers);
        return signals;
    }

    private static void AddAxis(List<PadSignal> signals, GamepadInput input, double value)
    {
        if (value != 0)
        {
            signals.Add(new PadSignal(input, value));
        }
    }

    private static bool IsDown(GamepadInput input, GamepadButtons buttons, double lx, double ly, InputTuning tuning) => input switch
    {
        GamepadInput.DPadUp => buttons.HasFlag(GamepadButtons.DPadUp),
        GamepadInput.DPadDown => buttons.HasFlag(GamepadButtons.DPadDown),
        GamepadInput.DPadLeft => buttons.HasFlag(GamepadButtons.DPadLeft),
        GamepadInput.DPadRight => buttons.HasFlag(GamepadButtons.DPadRight),
        GamepadInput.A => buttons.HasFlag(GamepadButtons.A),
        GamepadInput.B => buttons.HasFlag(GamepadButtons.B),
        GamepadInput.LeftShoulder => buttons.HasFlag(GamepadButtons.LeftShoulder),
        GamepadInput.RightShoulder => buttons.HasFlag(GamepadButtons.RightShoulder),
        GamepadInput.LeftStickLeft => lx <= -tuning.StickDigitalThreshold,
        GamepadInput.LeftStickRight => lx >= tuning.StickDigitalThreshold,
        GamepadInput.LeftStickUp => ly >= tuning.StickDigitalThreshold,
        GamepadInput.LeftStickDown => ly <= -tuning.StickDigitalThreshold,
        _ => false,
    };

    private static double Normalize(short value) => Math.Clamp(value / 32767.0, -1, 1);

    /// <summary>Scales a stick vector so that inside the dead zone it is zero and it rises smoothly to 1 at full deflection, keeping its direction.</summary>
    internal static (double X, double Y) ApplyRadialDeadZone(double x, double y, double deadZone)
    {
        double magnitude = Math.Sqrt((x * x) + (y * y));
        if (magnitude < deadZone)
        {
            return (0, 0);
        }

        double scaled = Math.Min(1, (magnitude - deadZone) / (1 - deadZone));
        return (x / magnitude * scaled, y / magnitude * scaled);
    }

    internal static double TriggerAxis(byte value, double deadZone)
    {
        double fraction = value / 255.0;
        return fraction < deadZone ? 0 : (fraction - deadZone) / (1 - deadZone);
    }
}
