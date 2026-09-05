namespace Paperbunkr.App.Services;

/// <summary>
/// A point-in-time battery reading (docs/superpowers/specs/2026-09-05-reader-polish-backlog-finish-
/// design.md §2) - CE's own <c>NavigationOverlay</c> shows a battery indicator only when the OS
/// reports a real battery, so <see cref="IBatteryStatusService.GetStatus"/> returns <c>null</c>
/// rather than a sample when none is present.
/// </summary>
public readonly record struct BatteryStatusSample(int Percentage, bool IsCharging);

/// <summary>
/// Reads the current machine's battery state. Abstracted behind an interface so
/// <see cref="ViewModels.ReaderScreenViewModel"/> can substitute a fake in tests without touching
/// the real Win32 API - see <see cref="BatteryStatusService"/> for the real implementation.
/// </summary>
public interface IBatteryStatusService
{
    /// <summary>Returns the current reading, or <c>null</c> if the machine reports no battery present.</summary>
    BatteryStatusSample? GetStatus();
}
