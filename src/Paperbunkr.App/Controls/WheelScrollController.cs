namespace Paperbunkr.App.Controls;

/// <summary>
/// The state machine behind eased wheel scrolling for one scroll viewer: accumulates notches into a target and steps the
/// offset toward it, standing down when something else moves the offset. No Avalonia types, so it is unit-testable;
/// <see cref="SmoothScrollViewer"/> feeds it wheel events and animation frames.
/// </summary>
public sealed class WheelScrollController
{
    private double? _target;
    private double _lastSet;

    public bool IsAnimating => _target is not null;

    public double? Target => _target;

    /// <summary>Adds wheel notches (positive = up). A notch during an animation retargets from the <b>current target</b>, so rapid notches accumulate instead of restarting.</summary>
    public void AddNotches(double notches, double currentOffset, double maxOffset)
    {
        double basis = _target ?? currentOffset;
        _target = WheelEasingMath.Retarget(basis, notches, maxOffset);
        _lastSet = currentOffset;
    }

    /// <summary>
    /// One animation frame. Returns the offset to apply, or <see langword="null"/> when there is nothing to do (idle, or another actor
    /// moved the offset and the animation was cancelled).
    /// </summary>
    public double? Tick(double currentOffset, double dtSeconds, double maxOffset)
    {
        if (_target is not { } target)
        {
            return null;
        }

        if (WheelEasingMath.MovedExternally(_lastSet, currentOffset))
        {
            _target = null;
            return null;
        }

        target = System.Math.Min(target, System.Math.Max(0, maxOffset));
        double next = WheelEasingMath.Advance(currentOffset, target, dtSeconds);
        _lastSet = next;
        _target = next == target ? null : target;
        return next;
    }

    public void Cancel() => _target = null;
}
