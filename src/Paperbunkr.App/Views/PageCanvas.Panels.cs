using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Services.Reader.Panels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// Guided panel view and smart double-click zoom (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md sections 3 and 4). The canvas owns the stepping and the eased view changes; the
/// panels themselves are detected by the view model (<see cref="Services.Reader.Panels.PagePanelAnalyzer"/>) and handed in through <see cref="Panels"/>. Kept in its own file so <c>PageCanvas.cs</c> stays
/// as small as it can.
/// </summary>
public partial class PageCanvas
{
    /// <summary>Guided panel view: next and previous step through the page's panels (paged, single-page layout only; ignored otherwise).</summary>
    public static readonly StyledProperty<bool> GuidedViewProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(GuidedView));

    /// <summary>The detected panels of the current page (reading order, page fractions), or null while they are not known yet.</summary>
    public static readonly StyledProperty<PagePanels?> PanelsProperty =
        AvaloniaProperty.Register<PageCanvas, PagePanels?>(nameof(Panels));

    /// <summary>A double-click or double-tap zooms to the panel under the pointer instead of the plain 200% (off by default so the PDF reader keeps its behaviour).</summary>
    public static readonly StyledProperty<bool> SmartDoubleClickZoomProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(SmartDoubleClickZoom));

    /// <summary>Asks the owner to make <see cref="Panels"/> available for the current page right now (smart double-click when guided view is off).</summary>
    public static readonly StyledProperty<ICommand?> EnsurePanelsCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(EnsurePanelsCommand));

    public static readonly StyledProperty<IReadOnlyList<KeyGesture>> ToggleGuidedViewGestureProperty =
        AvaloniaProperty.Register<PageCanvas, IReadOnlyList<KeyGesture>>(nameof(ToggleGuidedViewGesture), defaultValue: []);

    public static readonly StyledProperty<ICommand?> ToggleGuidedViewCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(ToggleGuidedViewCommand));

    public bool GuidedView
    {
        get => GetValue(GuidedViewProperty);
        set => SetValue(GuidedViewProperty, value);
    }

    public PagePanels? Panels
    {
        get => GetValue(PanelsProperty);
        set => SetValue(PanelsProperty, value);
    }

    public bool SmartDoubleClickZoom
    {
        get => GetValue(SmartDoubleClickZoomProperty);
        set => SetValue(SmartDoubleClickZoomProperty, value);
    }

    public ICommand? EnsurePanelsCommand
    {
        get => GetValue(EnsurePanelsCommandProperty);
        set => SetValue(EnsurePanelsCommandProperty, value);
    }

    public IReadOnlyList<KeyGesture> ToggleGuidedViewGesture
    {
        get => GetValue(ToggleGuidedViewGestureProperty);
        set => SetValue(ToggleGuidedViewGestureProperty, value);
    }

    public ICommand? ToggleGuidedViewCommand
    {
        get => GetValue(ToggleGuidedViewCommandProperty);
        set => SetValue(ToggleGuidedViewCommandProperty, value);
    }

    private int _panelIndex = -1;
    private bool _landingPending;
    private bool _landingForward = true;
    private bool _lastTurnReadingForward = true;
    private bool _viewDirty;
    private bool _applyingView;
    private (double Zoom, double PanX, double PanY)? _smartRestore;
    private ZoomPanTween? _tween;
    private long _tweenStartTimestamp;
    private bool _tweenFrameRequested;

    /// <summary>Guided view is only meaningful for one page in paged mode: not in a continuous mode and not while a double-page spread is showing.</summary>
    private bool GuidedActive => GuidedView && !IsContinuous && SecondaryPage is null && Page is not null;

    /// <summary>The detected panels of the current page in the orientation the page is shown in (rotation applied), or an empty list.</summary>
    private IReadOnlyList<PanelRect> DetectedShownPanels()
    {
        if (Panels is not { } panels)
        {
            return [];
        }

        int rotation = EffectiveRotationDegrees();
        return rotation == 0 ? panels.Rects : panels.Rects.Select(r => PanelViewMath.RotateRect(r, rotation)).ToList();
    }

    /// <summary>
    /// The steps guided view walks through: the detected panels, with any panel that is bigger than the viewport at 100% (a tall panel on a long strip, a page in fit-width mode) cut into overlapping
    /// screen-sized slices so every part of it is reached. Depends on the viewport, so it is worked out each time rather than stored.
    /// </summary>
    private IReadOnlyList<PanelRect> ShownPanels() =>
        PanelViewMath.SplitOversized(DetectedShownPanels(), EffectivePixelSize(), Bounds.Size, FitMode, FitOnlyIfOversized, ReadingMode == ReadingMode.RightToLeft);

    /// <summary>The page point (fractions) under a canvas point in the current view, or null when it is not on the page.</summary>
    private Point? PagePointAtCanvas(Point canvasPoint) =>
        PanelViewMath.PagePointAt(canvasPoint, Bounds.Size, EffectivePixelSize(), ZoomLevel, PanOffsetX, PanOffsetY, FitMode, FitOnlyIfOversized);

    /// <summary>Called from <see cref="OnPropertyChanged"/> for every change: keeps the panel state in step with page, panels, guided view and manual view changes.</summary>
    private void OnPanelPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == PageProperty)
        {
            // A new page: forget the old panel, show the whole page while the new one's panels are found, and remember which way we arrived.
            _tween = null;
            _panelIndex = -1;
            _viewDirty = false;
            _smartRestore = null;
            _landingPending = GuidedView && !IsContinuous && SecondaryPage is null;
            _landingForward = _lastTurnReadingForward;
            _lastTurnReadingForward = true;
            if (_landingPending && Page is not null)
            {
                ApplyView(ZoomPanMath.FitZoom, 0, 0);
            }

            TryApplyPendingLanding();
        }
        else if (change.Property == PanelsProperty)
        {
            TryApplyPendingLanding();
            UpdatePartLabel();
        }
        else if (change.Property == GuidedViewProperty)
        {
            _panelIndex = -1;
            _viewDirty = false;
            if (GuidedView)
            {
                _landingPending = true;
                _landingForward = true;
                TryApplyPendingLanding();
            }
            else
            {
                _landingPending = false;
                TweenTo(ZoomPanMath.FitZoom, 0, 0);
            }

            UpdatePartLabel();
        }
        else if (!_applyingView && (change.Property == ZoomLevelProperty || change.Property == PanOffsetXProperty || change.Property == PanOffsetYProperty))
        {
            // Someone other than a panel step moved the view (drag, wheel, pinch, slider, keys): stop any tween and remember that the current panel is no longer what is centred. A zoom glide
            // (PageCanvas.Smooth.cs) moves the view itself, so only a change from elsewhere ends it.
            _tween = null;
            _viewDirty = true;
            if (!_zoomApplying)
            {
                _zoomGoal = null;
            }
        }
    }

    /// <summary>Sets the view without treating it as a manual move.</summary>
    private void ApplyView(double zoom, double panX, double panY)
    {
        _applyingView = true;
        try
        {
            ZoomLevel = zoom;
            PanOffsetX = panX;
            PanOffsetY = panY;
        }
        finally
        {
            _applyingView = false;
        }
    }

    /// <summary>Eases the view to (<paramref name="zoom"/>, <paramref name="panX"/>, <paramref name="panY"/>); jumps there when reduced motion is on or the canvas has no size.</summary>
    private void TweenTo(double zoom, double panX, double panY)
    {
        _zoomGoal = null;
        if (MotionTokens.IsReducedMotion() || Bounds.Width <= 0 || Bounds.Height <= 0 || TopLevel.GetTopLevel(this) is null)
        {
            _tween = null;
            ApplyView(zoom, panX, panY);
            return;
        }

        _tween = new ZoomPanTween(ZoomLevel, PanOffsetX, PanOffsetY, zoom, panX, panY, ZoomPanTween.DefaultDurationMs);
        _tweenStartTimestamp = Stopwatch.GetTimestamp();
        RequestTweenFrame();
    }

    private void RequestTweenFrame()
    {
        if (_tweenFrameRequested || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _tweenFrameRequested = true;
        top.RequestAnimationFrame(_ => OnTweenFrame());
    }

    private void OnTweenFrame()
    {
        _tweenFrameRequested = false;
        if (_tween is not { } tween)
        {
            return;
        }

        double elapsed = Stopwatch.GetElapsedTime(_tweenStartTimestamp).TotalMilliseconds;
        var (zoom, panX, panY) = tween.Evaluate(elapsed);
        ApplyView(zoom, panX, panY);
        if (tween.IsComplete(elapsed))
        {
            _tween = null;
        }
        else
        {
            RequestTweenFrame();
        }
    }

    /// <summary>Test seam: runs a tween to its end immediately.</summary>
    internal void CompleteTweenForTest()
    {
        if (_tween is { } tween)
        {
            var (zoom, panX, panY) = tween.Evaluate(tween.DurationMs);
            _tween = null;
            ApplyView(zoom, panX, panY);
        }
    }

    internal bool IsTweening => _tween is not null;

    /// <summary>Shows panel <paramref name="index"/> of the current page (eased).</summary>
    private void ShowPanel(int index, IReadOnlyList<PanelRect> panels)
    {
        if (index < 0 || index >= panels.Count)
        {
            return;
        }

        // A page the detector gave up on is one "panel" that is just the page: keep the plain fit view rather than zooming to fill the window.
        var (zoom, panX, panY) = Panels is { Confident: true }
            ? PanelViewMath.ForPanel(panels[index], EffectivePixelSize(), Bounds.Size, FitMode, FitOnlyIfOversized)
            : (ZoomPanMath.FitZoom, 0.0, 0.0);
        _panelIndex = index;
        _viewDirty = false;
        TweenTo(zoom, panX, panY);
        UpdatePartLabel();
    }

    /// <summary>Applies the landing panel of a freshly shown page as soon as both the page and its panels are known.</summary>
    private void TryApplyPendingLanding()
    {
        if (!_landingPending || !GuidedActive || Panels is null)
        {
            return;
        }

        var panels = ShownPanels();
        if (panels.Count == 0)
        {
            _landingPending = false;
            return;
        }

        _landingPending = false;
        ShowPanel(PanelStepper.Landing(panels.Count, _landingForward), panels);
    }

    /// <summary>The panel currently framed: the one last shown unless the user moved the view since, in which case the one nearest the middle of the viewport (-1 at the fit view).</summary>
    private int CurrentPanelIndex(IReadOnlyList<PanelRect> panels)
    {
        if (_panelIndex >= 0 && _panelIndex < panels.Count && !_viewDirty)
        {
            return _panelIndex;
        }

        if (ZoomPanMath.IsFit(ZoomLevel) && Math.Abs(PanOffsetX) < 0.5 && Math.Abs(PanOffsetY) < 0.5)
        {
            return -1;
        }

        return PanelViewMath.CurrentPanelIndex(panels, Bounds.Size, EffectivePixelSize(), ZoomLevel, PanOffsetX, PanOffsetY, FitMode, FitOnlyIfOversized);
    }

    /// <summary>Guided stepping: replaces the split-page part grid. Returns false when there is no further panel in that direction (the caller then turns the page) or the panels are not known yet.</summary>
    private bool TryStepPanel(bool spatialForward)
    {
        var panels = ShownPanels();
        if (panels.Count == 0)
        {
            return false;
        }

        bool forward = SpatialTurnsFlipped ? !spatialForward : spatialForward;
        var (outcome, index) = PanelStepper.Step(panels.Count, CurrentPanelIndex(panels), forward);
        if (outcome != PanelStepOutcome.Moved)
        {
            return false;
        }

        ShowPanel(index, panels);
        return true;
    }

    /// <summary>Recorded by <see cref="ExecuteTurn"/> before it turns the page, so the new page lands on its first panel going forward and its last going back.</summary>
    private void NoteTurnDirection(bool spatialForward) => _lastTurnReadingForward = SpatialTurnsFlipped ? !spatialForward : spatialForward;

    /// <summary>The panel label ("Panel 3/7") when guided view is framing a panel of a confidently detected page, else null (the caller shows the part label).</summary>
    private string? GuidedPartLabel(out int count, out int current)
    {
        count = 1;
        current = 0;
        if (!GuidedActive)
        {
            return null;
        }

        // Guided view is on but this page has no panels it could find: say so, so the whole-page view does not look like nothing happened.
        if (Panels is { Confident: false })
        {
            return "No panels found";
        }

        if (Panels is not { Confident: true })
        {
            return null;
        }

        var shown = ShownPanels();
        if (_panelIndex < 0 || _panelIndex >= shown.Count)
        {
            return null;
        }

        count = shown.Count;
        current = _panelIndex;
        return PanelStepper.Label(_panelIndex, shown.Count);
    }

    // ===================== The first click of a double-click =====================
    // A double-click is two presses, and the first one lands on a tap zone and acts at once (delaying every page turn by the double-click interval would make the reader sluggish). So the
    // press is remembered when it really stepped a panel or turned the page, and the second press of a double-click puts that back before doing the zoom.

    private Point _tapPoint;
    private TapRecord? _lastTapTurn;

    /// <summary>What a tap zone changed, and how to put it back: a page turn is undone by turning the other way, a panel step by restoring the panel and view it started from.</summary>
    private readonly record struct TapRecord(long Timestamp, Point Point, bool PageTurned, bool SpatialForward, int PanelBefore, double ZoomBefore, double PanXBefore, double PanYBefore);

    /// <summary>Runs a tap zone's turn and remembers it when it actually changed the page or the framed panel.</summary>
    private void TapTurn(bool spatialForward, Func<bool> turn)
    {
        var pageBefore = Page;
        int panelBefore = _panelIndex;
        double zoomBefore = ZoomLevel, panXBefore = PanOffsetX, panYBefore = PanOffsetY;
        bool executed = turn();
        bool pageTurned = executed && !ReferenceEquals(Page, pageBefore);
        bool panelStepped = executed && _panelIndex != panelBefore;
        _lastTapTurn = pageTurned || panelStepped
            ? new TapRecord(Stopwatch.GetTimestamp(), _tapPoint, pageTurned, spatialForward, panelBefore, zoomBefore, panXBefore, panYBefore)
            : null;
    }

    private void UndoTapTurnForDoubleClick(Point point)
    {
        if (_lastTapTurn is not { } last)
        {
            return;
        }

        _lastTapTurn = null;
        bool recent = Stopwatch.GetElapsedTime(last.Timestamp).TotalMilliseconds <= 700;
        bool near = Math.Abs(point.X - last.Point.X) <= 12 && Math.Abs(point.Y - last.Point.Y) <= 12;
        if (!recent || !near)
        {
            return;
        }

        if (last.PageTurned)
        {
            ExecuteTurn(!last.SpatialForward);
            return;
        }

        // A panel step: back to the panel (or the whole-page view) it started from, at once, so the double-click that follows sees exactly the state the user clicked on.
        _tween = null;
        _panelIndex = last.PanelBefore;
        ApplyView(last.ZoomBefore, last.PanXBefore, last.PanYBefore);
        UpdatePartLabel();
    }

    // ===================== Smart double-click (design section 4) =====================

    /// <summary>
    /// A double-click or double-tap on the page. Returns true when it was handled here: in guided view it toggles between the current panel and the whole page; otherwise it zooms to the panel under
    /// the pointer, and a second one returns to the view it left. Returns false to fall back to the plain 200% (setting off, continuous or spread layout, no confident panel under the pointer).
    /// </summary>
    private bool TrySmartDoubleClick(Point canvasPoint)
    {
        if (!SmartDoubleClickZoom || IsContinuous || SecondaryPage is not null || Page is null)
        {
            return false;
        }

        // Second double-click: back to where the first one started.
        if (_smartRestore is { } restore && !GuidedView)
        {
            _smartRestore = null;
            _panelIndex = -1;
            TweenTo(restore.Zoom, restore.PanX, restore.PanY);
            UpdatePartLabel();
            return true;
        }

        if (Panels is null)
        {
            EnsurePanelsCommand?.Execute(null);
        }

        if (Panels is not { Confident: true })
        {
            return false;
        }

        var panels = ShownPanels();
        if (GuidedView)
        {
            // Guided view: in a panel -> the whole page; on the whole page -> the panel under the pointer (else the current one).
            if (!ZoomPanMath.IsFit(ZoomLevel))
            {
                _panelIndex = -1;
                TweenTo(ZoomPanMath.FitZoom, 0, 0);
                UpdatePartLabel();
                return true;
            }

            int at = PagePointAtCanvas(canvasPoint) is { } pagePoint ? PanelStepper.ContainingIndex(panels, pagePoint.X, pagePoint.Y) : -1;
            ShowPanel(at >= 0 ? at : Math.Max(0, _panelIndex), panels);
            return true;
        }

        if (!ZoomPanMath.IsFit(ZoomLevel))
        {
            return false;   // zoomed some other way: the plain double-click resets it
        }

        if (PagePointAtCanvas(canvasPoint) is not { } point)
        {
            return false;
        }

        int index = PanelStepper.ContainingIndex(panels, point.X, point.Y);
        if (index < 0)
        {
            return false;   // a gutter: the plain 200% at the pointer
        }

        var (zoom, panX, panY) = PanelViewMath.ForPanel(panels[index], EffectivePixelSize(), Bounds.Size, FitMode, FitOnlyIfOversized);
        _smartRestore = (ZoomLevel, PanOffsetX, PanOffsetY);
        TweenTo(zoom, panX, panY);
        return true;
    }

    /// <summary>The page's rectangle on the canvas in the current view (for overlays drawn over the page).</summary>
    public Rect GetPageScreenRect()
    {
        var content = EffectivePixelSize();
        double baseScale = ZoomPanMath.ComputeBaseScale(Bounds.Size, content, FitMode, FitOnlyIfOversized);
        double displayedW = content.Width * baseScale * ZoomLevel;
        double displayedH = content.Height * baseScale * ZoomLevel;
        return new Rect(((Bounds.Width - displayedW) / 2) + PanOffsetX, ((Bounds.Height - displayedH) / 2) + PanOffsetY, displayedW, displayedH);
    }

    /// <summary>The panels of the current page in the orientation shown, for overlays (empty when unknown).</summary>
    public IReadOnlyList<PanelRect> GetShownPanels() => DetectedShownPanels();
}
