using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Drives <c>SplashWindow</c> during the async startup sequence (docs/superpowers/specs/2026-09-09-
/// startup-onboarding-whats-new-design.md, Decision 2). <c>App.axaml.cs</c> shows the window, runs
/// the heavy init on a background thread calling <see cref="ReportPhase"/> as each phase finishes,
/// then holds the window on screen to the <see cref="MinimumVisible"/> floor before fading it out.
///
/// <para>
/// Determinate, step-based: the startup phases are a known fixed list, so the bar advances through
/// them rather than spinning meaninglessly (worst exactly when a big migration takes 20s).
/// </para>
/// </summary>
public partial class SplashViewModel : ObservableObject
{
    /// <summary>Anti-flash floor - the splash stays visible at least this long even if init
    /// finishes sooner (Decision 2, from the pasted spec's 400ms guidance).</summary>
    public static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(400);

    private readonly Func<TimeSpan, Task> _delay;

    public SplashViewModel()
        : this(Task.Delay)
    {
    }

    /// <summary>Test seam - production uses <see cref="Task.Delay(TimeSpan)"/>.</summary>
    internal SplashViewModel(Func<TimeSpan, Task> delay)
    {
        _delay = delay;
        StatusMessage = "Starting Paperbunkr…";
    }

    /// <summary>0..1, bound to the progress bar.</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>The current phase, shown under the bar.</summary>
    [ObservableProperty]
    private string _statusMessage;

    /// <summary>Bottom-right badge, e.g. "Paperbunkr 0.3.0-beta".</summary>
    public string VersionText => $"Paperbunkr {ReleaseVersion.DisplayString}";

    /// <summary>
    /// Records that phase <paramref name="index"/> of <paramref name="total"/> has completed:
    /// sets <see cref="Progress"/> to <c>index / total</c> and <see cref="StatusMessage"/> to the
    /// name of the phase now starting (<paramref name="message"/>). Call with
    /// <c>index == total</c> and any message for the final "done" tick.
    /// </summary>
    public void ReportPhase(int index, int total, string message)
    {
        if (total <= 0)
        {
            return;
        }

        Progress = Math.Clamp(index / (double)total, 0d, 1d);
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusMessage = message;
        }
    }

    /// <summary>
    /// Awaits whatever remains of the <see cref="MinimumVisible"/> floor, measured from
    /// <paramref name="shownAtUtc"/>. Returns immediately once the floor has already elapsed.
    /// </summary>
    public async Task EnforceMinimumVisibleAsync(DateTime shownAtUtc)
    {
        TimeSpan elapsed = DateTime.UtcNow - shownAtUtc;
        TimeSpan remaining = MinimumVisible - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await _delay(remaining);
        }
    }
}
