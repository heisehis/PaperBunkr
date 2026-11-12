using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Paperbunkr.App.Theming.Motion;

namespace Paperbunkr.App.Controls.EventMap;

public sealed partial class EventMapCanvas
{
    private int _glideGeneration;
    private int _dimGeneration;
    private IReadOnlySet<int>? _previousRelated;
    private double _dimProgress = 1;
    private int _hoverAnimationGeneration;
    private double _hoverProgress;
    private int? _highlightTrack;
    private double _highlightOpacity;
    private int _highlightGeneration;

    public void RevealSeries(int seriesId)
    {
        if (Layout is not { } layout) return;
        var cell = System.Linq.Enumerable.FirstOrDefault(layout.Cells, c => c.Row.SeriesId == seriesId);
        if (cell is null) return;
        Reveal(cell.Index);
        int generation = ++_highlightGeneration;
        _highlightTrack = cell.Track;
        var duration = MotionTokens.Get(MotionRole.Acknowledge).Fade;
        if (MotionTokens.IsReducedMotion() || !MotionPolicy.AllowsMotion(MotionRole.Acknowledge) || duration <= TimeSpan.Zero || TopLevel.GetTopLevel(this) is not { } top)
        { _highlightOpacity = 0; InvalidateVisual(); return; }
        _highlightOpacity = 1;
        TimeSpan? started = null;
        void Frame(TimeSpan now)
        {
            if (generation != _highlightGeneration) return;
            started ??= now;
            double t = Math.Clamp((now - started.Value).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            _highlightOpacity = 1 - t; InvalidateVisual();
            if (t < 1) top.RequestAnimationFrame(Frame); else _highlightTrack = null;
        }
        top.RequestAnimationFrame(Frame);
    }

    private void AnimateHover()
    {
        int generation = ++_hoverAnimationGeneration;
        var duration = MotionTokens.Get(MotionRole.Hover).Fade;
        if (MotionTokens.IsReducedMotion() || !MotionPolicy.AllowsMotion(MotionRole.Hover) || duration <= TimeSpan.Zero || TopLevel.GetTopLevel(this) is not { } top)
        {
            _hoverProgress = 1;
            InvalidateVisual();
            return;
        }
        _hoverProgress = 0;
        TimeSpan? started = null;
        void Frame(TimeSpan now)
        {
            if (generation != _hoverAnimationGeneration) return;
            started ??= now;
            _hoverProgress = Math.Clamp((now - started.Value).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            InvalidateVisual();
            if (_hoverProgress < 1) top.RequestAnimationFrame(Frame);
        }
        top.RequestAnimationFrame(Frame);
    }

    private void AnimateDimming(IReadOnlySet<int>? previous)
    {
        _previousRelated = previous;
        int generation = ++_dimGeneration;
        var duration = MotionTokens.Get(MotionRole.Acknowledge).Fade;
        if (MotionTokens.IsReducedMotion() || !MotionPolicy.AllowsMotion(MotionRole.Acknowledge) || duration <= TimeSpan.Zero || TopLevel.GetTopLevel(this) is not { } top)
        {
            _dimProgress = 1;
            InvalidateVisual();
            return;
        }
        _dimProgress = 0;
        TimeSpan? started = null;
        void Frame(TimeSpan now)
        {
            if (generation != _dimGeneration) return;
            started ??= now;
            double t = Math.Clamp((now - started.Value).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            _dimProgress = 1 - Math.Pow(1 - t, 3);
            InvalidateVisual();
            if (t < 1) top.RequestAnimationFrame(Frame);
        }
        top.RequestAnimationFrame(Frame);
    }

    private double CardOpacity(int index)
    {
        if (TraceCardOpacity(index) is double traced) return traced;
        double previous = _previousRelated is null || _previousRelated.Contains(index) ? 1 : 0.25;
        double current = RelatedSet is null || RelatedSet.Contains(index) ? 1 : 0.25;
        return previous + ((current - previous) * _dimProgress);
    }

    public void GlideTo(Vector target)
        => GlideToView(Scale, target);

    public void GlideToView(double targetScale, Vector target)
    {
        int generation = ++_glideGeneration;
        var duration = MotionTokens.Get(MotionRole.Travel).Move;
        if (!MotionPolicy.AllowsMotion(MotionRole.Travel) || duration <= TimeSpan.Zero || TopLevel.GetTopLevel(this) is not { } top)
        {
            _zoomAnchoring = true;
            SetCurrentValue(ScaleProperty, targetScale);
            SetCurrentValue(OffsetProperty, target);
            _zoomAnchoring = false;
            return;
        }
        var start = Offset;
        double startScale = Scale;
        TimeSpan? started = null;
        void Frame(TimeSpan now)
        {
            if (generation != _glideGeneration) return;
            started ??= now;
            double t = Math.Clamp((now - started.Value).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            double eased = 1 - Math.Pow(1 - t, 3);
            _zoomAnchoring = true;
            SetCurrentValue(ScaleProperty, startScale + ((targetScale - startScale) * eased));
            SetCurrentValue(OffsetProperty, start + ((target - start) * eased));
            _zoomAnchoring = false;
            if (t < 1) top.RequestAnimationFrame(Frame);
        }
        top.RequestAnimationFrame(Frame);
    }
}
