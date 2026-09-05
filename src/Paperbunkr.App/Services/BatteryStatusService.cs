using System.Runtime.InteropServices;

namespace Paperbunkr.App.Services;

/// <summary>
/// Real Win32 <c>GetSystemPowerStatus</c> P/Invoke (docs/superpowers/specs/2026-09-05-reader-
/// polish-backlog-finish-design.md §2) - CE's own <c>NavigationOverlay</c> reads
/// <c>SystemInformation.PowerStatus</c> (WinForms), which this app doesn't reference; same raw-P/
/// Invoke precedent as <see cref="NativeMessageBox"/>. Windows-only, consistent with this project's
/// "Windows-first, cross-platform later" scope (docs/onboarding.md §1).
/// </summary>
public sealed class BatteryStatusService : IBatteryStatusService
{
    /// <summary><c>BatteryFlag</c>'s "no system battery" bit (winbase.h's <c>BATTERY_FLAG_NO_BATTERY</c>) - CE's own guard checks the equivalent <c>BatteryChargeStatus.NoSystemBattery</c>.</summary>
    private const byte BatteryFlagNoBattery = 128;

    private const byte BatteryLifePercentUnknown = 255;

    private const byte AcLineStatusOnline = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

    public BatteryStatusSample? GetStatus()
    {
        if (!GetSystemPowerStatus(out var status) || status.BatteryFlag == BatteryFlagNoBattery)
        {
            return null;
        }

        if (status.BatteryLifePercent == BatteryLifePercentUnknown)
        {
            return null;
        }

        return new BatteryStatusSample(status.BatteryLifePercent, status.ACLineStatus == AcLineStatusOnline);
    }
}
