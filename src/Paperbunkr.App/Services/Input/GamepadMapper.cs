using System;

namespace Paperbunkr.App.Services.Input;

/// <summary>XInput button bits (the values of <c>XINPUT_GAMEPAD_*</c>).</summary>
[Flags]
public enum GamepadButtons : ushort
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

/// <summary>One snapshot of a controller: buttons, analogue triggers (0-255) and sticks (-32768..32767, up and right positive).</summary>
public readonly record struct GamepadState(GamepadButtons Buttons, byte LeftTrigger, byte RightTrigger, short LeftX, short LeftY, short RightX, short RightY);

/// <summary>
/// What the controller asked the reader to do in one poll. The digital members are edges (with repeat while held); <see cref="PanX"/>, <see cref="PanY"/> and
/// <see cref="Zoom"/> are analogue values in -1..1 (pan: right and down positive; zoom: in positive) already past the dead zone.
/// </summary>
public readonly record struct GamepadFrame(
    bool Next, bool Previous, bool Left, bool Right, bool Up, bool Down,
    bool ToggleChrome, bool Fullscreen, bool Palette, bool Leave,
    double PanX, double PanY, double Zoom)
{
    public bool HasAny =>
        Next || Previous || Left || Right || Up || Down || ToggleChrome || Fullscreen || Palette || Leave
        || PanX != 0 || PanY != 0 || Zoom != 0;
}

/// <summary>
/// Turns successive controller snapshots into reader actions (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md section 3). Pure and stateful only for
/// edge detection and key-repeat timing, so it is tested with hand-built snapshots. Defaults, for an Xbox layout: <b>A</b> / right bumper = next page, <b>B</b> / left bumper =
/// previous page, D-pad and left stick = direction (page turn or pan, decided by the reader), <b>right stick</b> = pan or scroll, triggers = zoom (right in, left out),
/// <b>Y</b> toggle the toolbar, <b>X</b> fullscreen, <b>Start</b> the command palette, <b>Back</b> leave the reader.
/// </summary>
public sealed class GamepadMapper
{
    /// <summary>Fraction of the stick's range that is ignored (radial dead zone).</summary>
    public const double StickDeadZone = 0.25;

    /// <summary>How far a stick must be pushed to count as a D-pad press.</summary>
    public const double StickDigitalThreshold = 0.5;

    /// <summary>Dead zone of the analogue triggers, as a fraction of their range.</summary>
    public const double TriggerDeadZone = 0.12;

    /// <summary>A held turn button repeats after this long...</summary>
    public const double RepeatInitialMs = 400;

    /// <summary>...and then every this often.</summary>
    public const double RepeatIntervalMs = 90;

    private sealed class Repeater
    {
        private bool _held;
        private double _heldMs;
        private double _sinceLastMs;

        /// <summary>True on the poll where the input goes down, and again on each repeat while held.</summary>
        public bool Update(bool down, double elapsedMs)
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
            if (_heldMs < RepeatInitialMs)
            {
                return false;
            }

            _sinceLastMs += elapsedMs;
            if (_sinceLastMs < RepeatIntervalMs)
            {
                return false;
            }

            _sinceLastMs = 0;
            return true;
        }

        public void Reset()
        {
            _held = false;
            _heldMs = 0;
            _sinceLastMs = 0;
        }
    }

    private readonly Repeater _next = new();
    private readonly Repeater _previous = new();
    private readonly Repeater _left = new();
    private readonly Repeater _right = new();
    private readonly Repeater _up = new();
    private readonly Repeater _down = new();
    private GamepadButtons _previousButtons;

    /// <summary>Forgets held buttons (call when the controller changes or disappears, so nothing sticks).</summary>
    public void Reset()
    {
        _next.Reset();
        _previous.Reset();
        _left.Reset();
        _right.Reset();
        _up.Reset();
        _down.Reset();
        _previousButtons = GamepadButtons.None;
    }

    public GamepadFrame Update(GamepadState state, TimeSpan elapsed)
    {
        double ms = elapsed.TotalMilliseconds;
        var b = state.Buttons;

        double lx = Normalize(state.LeftX);
        double ly = Normalize(state.LeftY);

        bool next = _next.Update(b.HasFlag(GamepadButtons.A) || b.HasFlag(GamepadButtons.RightShoulder), ms);
        bool previous = _previous.Update(b.HasFlag(GamepadButtons.B) || b.HasFlag(GamepadButtons.LeftShoulder), ms);
        bool left = _left.Update(b.HasFlag(GamepadButtons.DPadLeft) || lx <= -StickDigitalThreshold, ms);
        bool right = _right.Update(b.HasFlag(GamepadButtons.DPadRight) || lx >= StickDigitalThreshold, ms);
        bool up = _up.Update(b.HasFlag(GamepadButtons.DPadUp) || ly >= StickDigitalThreshold, ms);
        bool down = _down.Update(b.HasFlag(GamepadButtons.DPadDown) || ly <= -StickDigitalThreshold, ms);

        bool Pressed(GamepadButtons button) => b.HasFlag(button) && !_previousButtons.HasFlag(button);
        bool toggleChrome = Pressed(GamepadButtons.Y);
        bool fullscreen = Pressed(GamepadButtons.X);
        bool palette = Pressed(GamepadButtons.Start);
        bool leave = Pressed(GamepadButtons.Back);
        _previousButtons = b;

        var (panX, stickY) = ApplyRadialDeadZone(Normalize(state.RightX), Normalize(state.RightY));
        double zoom = TriggerAxis(state.RightTrigger) - TriggerAxis(state.LeftTrigger);

        return new GamepadFrame(next, previous, left, right, up, down, toggleChrome, fullscreen, palette, leave, panX, -stickY, zoom);
    }

    private static double Normalize(short value) => Math.Clamp(value / 32767.0, -1, 1);

    /// <summary>Scales a stick vector so that inside the dead zone it is zero and it rises smoothly to 1 at full deflection, keeping its direction.</summary>
    internal static (double X, double Y) ApplyRadialDeadZone(double x, double y)
    {
        double magnitude = Math.Sqrt((x * x) + (y * y));
        if (magnitude < StickDeadZone)
        {
            return (0, 0);
        }

        double scaled = Math.Min(1, (magnitude - StickDeadZone) / (1 - StickDeadZone));
        return (x / magnitude * scaled, y / magnitude * scaled);
    }

    internal static double TriggerAxis(byte value)
    {
        double fraction = value / 255.0;
        return fraction < TriggerDeadZone ? 0 : (fraction - TriggerDeadZone) / (1 - TriggerDeadZone);
    }
}
