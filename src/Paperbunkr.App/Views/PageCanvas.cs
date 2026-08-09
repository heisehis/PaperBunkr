using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Views;

/// <summary>
/// The Reader screen's page canvas (docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md
/// §4/§6). Renders <see cref="Page"/> via <see cref="ReaderPageDrawOperation"/>; clicking the
/// left/right half or pressing <see cref="LeftKey"/>/<see cref="RightKey"/> (remappable via
/// Preferences, default the physical Left/Right arrows) invokes <see cref="LeftCommand"/>/
/// <see cref="RightCommand"/> - bound from XAML like every other command in this codebase, rather
/// than a code-behind event the ViewModel would need to subscribe to. Named spatially, not
/// semantically ("Previous"/"Next"), per docs/superpowers/specs/
/// 2026-08-07-reader-rtl-navigation-design.md §3 - which physical side means "forward" depends on
/// reading direction, and PageCanvas itself has no opinion on that.
///
/// Also handles zoom/pan gestures (mouse wheel, drag, double-click, touch tap-zones/flick) per
/// docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-design.md.
/// <see cref="ZoomLevel"/>/<see cref="PanOffsetX"/>/<see cref="PanOffsetY"/> are two-way bound -
/// <see cref="ViewModels.ReaderScreenViewModel.ZoomLevel"/> is the clamp authority, not this
/// control; this control writes proposed values and trusts the TwoWay round-trip to reflect the
/// clamped result back, the same mechanism a TwoWay-bound <c>Slider.Value</c> relies on.
/// </summary>
public class PageCanvas : Control
{
    private const double KeyPanStep = 40;
    private const double WheelZoomStep = 0.25;
    private const double WheelPanStep = 1.0;
    private const double MinFlickDistance = 60;
    private const double MaxFlickDurationMs = 400;

    public static readonly StyledProperty<Bitmap?> PageProperty =
        AvaloniaProperty.Register<PageCanvas, Bitmap?>(nameof(Page));

    public static readonly StyledProperty<ICommand?> LeftCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(LeftCommand));

    public static readonly StyledProperty<ICommand?> RightCommandProperty =
        AvaloniaProperty.Register<PageCanvas, ICommand?>(nameof(RightCommand));

    public static readonly StyledProperty<bool> HighQualityDisplayProperty =
        AvaloniaProperty.Register<PageCanvas, bool>(nameof(HighQualityDisplay), defaultValue: true);

    public static readonly StyledProperty<Key> LeftKeyProperty =
        AvaloniaProperty.Register<PageCanvas, Key>(nameof(LeftKey), defaultValue: Key.Left);

    public static readonly StyledProperty<Key> RightKeyProperty =
        AvaloniaProperty.Register<PageCanvas, Key>(nameof(RightKey), defaultValue: Key.Right);

    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(ZoomLevel), defaultValue: ZoomPanMath.MinZoom,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> PanOffsetXProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(PanOffsetX), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> PanOffsetYProperty =
        AvaloniaProperty.Register<PageCanvas, double>(nameof(PanOffsetY), defaultBindingMode: BindingMode.TwoWay);

    private bool _isDragging;
    private Point _dragStartPointer;
    private double _dragStartPanX;
    private double _dragStartPanY;
    private Point? _touchPressPosition;
    private DateTime _touchPressTime;

    static PageCanvas()
    {
        AffectsRender<PageCanvas>(PageProperty);
        AffectsRender<PageCanvas>(HighQualityDisplayProperty);
        AffectsRender<PageCanvas>(ZoomLevelProperty);
        AffectsRender<PageCanvas>(PanOffsetXProperty);
        AffectsRender<PageCanvas>(PanOffsetYProperty);
        FocusableProperty.OverrideDefaultValue<PageCanvas>(true);
    }

    public Bitmap? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public ICommand? LeftCommand
    {
        get => GetValue(LeftCommandProperty);
        set => SetValue(LeftCommandProperty, value);
    }

    public ICommand? RightCommand
    {
        get => GetValue(RightCommandProperty);
        set => SetValue(RightCommandProperty, value);
    }

    public bool HighQualityDisplay
    {
        get => GetValue(HighQualityDisplayProperty);
        set => SetValue(HighQualityDisplayProperty, value);
    }

    /// <summary>Remappable via Preferences &gt; Reader &gt; Keyboard Shortcuts (docs/alpha-roadmap.md P5 follow-up). Defaults to the physical Left arrow.</summary>
    public Key LeftKey
    {
        get => GetValue(LeftKeyProperty);
        set => SetValue(LeftKeyProperty, value);
    }

    /// <summary>See <see cref="LeftKey"/>. Defaults to the physical Right arrow.</summary>
    public Key RightKey
    {
        get => GetValue(RightKeyProperty);
        set => SetValue(RightKeyProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public double PanOffsetX
    {
        get => GetValue(PanOffsetXProperty);
        set => SetValue(PanOffsetXProperty, value);
    }

    public double PanOffsetY
    {
        get => GetValue(PanOffsetYProperty);
        set => SetValue(PanOffsetYProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new ReaderPageDrawOperation(new Rect(Bounds.Size), Page, HighQualityDisplay, ZoomLevel, PanOffsetX, PanOffsetY));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        if (e.ClickCount == 2)
        {
            ToggleZoom(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        bool isTouch = e.Pointer.Type == PointerType.Touch;
        if (isTouch)
        {
            _touchPressPosition = e.GetPosition(this);
            _touchPressTime = DateTime.UtcNow;
        }

        if (ZoomLevel > ZoomPanMath.MinZoom)
        {
            _isDragging = true;
            _dragStartPointer = e.GetPosition(this);
            _dragStartPanX = PanOffsetX;
            _dragStartPanY = PanOffsetY;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (isTouch)
        {
            InvokeTouchZone(e.GetPosition(this));
        }
        else
        {
            InvokeZoneCommand(e.GetPosition(this).X);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging)
        {
            return;
        }

        var p = e.GetPosition(this);
        var (x, y) = ZoomPanMath.ClampPan(Bounds.Size, Page?.PixelSize ?? default, ZoomLevel,
            _dragStartPanX + (p.X - _dragStartPointer.X), _dragStartPanY + (p.Y - _dragStartPointer.Y));
        PanOffsetX = x;
        PanOffsetY = y;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging)
        {
            e.Pointer.Capture(null);
        }

        _isDragging = false;

        if (e.Pointer.Type == PointerType.Touch && _touchPressPosition is { } start && ZoomLevel <= ZoomPanMath.MinZoom)
        {
            var end = e.GetPosition(this);
            double dx = end.X - start.X;
            var elapsed = DateTime.UtcNow - _touchPressTime;
            if (elapsed.TotalMilliseconds <= MaxFlickDurationMs && Math.Abs(dx) >= MinFlickDistance && Math.Abs(dx) > Math.Abs(end.Y - start.Y))
            {
                TryExecute(dx < 0 ? RightCommand : LeftCommand);
                e.Handled = true;
            }
        }

        _touchPressPosition = null;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            double newZoom = ZoomPanMath.ClampZoom(ZoomLevel + (e.Delta.Y * WheelZoomStep));
            var cursor = e.GetPosition(this);
            var (x, y) = ZoomPanMath.PanToKeepPointFixed(Bounds.Size, Page?.PixelSize ?? default,
                ZoomLevel, new Point(PanOffsetX, PanOffsetY), cursor, newZoom);
            ZoomLevel = newZoom;
            PanOffsetX = x;
            PanOffsetY = y;
            e.Handled = true;
            return;
        }

        if (ZoomLevel > ZoomPanMath.MinZoom)
        {
            var (x, y) = ZoomPanMath.ClampPan(Bounds.Size, Page?.PixelSize ?? default, ZoomLevel,
                PanOffsetX - (e.Delta.X * WheelPanStep), PanOffsetY + (e.Delta.Y * WheelPanStep));
            PanOffsetX = x;
            PanOffsetY = y;
        }
        else if (e.Delta.Y < 0 || e.Delta.X > 0)
        {
            TryExecute(RightCommand);
        }
        else if (e.Delta.Y > 0 || e.Delta.X < 0)
        {
            TryExecute(LeftCommand);
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (ZoomLevel > ZoomPanMath.MinZoom && TryGetArrowPanDelta(e.Key, out double dx, out double dy))
        {
            var (x, y) = ZoomPanMath.ClampPan(Bounds.Size, Page?.PixelSize ?? default, ZoomLevel, PanOffsetX + dx, PanOffsetY + dy);
            PanOffsetX = x;
            PanOffsetY = y;
            e.Handled = true;
            return;
        }

        if (e.Key == LeftKey && TryExecute(LeftCommand))
        {
            e.Handled = true;
        }
        else if (e.Key == RightKey && TryExecute(RightCommand))
        {
            e.Handled = true;
        }
    }

    private void ToggleZoom(Point clickPoint)
    {
        if (ZoomLevel > ZoomPanMath.MinZoom)
        {
            ZoomLevel = ZoomPanMath.MinZoom; // cascade (VM setter) zeroes pan
            return;
        }

        var (x, y) = ZoomPanMath.PanToCenterOn(Bounds.Size, Page?.PixelSize ?? default, ZoomPanMath.DoubleClickZoom, clickPoint);
        ZoomLevel = ZoomPanMath.DoubleClickZoom;
        PanOffsetX = x;
        PanOffsetY = y;
    }

    private void InvokeTouchZone(Point p)
    {
        double third = Bounds.Width / 3;
        if (p.X < third)
        {
            TryExecute(LeftCommand);
        }
        else if (p.X > third * 2)
        {
            TryExecute(RightCommand);
        }
        // center column (all 3 rows): reserved no-op, per spec §4 - no chrome/menu feature to call yet
    }

    private void InvokeZoneCommand(double x)
    {
        var command = x < Bounds.Width / 2 ? LeftCommand : RightCommand;
        TryExecute(command);
    }

    private static bool TryGetArrowPanDelta(Key key, out double dx, out double dy)
    {
        dx = dy = 0;
        switch (key)
        {
            case Key.Left:
                dx = KeyPanStep;
                return true;
            case Key.Right:
                dx = -KeyPanStep;
                return true;
            case Key.Up:
                dy = KeyPanStep;
                return true;
            case Key.Down:
                dy = -KeyPanStep;
                return true;
            default:
                return false;
        }
    }

    private static bool TryExecute(ICommand? command)
    {
        if (command?.CanExecute(null) != true)
        {
            return false;
        }

        command.Execute(null);
        return true;
    }
}
