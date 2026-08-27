namespace Paperbunkr.App.Services;

/// <summary>
/// What the user chose in <see cref="Views.CrashReportWindow"/>, mapped from ComicRackCE's four
/// CrashDialog outcomes (Retry/OK/Cancel/Abort) to what's actually valid on this platform - see
/// docs/superpowers/specs/2026-08-23-app-chrome-crash-reporter-and-tray-design.md §2. No Retry:
/// CE's Retry only applied to its lock/freeze case, which is handled separately by
/// <see cref="FreezeWatchdogService"/>, not this dialog.
/// </summary>
public enum CrashOutcome
{
    /// <summary>Relaunch the app (new process) and exit this one - CE's OK.</summary>
    Restart,

    /// <summary>Exit without relaunching - CE's Cancel.</summary>
    Exit,

    /// <summary>
    /// Keep running - CE's Abort. Only ever offered when the crash source is
    /// <c>Dispatcher.UIThread.UnhandledException</c>, since that's the only source where
    /// "continue" is technically possible (see the design spec's "Platform differences" section).
    /// </summary>
    Continue,
}
