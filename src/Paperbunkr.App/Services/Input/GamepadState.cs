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
public readonly record struct GamepadState(GamepadButtons Buttons, byte LeftTrigger, byte RightTrigger, short LeftX, short LeftY, short RightX, short RightY)
{
    /// <summary>
    /// True when the player is touching the controller: a button is down, a trigger is past its dead zone, or a stick is pushed past its radial dead zone. A controller resting on
    /// the desk reports false, so the reading-session clock does not count an idle pad as presence.
    /// </summary>
    public bool HasInput(InputTuning tuning)
    {
        if (Buttons != GamepadButtons.None)
        {
            return true;
        }

        if (GamepadInputProcessor.TriggerAxis(LeftTrigger, tuning.TriggerDeadZone) != 0 || GamepadInputProcessor.TriggerAxis(RightTrigger, tuning.TriggerDeadZone) != 0)
        {
            return true;
        }

        return Magnitude(LeftX, LeftY) >= tuning.StickDeadZone || Magnitude(RightX, RightY) >= tuning.StickDeadZone;
    }

    private static double Magnitude(short x, short y)
    {
        double fx = x / 32767.0;
        double fy = y / 32767.0;
        return Math.Sqrt((fx * fx) + (fy * fy));
    }
}
