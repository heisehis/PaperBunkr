using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Views;

/// <summary>
/// Eased zoom, and turning the page at the edge of a zoomed page. The zoom used to jump to each new value the moment a wheel notch, key press or button arrived, which reads as stepping however fine the
/// steps are; now every one of those moves a zoom <em>goal</em> and the view glides towards it a frame at a time. Kept in its own file so <c>PageCanvas.cs</c> stays as small as it can.
/// </summary>
public partial class PageCanvas
{
    /// <summary>
    /// How quickly the view catches up with its zoom goal: the remaining distance (in log space, so zooming in and out feel alike) shrinks by 1/e every this many seconds. About 0.07 lets a single wheel
    /// notch settle in a fifth of a second while a run of notches blends into one continuous glide.
    /// </summary>
    private const double ZoomSmoothingSeconds = 0.07;

    /// <summary>Closer than this to the goal (in log space, about 0.2%) the view snaps onto it and stops animating.</summary>
    private const double ZoomSnapLog = 0.002;

    private double? _zoomGoal;
    private Point _zoomAnchor;
    private bool _zoomFrameRequested;
    private bool _zoomApplying;
    private long _zoomLastFrameTimestamp;

    /// <summary>The zoom the view is gliding towards (null when it is not).</summary>
    internal double? ZoomGoal => _zoomGoal;

    /// <summary>Multiplies the zoom goal by <paramref name="factor"/> and glides there, keeping the point under <paramref name="anchor"/> (default: the middle of the canvas) where it is. Repeated calls accumulate.</summary>
    public void SmoothZoomBy(double factor, Point? anchor = null) =>
        SmoothZoomTo((_zoomGoal ?? ZoomLevel) * factor, anchor);

    /// <summary>Glides to <paramref name="goal"/> (clamped to the allowed range), keeping the point under <paramref name="anchor"/> (default: the middle of the canvas) where it is. Jumps there when reduced motion is on.</summary>
    public void SmoothZoomTo(double goal, Point? anchor = null)
    {
        goal = ZoomPanMath.ClampZoom(goal, ZoomPanMath.MaxZoom, MinZoomLevel);
        _zoomAnchor = anchor ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        _tween = null;
        if (MotionTokens.IsReducedMotion() || Bounds.Width <= 0 || Bounds.Height <= 0 || TopLevel.GetTopLevel(this) is null)
        {
            _zoomGoal = null;
            ApplyZoomAt(goal, _zoomAnchor);
            return;
        }

        _zoomGoal = goal;
        _zoomLastFrameTimestamp = Stopwatch.GetTimestamp();
        RequestZoomFrame();
    }

    /// <summary>Glides back to the fit view (100%, page centred): the Fit button and the palette's reset.</summary>
    public void SmoothZoomToFit()
    {
        _zoomGoal = null;
        _panelIndex = -1;
        TweenTo(ZoomPanMath.FitZoom, 0, 0);
        UpdatePartLabel();
    }

    private void RequestZoomFrame()
    {
        if (_zoomFrameRequested || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _zoomFrameRequested = true;
        top.RequestAnimationFrame(_ => OnZoomFrame());
    }

    private void OnZoomFrame()
    {
        _zoomFrameRequested = false;
        if (_zoomGoal is not { } goal)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double dt = Math.Clamp(Stopwatch.GetElapsedTime(_zoomLastFrameTimestamp, now).TotalSeconds, 0, 0.1);
        _zoomLastFrameTimestamp = now;

        double next = StepZoomTowards(ZoomLevel, goal, dt);
        bool arrived = Math.Abs(Math.Log(next) - Math.Log(goal)) < 1e-9;
        ApplyZoomAt(next, _zoomAnchor);

        // Only stop when the zoom actually reached the goal: if something else moved it meanwhile the goal was already cleared (see OnPanelPropertyChanged).
        if (arrived && _zoomGoal == goal)
        {
            _zoomGoal = null;
        }
        else if (_zoomGoal is not null)
        {
            RequestZoomFrame();
        }
    }

    /// <summary>One frame of the glide: <paramref name="current"/> moves towards <paramref name="goal"/> by the share of the remaining log distance that <paramref name="seconds"/> covers, snapping onto it when it is close. Pure.</summary>
    internal static double StepZoomTowards(double current, double goal, double seconds)
    {
        double logCurrent = Math.Log(current);
        double logGoal = Math.Log(goal);
        double remaining = logGoal - logCurrent;
        if (Math.Abs(remaining) < ZoomSnapLog)
        {
            return goal;
        }

        double share = 1 - Math.Exp(-Math.Max(seconds, 0) / ZoomSmoothingSeconds);
        return Math.Exp(logCurrent + (remaining * share));
    }

    /// <summary>Test seam: finishes a zoom glide at once.</summary>
    internal void CompleteZoomForTest()
    {
        if (_zoomGoal is { } goal)
        {
            _zoomGoal = null;
            ApplyZoomAt(goal, _zoomAnchor);
        }
    }

    /// <summary>Sets the zoom to <paramref name="newZoom"/> with the point under <paramref name="anchor"/> staying put: the paged page's pan, or the continuous stack's scroll offset and cross-axis pan.</summary>
    private void ApplyZoomAt(double newZoom, Point anchor)
    {
        _zoomApplying = true;
        try
        {
            if (IsContinuous)
            {
                double crossAxisPan = ContinuousAxis == ReaderLayoutModel.Axis.Vertical ? PanOffsetX : PanOffsetY;
                var (newScrollOffset, newCrossAxisPan) = ReaderLayoutModel.ComputeContinuousZoomAnchor(
                    EstimatedPageSizes(), ScrollOffset, ZoomLevel, crossAxisPan, Bounds.Size, ContinuousAxis,
                    anchor, newZoom, ContinuousMainAxisGap, IsContinuousReversed);

                ZoomLevel = newZoom;
                ScrollOffset = ClampScrollOffset(newScrollOffset);
                if (ContinuousAxis == ReaderLayoutModel.Axis.Vertical)
                {
                    PanOffsetX = ClampContinuousCrossAxisPan(newCrossAxisPan);
                }
                else
                {
                    PanOffsetY = ClampContinuousCrossAxisPan(newCrossAxisPan);
                }

                return;
            }

            if (Page is null)
            {
                ZoomLevel = newZoom;
                return;
            }

            var (x, y) = ZoomPanMath.PanToKeepPointFixed(Bounds.Size, EffectivePixelSize(), ZoomLevel, new Point(PanOffsetX, PanOffsetY), anchor, newZoom, FitMode, FitOnlyIfOversized);
            ZoomLevel = newZoom;
            PanOffsetX = x;
            PanOffsetY = y;
        }
        finally
        {
            _zoomApplying = false;
        }
    }

    // ===================== Turning the page from a zoomed page =====================

    /// <summary>A wheel notch or arrow key that finds the zoomed page already at its edge only turns the page if nothing arrived just before it, so a run of scrolling that reaches the edge does not carry straight on into the next page.</summary>
    private const double WheelEdgeQuietMs = 350;

    private const double KeyEdgeQuietMs = 120;

    private long _lastWheelTimestamp;
    private long _lastArrowTimestamp;

    /// <summary>Guided view frames a panel for you: while it is on and you have not zoomed or dragged the view yourself, the arrow keys and the wheel step panels instead of panning.</summary>
    private bool GuidedSteps => GuidedActive && !_viewDirty;

    /// <summary>Whether this event came at least <paramref name="quietMs"/> after the previous one on the same input, and records it.</summary>
    private static bool NoteInput(ref long lastTimestamp, double quietMs)
    {
        long now = Stopwatch.GetTimestamp();
        bool quiet = lastTimestamp == 0 || Stopwatch.GetElapsedTime(lastTimestamp, now).TotalMilliseconds >= quietMs;
        lastTimestamp = now;
        return quiet;
    }

    /// <summary>Moves a zoomed page's pan by (<paramref name="dx"/>, <paramref name="dy"/>) pixels, clamped to the page; returns whether it actually moved (false: already at that edge).</summary>
    private bool TryPanPaged(double dx, double dy)
    {
        var (x, y) = ZoomPanMath.ClampPan(Bounds.Size, EffectivePixelSize(), ZoomLevel, PanOffsetX + dx, PanOffsetY + dy, FitMode, FitOnlyIfOversized);
        if (Math.Abs(x - PanOffsetX) < 0.01 && Math.Abs(y - PanOffsetY) < 0.01)
        {
            return false;
        }

        PanOffsetX = x;
        PanOffsetY = y;
        return true;
    }

    /// <summary>
    /// An arrow key (or D-pad press) on a zoomed page: pans, and when the page is already at that edge turns the page instead (through the part grid, so a page zoomed to several screenfuls reads on to its next part
    /// first, like the page-down key). <paramref name="dx"/> and <paramref name="dy"/> are the pan offsets the key asks for (a Right key is negative <paramref name="dx"/>).
    /// </summary>
    private void PanOrTurnFromKey(double dx, double dy)
    {
        bool quiet = NoteInput(ref _lastArrowTimestamp, KeyEdgeQuietMs);
        if (TryPanPaged(dx, dy) || !quiet)
        {
            return;
        }

        if (dx != 0)
        {
            ExecuteTurn(forward: dx < 0);
        }
        else if (IsPagedVertical)
        {
            ExecuteTurn(forward: dy < 0);
        }
        else
        {
            ExecuteReadingOrderTurn(forward: dy < 0);
        }
    }
}
