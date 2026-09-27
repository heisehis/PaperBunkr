using System;
using System.Runtime.InteropServices;

namespace Paperbunkr.App.Services.Input;

/// <summary>
/// Reads Xbox and other XInput controllers through <c>XInputGetState</c> (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md section 3). Tries
/// <c>xinput1_4.dll</c> (Windows 8 and later) and falls back to <c>xinput9_1_0.dll</c>; on any other platform, or when neither library loads, it simply reports no controller.
/// PlayStation and Switch pads appear here only when something (Steam Input, DS4Windows) presents them as XInput. Same P/Invoke-with-guard shape as
/// <see cref="BatteryStatusInterop"/>.
/// </summary>
public sealed class XInputSource : IGamepadSource
{
    private const uint ErrorSuccess = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputStateNative
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState14(uint userIndex, out XInputStateNative state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState910(uint userIndex, out XInputStateNative state);

    private enum Library
    {
        Unknown,
        Xinput14,
        Xinput910,
        None,
    }

    private Library _library = Library.Unknown;

    public bool TryGetState(int slot, out GamepadState state)
    {
        state = default;
        if (!OperatingSystem.IsWindows() || slot is < 0 or > 3 || _library == Library.None)
        {
            return false;
        }

        XInputStateNative native;
        uint result;
        try
        {
            if (_library == Library.Unknown)
            {
                _library = Library.Xinput14;
            }

            result = _library == Library.Xinput14 ? GetState14((uint)slot, out native) : GetState910((uint)slot, out native);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            if (_library == Library.Xinput14)
            {
                _library = Library.Xinput910;
                return TryGetState(slot, out state);
            }

            _library = Library.None;
            return false;
        }

        if (result != ErrorSuccess)
        {
            return false;
        }

        var pad = native.Gamepad;
        state = new GamepadState((GamepadButtons)pad.Buttons, pad.LeftTrigger, pad.RightTrigger, pad.ThumbLX, pad.ThumbLY, pad.ThumbRX, pad.ThumbRY);
        return true;
    }
}
