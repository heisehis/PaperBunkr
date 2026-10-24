using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Theming.Motion;

namespace Paperbunkr.App.Views;

/// <summary>
/// Continuous mode's scroll motion (docs/superpowers/specs/2026-10-20-motion-system-v2-reader-design.md Q35-Q36).
/// <list type="bullet">
/// <item><b>Wheel glide</b> - like the zoom glide in PageCanvas.Smooth.cs: each wheel notch moves a scroll <em>goal</em> and the view chases it
/// a frame at a time (the remaining distance shrinks by 1/e every <see cref="ScrollSmoothingSeconds"/>), so a run of notches is one glide.</item>
/// <item><b>Page jumps</b> - go-to-page, next/previous page, dot strip, thumbnails, Resume: within <see cref="GlideJumpViewports"/> screens
/// the view glides there over 220-400 ms; further, it jumps and fades in, so the decoder is not dragged through every page in between.</item>
/// </list>
/// Anything else that moves the scroll (a drag, a resize, a layout correction) cancels the glide on the spot. Travel role: Reduced and the
/// Performance tier step instantly, as before.
/// </summary>
public partial class PageCanvas
{
    internal const double ScrollSmoothingSeconds = 0.07;

    /// <summary>Jumps up to this many viewports away glide; longer ones jump and fade.</summary>
    internal const double GlideJumpViewports = 3;

    private double? _scrollGoal;
    private (double From, double To, long Start, double Seconds)? _scrollTween;
    private bool _scrollFrameRequested;
    private bool _scrollApplying;
    private long _scrollLastFrameTimestamp;

    /// <summary>The scroll position the view is gliding towards (null when it is not).</summary>
    internal double? ScrollGoal => _scrollGoal ?? _scrollTween?.To;

    private static bool CanGlide() => MotionPolicy.AllowsMotion(MotionRole.Travel);

    /// <summary>One frame of the wheel glide: <paramref name="current"/> covers the share of the remaining distance that <paramref name="seconds"/>
    /// is worth, snapping onto the goal within half a pixel. Pure.</summary>
    internal static double StepScrollTowards(double current, double goal, double seconds)
    {
        double remaining = goal - current;
        if (Math.Abs(remaining) < 0.5)
        {
            return goal;
        }

        double share = 1 - Math.Exp(-Math.Max(seconds, 0) / ScrollSmoothingSeconds);
        return current + (remaining * share);
    }

    /// <summary>How long a page-jump glide takes for <paramref name="distance"/> in a viewport <paramref name="viewport"/> long: 220 ms, plus
    /// 60 ms per screen travelled, capped at 400 ms. Null when the jump is too far to glide.</summary>
    internal static double? JumpGlideSeconds(double distance, double viewport)
    {
        if (viewport <= 0)
        {
            return null;
        }

        double screens = Math.Abs(distance) / viewport;
        return screens > GlideJumpViewports ? null : Math.Min(0.22 + (0.06 * screens), 0.40) * MotionPolicy.TimeScale;
    }

    /// <summary>Wheel input in continuous mode: moves the goal (or, when gliding is off, the scroll) and returns the value it aimed at.</summary>
    private bool TryGlideWheel(double proposedFromGoal)
    {
        if (!CanGlide() || TopLevel.GetTopLevel(this) is null)
        {
            _scrollGoal = null;
            return false;
        }

        _scrollTween = null;
        _scrollGoal = ClampScrollOffset(proposedFromGoal);
        _scrollLastFrameTimestamp = Stopwatch.GetTimestamp();
        RequestScrollFrame();
        return true;
    }

    /// <summary>Glides (near) or jumps-and-fades (far) to <paramref name="target"/>; instant when gliding is off.</summary>
    private void GlideOrJumpTo(double target)
    {
        target = ClampScrollOffset(target);
        double viewport = ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? Bounds.Height : Bounds.Width;
        double distance = target - ScrollOffset;
        _scrollGoal = null;

        if (!CanGlide() || TopLevel.GetTopLevel(this) is null || Math.Abs(distance) < 0.5)
        {
            _scrollTween = null;
            ApplyScroll(target);
            return;
        }

        if (JumpGlideSeconds(distance, viewport) is { } seconds)
        {
            _scrollTween = (ScrollOffset, target, Stopwatch.GetTimestamp(), seconds);
            RequestScrollFrame();
            return;
        }

        // Far: jump, and let the new spot fade in rather than streaming every page between through the decoder.
        _scrollTween = null;
        ApplyScroll(target);
        Replace.Play(this);
    }

    private void RequestScrollFrame()
    {
        if (_scrollFrameRequested || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _scrollFrameRequested = true;
        top.RequestAnimationFrame(_ => OnScrollFrame());
    }

    private void OnScrollFrame()
    {
        _scrollFrameRequested = false;
        long now = Stopwatch.GetTimestamp();

        if (_scrollTween is { } tween)
        {
            double t = Math.Clamp(Stopwatch.GetElapsedTime(tween.Start, now).TotalSeconds / Math.Max(tween.Seconds, 0.001), 0, 1);
            double eased = t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);
            ApplyScroll(tween.From + ((tween.To - tween.From) * eased));
            if (t >= 1)
            {
                _scrollTween = null;
            }
            else
            {
                RequestScrollFrame();
            }

            return;
        }

        if (_scrollGoal is not { } goal)
        {
            return;
        }

        double dt = Math.Clamp(Stopwatch.GetElapsedTime(_scrollLastFrameTimestamp, now).TotalSeconds, 0, 0.1);
        _scrollLastFrameTimestamp = now;
        double next = StepScrollTowards(ScrollOffset, goal, dt);
        ApplyScroll(next);
        if (next == goal)
        {
            _scrollGoal = null;
        }
        else
        {
            RequestScrollFrame();
        }
    }

    private void ApplyScroll(double value)
    {
        _scrollApplying = true;
        try
        {
            ScrollOffset = value;
        }
        finally
        {
            _scrollApplying = false;
        }
    }

    /// <summary>Called on every ScrollOffset change: a change the glide did not make (drag, resize, clamp) cancels it.</summary>
    private void CancelScrollGlideUnlessOwn()
    {
        if (!_scrollApplying)
        {
            _scrollGoal = null;
            _scrollTween = null;
        }
    }

    /// <summary>Test seam: finishes any glide at once.</summary>
    internal void CompleteScrollGlideForTest()
    {
        if (ScrollGoal is { } goal)
        {
            _scrollGoal = null;
            _scrollTween = null;
            ApplyScroll(goal);
        }
    }
}
