namespace Paperbunkr.App.Services;

/// <summary>
/// One-shot flag: set by <c>Program.Main</c> when <see cref="BootstrapSentinel.Evaluate"/> returned
/// <see cref="BootstrapDecision.SafeMode"/> and this launch is a post-crash software-rendering
/// retry. Read once by <c>App.RunStartupSequenceAsync</c> to raise the user-facing safe-mode
/// notice. A static is acceptable here for the same reason <see cref="GraphicsBootstrap"/>'s cache
/// path is: single process, written once before Avalonia starts, read once during startup, never
/// mutated afterward (docs/superpowers/specs/2026-09-10-bootstrap-crash-sentinel-safe-mode-design.md
/// §5.2).
/// </summary>
public static class SafeModeState
{
    public static bool Active { get; set; }
}
