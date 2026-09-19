using System;
using System.Runtime.InteropServices;

namespace Paperbunkr.App.Services;

/// <summary>
/// Windows battery-status check for <c>MatrixRainOverlay</c>'s throttling (docs/superpowers/specs/
/// 2026-09-16-theme-system-design.md § Battery-linked animation throttling). Polled from a UI-thread
/// <c>DispatcherTimer</c> only (every 30s) - never from the render thread, which is the whole point
/// of this existing at all (a P/Invoke transition 60-144 times/second in the render loop was the
/// original, rejected design). Mirrors <c>Paperbunkr.Common/Win32/ShellRegister.cs</c>'s nested
/// <c>Native</c>-class <c>[DllImport]</c> pattern.
/// </summary>
public static class BatteryStatusInterop
{
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    }

    /// <summary>Unplugged and below 20% - the Matrix rain throttle threshold. Windows-only; always false elsewhere (no interop attempted) so other platforms just always animate.</summary>
    public static bool IsThrottled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            if (!Native.GetSystemPowerStatus(out var status))
            {
                return false;
            }

            // ACLineStatus: 0 = offline (on battery), 1 = online (plugged in), 255 = unknown - treat
            // unknown as "don't throttle" (fail open, same posture as every other best-effort check
            // in this codebase, e.g. ShellRegister.CanRegisterShell).
            bool onBattery = status.ACLineStatus == 0;
            bool lowBattery = status.BatteryLifePercent is > 0 and < 20;
            return onBattery && lowBattery;
        }
        catch
        {
            return false;
        }
    }
}
