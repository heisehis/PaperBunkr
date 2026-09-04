using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Services;

/// <summary>
/// Flies a floating clone of a registered cover element from its position on the outgoing screen to
/// the matching element's position on the incoming screen (docs/superpowers/specs/2026-09-04-
/// navigation-transition-system-design.md). Driven by <see cref="NavigationTransitionCoordinator"/>,
/// never called directly from a ViewModel. Participants register via the
/// <see cref="Paperbunkr.App.Controls.SharedElement"/> attached properties, not this interface
/// directly.
/// </summary>
public interface ISharedElementTransitionService
{
    /// <summary>The overlay layer clones are placed in - a transparent, non-hit-testable <see cref="Canvas"/> above every screen (a <see cref="Canvas"/> specifically, not just any <see cref="Panel"/>, so the clone can be positioned by absolute coordinates via <c>Canvas.Left</c>/<c>Canvas.Top</c>). Call once, from <c>MainWindow</c>, before any navigation.</summary>
    void RegisterOverlayHost(Canvas host);

    /// <summary>Called by <see cref="Paperbunkr.App.Controls.SharedElement"/> when a participating element attaches to the visual tree.</summary>
    void Register(string key, Visual element, Func<IImage?> imageAccessor);

    /// <summary>Called by <see cref="Paperbunkr.App.Controls.SharedElement"/> when a participating element detaches. A no-op if <paramref name="element"/> is no longer the currently-registered element for <paramref name="key"/> (a newer registration already replaced it).</summary>
    void Unregister(string key, Visual element);

    /// <summary>Snapshots the outgoing element's position/size/corner-radius/image for <paramref name="key"/>. Call BEFORE swapping screen content. A no-op (no flight later) if nothing is registered under <paramref name="key"/>.</summary>
    void CaptureOutgoing(string key);

    /// <summary>
    /// Call AFTER swapping screen content. Polls briefly for the incoming element to register and
    /// lay out with a non-zero size, then animates a clone from the captured source to it.
    /// Returns <c>false</c> (no exception) when nothing was captured, the incoming element never
    /// registers within the poll budget, or the flight would be degenerate (zero-size rect,
    /// missing image) - callers should already have cross-faded the screens regardless, so a
    /// <c>false</c> result means only "no cover flight played," not "navigation failed."
    /// </summary>
    Task<bool> FlyToIncomingAsync(string key, TimeSpan duration, Easing easing, CancellationToken cancellationToken);

    /// <summary>Aborts any in-flight animation and removes its clone immediately. Safe to call when nothing is flying.</summary>
    void Cancel();
}
