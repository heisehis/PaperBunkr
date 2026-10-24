using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation.Easings;
using Paperbunkr.App.Theming.Motion;

namespace Paperbunkr.App.Services;

/// <summary>
/// Sequences one drill-down navigation (docs/superpowers/specs/2026-09-04-navigation-transition-
/// system-design.md): snapshot the outgoing shared element (if any) -&gt; swap screen content -&gt;
/// fly a clone to the incoming element. <see cref="MainViewModel"/> holds this behind a plain
/// delegate (<c>Func&lt;string?, Action, Task&gt;</c>), the same pattern it already uses for
/// <c>ShowToast</c>/<c>NavigateBack</c> callbacks - so it stays visual-free and testable with a
/// no-op default.
///
/// Deliberately doesn't take a <c>DrillTransitionKind</c> parameter, unlike the design doc's literal
/// pseudocode - direction only matters for <c>MainViewModel.IsDrillTransitionReversed</c> (which
/// screen slides in from which side), never for how the shared-element flight itself runs (it flies
/// from whatever was captured to whatever registers under the same key, agnostic of direction), so
/// <c>MainViewModel</c> sets <c>DrillTransitionKind</c> itself before calling this, rather than
/// threading it through here too.
/// </summary>
public sealed class NavigationTransitionCoordinator
{
    private readonly ISharedElementTransitionService _service;
    private readonly Func<bool> _isReducedMotion;
    private readonly Func<TimeSpan> _flightDuration;
    private readonly Easing _easing;

    public NavigationTransitionCoordinator(
        ISharedElementTransitionService service,
        Func<bool> isReducedMotion,
        Func<TimeSpan> flightDuration,
        Easing easing)
    {
        _service = service;
        _isReducedMotion = isReducedMotion;
        _flightDuration = flightDuration;
        _easing = easing;
    }

    private CancellationTokenSource? _flight;

    /// <param name="sharedKey">Null for a navigation with no shared-element participant (e.g. any Book screen) - skips capture/fly entirely, only <paramref name="swapContent"/> runs.</param>
    /// <param name="swapContent">Exactly today's navigation body (set CurrentScreen, load the target, PushEntry/ReplayEntry) - runs synchronously, before any await, so history/CanNavigateBack etc. update immediately regardless of how the flight resolves.</param>
    /// <remarks>A newer navigation supersedes an older one (docs/superpowers/specs/2026-10-20-motion-system-v2-navigation-design.md §3.3):
    /// the previous flight is cancelled and its clone removed before anything else happens.</remarks>
    public async Task RunAsync(string? sharedKey, Action swapContent)
    {
        _flight?.Cancel();
        _flight = null;
        _service.Cancel();

        if (_isReducedMotion() || !MotionPolicy.AllowsMotion(MotionRole.Travel))
        {
            swapContent();
            return;
        }

        if (sharedKey is not null)
        {
            _service.CaptureOutgoing(sharedKey);
        }

        swapContent();

        if (sharedKey is not null)
        {
            using var flight = new CancellationTokenSource();
            _flight = flight;
            try
            {
                await _service.FlyToIncomingAsync(sharedKey, _flightDuration(), _easing, flight.Token).ConfigureAwait(true);
            }
            finally
            {
                if (ReferenceEquals(_flight, flight))
                {
                    _flight = null;
                }
            }
        }
    }
}
