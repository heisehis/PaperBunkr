using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A small saturation/value square over a hue strip (docs/superpowers/specs/2026-11-06-collection-dialog-polish-design.md, the custom-accent
/// flyout of the Edit Collection dialog). Code-only on purpose - no template, no new .axaml - and drawn directly, so the only colours here are the
/// picked colour itself (data) and the thumb rings. Mouse: drag in either area. Keyboard: arrows move saturation/value, PageUp/PageDown the hue.
/// </summary>
public sealed class HsvColorPicker : Control
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<HsvColorPicker, Color>(nameof(Color), Color.FromRgb(0xC9, 0x80, 0x3F), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private const double SquareHeight = 120;
    private const double StripHeight = 14;
    private const double Gap = 8;
    private const double KeyStep = 0.02;
    private const double HueKeyStep = 5;

    private double _h;
    private double _s;
    private double _v;
    private bool _draggingSquare;
    private bool _draggingStrip;

    static HsvColorPicker()
    {
        AffectsRender<HsvColorPicker>(ColorProperty);
        FocusableProperty.OverrideDefaultValue<HsvColorPicker>(true);
        ColorProperty.Changed.AddClassHandler<HsvColorPicker>((picker, _) => picker.OnColorChanged());
    }

    public HsvColorPicker()
    {
        Width = 200;
        Height = SquareHeight + Gap + StripHeight;
        Cursor = new Cursor(StandardCursorType.Cross);
        AutomationProperties.SetName(this, "Colour picker");
        SyncFromColor();
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private void OnColorChanged()
    {
        // An external change (a hex entry, a preset) re-seeds H/S/V; our own drags already hold them. Grey keeps its hue.
        var ours = AccentColorInput.FromHsv(_h, _s, _v);
        if (ours != Color)
        {
            SyncFromColor();
        }

        InvalidateVisual();
    }

    private void SyncFromColor()
    {
        var (h, s, v) = AccentColorInput.ToHsv(Color);
        _s = s;
        _v = v;
        if (s > 0 && v > 0)
        {
            _h = h;
        }
    }

    private void Push()
    {
        SetCurrentValue(ColorProperty, AccentColorInput.FromHsv(_h, _s, _v));
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        var square = new Rect(0, 0, w, SquareHeight);
        var strip = new Rect(0, SquareHeight + Gap, w, StripHeight);

        var hueColor = AccentColorInput.FromHsv(_h, 1, 1);
        var horizontal = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(hueColor, 1) },
        };
        var vertical = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Colors.Black, 1) },
        };

        using (context.PushClip(new RoundedRect(square, 4)))
        {
            context.DrawRectangle(horizontal, null, square);
            context.DrawRectangle(vertical, null, square);
        }

        var hues = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        };
        for (int i = 0; i <= 6; i++)
        {
            hues.GradientStops.Add(new GradientStop(AccentColorInput.FromHsv(i * 60, 1, 1), i / 6.0));
        }

        using (context.PushClip(new RoundedRect(strip, 4)))
        {
            context.DrawRectangle(hues, null, strip);
        }

        DrawThumb(context, new Point(_s * w, (1 - _v) * SquareHeight), AccentColorInput.FromHsv(_h, _s, _v));
        DrawThumb(context, new Point((_h / 360.0) * w, strip.Center.Y), AccentColorInput.FromHsv(_h, 1, 1));
    }

    private static void DrawThumb(DrawingContext context, Point center, Color fill)
    {
        context.DrawEllipse(new SolidColorBrush(fill), new Pen(Brushes.White, 2), center, 6, 6);
        context.DrawEllipse(null, new Pen(Brushes.Black, 1), center, 7.5, 7.5);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        _draggingSquare = point.Y <= SquareHeight;
        _draggingStrip = !_draggingSquare;
        e.Pointer.Capture(this);
        Apply(point);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_draggingSquare || _draggingStrip)
        {
            Apply(e.GetPosition(this));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _draggingSquare = false;
        _draggingStrip = false;
        e.Pointer.Capture(null);
    }

    private void Apply(Point point)
    {
        double w = Math.Max(1, Bounds.Width);
        if (_draggingSquare)
        {
            _s = Math.Clamp(point.X / w, 0, 1);
            _v = 1 - Math.Clamp(point.Y / SquareHeight, 0, 1);
        }
        else
        {
            _h = Math.Clamp(point.X / w, 0, 1) * 360;
        }

        Push();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool handled = true;
        switch (e.Key)
        {
            case Key.Left: _s = Math.Clamp(_s - KeyStep, 0, 1); break;
            case Key.Right: _s = Math.Clamp(_s + KeyStep, 0, 1); break;
            case Key.Up: _v = Math.Clamp(_v + KeyStep, 0, 1); break;
            case Key.Down: _v = Math.Clamp(_v - KeyStep, 0, 1); break;
            case Key.PageUp: _h = (_h + HueKeyStep) % 360; break;
            case Key.PageDown: _h = ((_h - HueKeyStep) + 360) % 360; break;
            default: handled = false; break;
        }

        if (handled)
        {
            Push();
            e.Handled = true;
        }
    }
}
