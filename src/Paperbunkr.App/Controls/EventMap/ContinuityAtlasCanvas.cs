using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>
/// The continuity Atlas (docs/superpowers/specs/2026-11-11-continuity-list-atlas-views-design.md §2): one bar per event against a pinned
/// year axis, thickness by issue count, the read part solid, relation connectors between bars and a loose-issue density band at the bottom.
/// One focus stop; Left/Right step bars in chronological order, Up/Down move to the nearest bar in the row above or below, Enter opens.
/// Zoom is horizontal only (Ctrl+wheel, <see cref="Fit"/>). Colours are skin tokens resolved at render time.
/// </summary>
public sealed class ContinuityAtlasCanvas : Control
{
    public static readonly StyledProperty<ContinuityAtlas?> AtlasProperty =
        AvaloniaProperty.Register<ContinuityAtlasCanvas, ContinuityAtlas?>(nameof(Atlas));

    internal const double Axis = 28, RowHeight = 28, LooseBand = 16, Pad = 16;
    private const double MinMonthWidth = 1, MaxMonthWidth = 48;
    private readonly EventMapTextCache _text = new();
    private double _monthWidth = 6, _offsetX, _offsetY;
    private bool _fitted;
    private int _focused = -1;
    private int? _hovered;

    /// <summary>Raised with the event id when a bar is clicked or activated with Enter.</summary>
    public event Action<int>? OpenEvent;

    internal event Action? ViewportChanged;

    static ContinuityAtlasCanvas() => AffectsRender<ContinuityAtlasCanvas>(AtlasProperty);

    public ContinuityAtlasCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => { _text.Clear(); InvalidateVisual(); };
        ResourcesChanged += (_, _) => { _text.Clear(); InvalidateVisual(); };
    }

    public ContinuityAtlas? Atlas { get => GetValue(AtlasProperty); set => SetValue(AtlasProperty, value); }

    internal double MonthWidth => _monthWidth;
    internal int FocusedBar => _focused;
    internal Vector Offset => new(_offsetX, _offsetY);

    /// <summary>Fits the whole span into the width (at least 1 px per month; longer continuities scroll).</summary>
    public void Fit()
    {
        if (Atlas is not { MonthSpan: > 0 } atlas || Bounds.Width <= 2 * Pad)
        {
            return;
        }

        _monthWidth = Math.Clamp((Bounds.Width - 2 * Pad) / atlas.MonthSpan, MinMonthWidth, MaxMonthWidth);
        _offsetX = 0;
        Clamp();
        InvalidateVisual();
        ViewportChanged?.Invoke();
    }

    internal Rect BarRect(AtlasBar bar)
    {
        var atlas = Atlas!;
        double x = Pad + (bar.StartMonth - atlas.FirstMonth) * _monthWidth - _offsetX;
        double width = Math.Max(3, (bar.EndMonth - bar.StartMonth + 1) * _monthWidth);
        double y = Axis + bar.Row * RowHeight + (RowHeight - bar.Thickness) / 2 - _offsetY;
        return new Rect(x, y, width, bar.Thickness);
    }

    internal void Activate(int barIndex)
    {
        if (Atlas is { } atlas && barIndex >= 0 && barIndex < atlas.Bars.Count)
        {
            _focused = barIndex;
            OpenEvent?.Invoke(atlas.Bars[barIndex].EventId);
        }
    }

    internal void FocusBar(int barIndex)
    {
        if (Atlas is { } atlas && barIndex >= 0 && barIndex < atlas.Bars.Count)
        {
            _focused = barIndex;
            Focus();
            EnsureVisible(barIndex);
            InvalidateVisual();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ContinuityAtlasAutomationPeer(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AtlasProperty)
        {
            _focused = Atlas is { Bars.Count: > 0 } ? Math.Clamp(_focused, 0, Atlas.Bars.Count - 1) : -1;
            if (!_fitted && Bounds.Width > 0)
            {
                _fitted = true;
                Fit();
            }

            Clamp();
            ViewportChanged?.Invoke();
        }
        else if (change.Property == BoundsProperty)
        {
            if (!_fitted && Bounds.Width > 0 && Atlas is { MonthSpan: > 0 })
            {
                _fitted = true;
                Fit();
            }

            Clamp();
        }
    }

    private double ContentWidth => Atlas is { } a ? 2 * Pad + a.MonthSpan * _monthWidth : 0;

    private double ContentHeight => Atlas is { } a ? Axis + a.RowCount * RowHeight + LooseBand + Pad : 0;

    private void Clamp()
    {
        _offsetX = Math.Clamp(_offsetX, 0, Math.Max(0, ContentWidth - Bounds.Width));
        _offsetY = Math.Clamp(_offsetY, 0, Math.Max(0, ContentHeight - Bounds.Height));
    }

    private void EnsureVisible(int barIndex)
    {
        if (Atlas is not { } atlas)
        {
            return;
        }

        var rect = BarRect(atlas.Bars[barIndex]);
        if (rect.X < Pad)
        {
            _offsetX += rect.X - Pad;
        }
        else if (rect.Right > Bounds.Width - Pad)
        {
            _offsetX += Math.Min(rect.X - Pad, rect.Right - (Bounds.Width - Pad));
        }

        if (rect.Y < Axis)
        {
            _offsetY += rect.Y - Axis - 8;
        }
        else if (rect.Bottom > Bounds.Height - LooseBand)
        {
            _offsetY += rect.Bottom - (Bounds.Height - LooseBand) + 8;
        }

        Clamp();
        ViewportChanged?.Invoke();
    }

    private int? HitBar(Point p)
    {
        if (Atlas is not { } atlas || p.Y < Axis)
        {
            return null;
        }

        for (int i = 0; i < atlas.Bars.Count; i++)
        {
            var rect = BarRect(atlas.Bars[i]);
            // A thin bar gets the whole row height as its target.
            var target = new Rect(rect.X, Axis + atlas.Bars[i].Row * RowHeight - _offsetY, rect.Width, RowHeight);
            if (target.Contains(p))
            {
                return i;
            }
        }

        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && HitBar(e.GetPosition(this)) is int hit)
        {
            _focused = hit;
            Activate(hit);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int? hit = HitBar(e.GetPosition(this));
        if (hit != _hovered)
        {
            _hovered = hit;
            ToolTip.SetTip(this, hit is int h && Atlas is { } atlas ? atlas.Bars[h].Tooltip : null);
            Cursor = hit is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = null;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Atlas is not { } atlas)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Zoom about the pointer: the month under it stays put.
            double pointer = e.GetPosition(this).X;
            double month = (pointer - Pad + _offsetX) / _monthWidth;
            _monthWidth = Math.Clamp(_monthWidth * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), MinMonthWidth, MaxMonthWidth);
            _offsetX = month * _monthWidth - (pointer - Pad);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
        {
            _offsetX -= (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 70;
        }
        else
        {
            _offsetY -= e.Delta.Y * 70;
        }

        Clamp();
        InvalidateVisual();
        ViewportChanged?.Invoke();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Atlas is not { Bars.Count: > 0 } atlas)
        {
            return;
        }

        int current = Math.Max(0, _focused);
        int next = current;
        switch (e.Key)
        {
            case Key.Left: next = Math.Max(0, current - 1); break;
            case Key.Right: next = Math.Min(atlas.Bars.Count - 1, current + 1); break;
            case Key.Home: next = 0; break;
            case Key.End: next = atlas.Bars.Count - 1; break;
            case Key.Up or Key.Down:
                int row = atlas.Bars[current].Row + (e.Key == Key.Up ? -1 : 1);
                var candidates = atlas.Bars.Select((b, i) => (b, i)).Where(t => t.b.Row == row).ToList();
                if (candidates.Count == 0)
                {
                    e.Handled = true;
                    return;
                }

                int middle = (atlas.Bars[current].StartMonth + atlas.Bars[current].EndMonth) / 2;
                next = candidates.OrderBy(t => Math.Abs((t.b.StartMonth + t.b.EndMonth) / 2 - middle)).ThenBy(t => t.i).First().i;
                break;
            case Key.Enter:
                Activate(current);
                e.Handled = true;
                return;
            default:
                return;
        }

        _focused = next;
        EnsureVisible(next);
        InvalidateVisual();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        IBrush Brush(string key) => new SolidColorBrush(EventMapPalette.ResolveColor(this, key));
        context.FillRectangle(Brush("PbBgColor"), new Rect(Bounds.Size));
        if (Atlas is not { Bars.Count: > 0 } atlas)
        {
            return;
        }

        var text = EventMapPalette.ResolveColor(this, "PbTextColor");
        var muted = EventMapPalette.ResolveColor(this, "PbTextMutedColor");
        var border = Brush("PbBorderColor");
        var accent = Brush("PbAccentColor");
        var typeface = Typeface.Default;

        // Year grid lines, under everything.
        int firstYear = atlas.FirstMonth / 12, lastYear = atlas.LastMonth / 12;
        int step = YearStep(_monthWidth);
        for (int year = firstYear - firstYear % step; year <= lastYear + 1; year += step)
        {
            double x = Pad + (year * 12 - atlas.FirstMonth) * _monthWidth - _offsetX;
            if (x >= 0 && x <= Bounds.Width)
            {
                context.DrawLine(new Pen(border, 0.5), new Point(x, Axis), new Point(x, Bounds.Height));
            }
        }

        // Loose-issue density band along the bottom.
        if (atlas.LooseByYear.Count > 0)
        {
            int max = atlas.LooseByYear.Values.Max();
            var soft = Brush("PbAccentSoftColor");
            double bandTop = Bounds.Height - LooseBand;
            foreach (var (year, count) in atlas.LooseByYear)
            {
                double x = Pad + (year * 12 - atlas.FirstMonth) * _monthWidth - _offsetX;
                double h = Math.Max(2, LooseBand * count / max);
                context.FillRectangle(soft, new Rect(x, bandTop + LooseBand - h, Math.Max(1, 12 * _monthWidth - 1), h));
            }
        }

        // Connectors (earlier bar's end → later bar's start): solid for yours, dashed otherwise.
        var byId = atlas.Bars.Select((b, i) => (b, i)).ToDictionary(t => t.b.EventId, t => t.i);
        using (context.PushClip(new Rect(0, Axis, Bounds.Width, Math.Max(0, Bounds.Height - Axis))))
        {
            foreach (var c in atlas.Connectors)
            {
                if (!byId.TryGetValue(c.EarlierEventId, out int from) || !byId.TryGetValue(c.LaterEventId, out int to))
                {
                    continue;
                }

                // Connectors leave from under the earlier bar's end and arrive under the later bar's start, dipping below the row,
                // so they never run through a bar's label (labels sit to the right of their bar, on its centre line).
                var a = BarRect(atlas.Bars[from]);
                var b = BarRect(atlas.Bars[to]);
                var start = new Point(a.Right - 3, a.Bottom);
                var end = new Point(b.X + 1, b.Bottom);
                bool emphasised = _hovered == from || _hovered == to;
                var pen = new Pen(Brush("PbTextMutedColor"), emphasised ? 1.5 : 1,
                    c.IsYours ? null : new DashStyle(new double[] { 3, 3 }, 0));
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(start, false);
                    double dip = Math.Max(start.Y, end.Y) + (RowHeight / 2);
                    double bend = Math.Max(12, Math.Abs(end.X - start.X) / 3);
                    path.CubicBezierTo(new Point(start.X + bend, dip), new Point(end.X - bend, dip), end);
                    path.EndFigure(false);
                }

                context.DrawGeometry(null, pen, geometry);
            }

            // Bars: the read part solid, the rest the same colour at 30 %.
            for (int i = 0; i < atlas.Bars.Count; i++)
            {
                var bar = atlas.Bars[i];
                var rect = BarRect(bar);
                if (rect.Right < 0 || rect.X > Bounds.Width || rect.Bottom < Axis || rect.Y > Bounds.Height)
                {
                    continue;
                }

                var colour = EventMapPalette.ResolveColor(this, bar.ColorIndex);
                double radius = Math.Min(4, bar.Thickness / 2);
                context.DrawRectangle(new SolidColorBrush(colour, 0.3), null, rect, radius, radius);
                if (bar.ReadFraction > 0)
                {
                    using (context.PushClip(new Rect(rect.X, rect.Y, rect.Width * bar.ReadFraction, rect.Height)))
                    {
                        context.DrawRectangle(new SolidColorBrush(colour), null, rect, radius, radius);
                    }
                }

                if (_hovered == i)
                {
                    context.DrawRectangle(null, new Pen(Brush("PbTextColor"), 1), rect.Inflate(1), radius, radius);
                }

                if (i == _focused && IsFocused)
                {
                    context.DrawRectangle(null, new Pen(accent, 2), rect.Inflate(3), radius + 2, radius + 2);
                }

                // The label sits to the right of the bar, trimmed before the next bar in the row.
                double labelX = rect.Right + 6;
                double limit = atlas.Bars.Where(o => o.Row == bar.Row && o.StartMonth > bar.EndMonth)
                    .Select(o => BarRect(o).X - 6).DefaultIfEmpty(Bounds.Width - 4).Min();
                double width = limit - labelX;
                if (width > 24)
                {
                    var layout = _text.Get(bar.Name, typeface, 11, width, text);
                    layout.Draw(context, new Point(labelX, Axis + bar.Row * RowHeight - _offsetY + (RowHeight - layout.Height) / 2));
                }
            }
        }

        // Pinned year axis.
        context.FillRectangle(Brush("PbChromeColor"), new Rect(0, 0, Bounds.Width, Axis));
        context.DrawLine(new Pen(border, 1), new Point(0, Axis - 0.5), new Point(Bounds.Width, Axis - 0.5));
        double firstLabelX = double.MaxValue;
        for (int year = firstYear - firstYear % step; year <= lastYear + 1; year += step)
        {
            double x = Pad + (year * 12 - atlas.FirstMonth) * _monthWidth - _offsetX;
            if (x < 0 || x > Bounds.Width)
            {
                continue;
            }

            firstLabelX = Math.Min(firstLabelX, x);
            var label = _text.Get(year.ToString(CultureInfo.InvariantCulture), typeface, 10, 48, muted);
            label.Draw(context, new Point(x + 3, (Axis - label.Height) / 2));
        }

        // The year at the left edge, whose own grid line is scrolled (or starts) off screen, is pinned there so the axis always says when.
        int leftYear = (int)Math.Floor((atlas.FirstMonth + ((_offsetX - Pad) / _monthWidth)) / 12);
        if (firstLabelX > 56)
        {
            var pinned = _text.Get(leftYear.ToString(CultureInfo.InvariantCulture), typeface, 10, 48, muted);
            pinned.Draw(context, new Point(Pad / 2, (Axis - pinned.Height) / 2));
        }
    }

    /// <summary>Years between axis labels, so labels thin out as the axis compresses (about 56 px apart at least).</summary>
    internal static int YearStep(double monthWidth)
    {
        foreach (int step in new[] { 1, 2, 5, 10, 20, 50 })
        {
            if (step * 12 * monthWidth >= 56)
            {
                return step;
            }
        }

        return 100;
    }
}

internal sealed class ContinuityAtlasAutomationPeer : ControlAutomationPeer
{
    private readonly ContinuityAtlasCanvas _canvas;

    public ContinuityAtlasAutomationPeer(ContinuityAtlasCanvas canvas) : base(canvas)
    {
        _canvas = canvas;
        canvas.ViewportChanged += InvalidateChildren;
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
    {
        if (_canvas.Atlas is not { } atlas)
        {
            return Array.Empty<AutomationPeer>();
        }

        var result = new List<AutomationPeer>();
        for (int i = 0; i < atlas.Bars.Count; i++)
        {
            var rect = _canvas.BarRect(atlas.Bars[i]);
            if (rect.Right >= 0 && rect.X <= _canvas.Bounds.Width && rect.Bottom >= ContinuityAtlasCanvas.Axis && rect.Y <= _canvas.Bounds.Height)
            {
                result.Add(new ContinuityAtlasBarPeer(_canvas, this, i));
            }
        }

        return result;
    }
}

internal sealed class ContinuityAtlasBarPeer : AutomationPeer, IInvokeProvider
{
    private readonly ContinuityAtlasCanvas _canvas;
    private readonly int _index;
    private AutomationPeer? _parent;

    public ContinuityAtlasBarPeer(ContinuityAtlasCanvas canvas, AutomationPeer parent, int index)
    {
        _canvas = canvas;
        _parent = parent;
        _index = index;
    }

    public void Invoke() => _canvas.Activate(_index);

    protected override string? GetNameCore() => _canvas.Atlas is { } a && _index < a.Bars.Count ? a.Bars[_index].Tooltip : null;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
    protected override string? GetAutomationIdCore() => $"AtlasBar{_index}";
    protected override string GetClassNameCore() => "ContinuityAtlasBar";

    protected override Rect GetBoundingRectangleCore()
    {
        var origin = _parent?.GetBoundingRectangle().Position ?? default;
        var rect = _canvas.Atlas is { } a && _index < a.Bars.Count ? _canvas.BarRect(a.Bars[_index]) : default;
        return rect.Translate(new Vector(origin.X, origin.Y));
    }

    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => Array.Empty<AutomationPeer>();
    protected override AutomationPeer? GetParentCore() => _parent;
    protected override bool TrySetParent(AutomationPeer? parent) { _parent = parent; return true; }
    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;
    protected override bool IsEnabledCore() => _canvas.IsEffectivelyEnabled;
    protected override bool IsKeyboardFocusableCore() => true;
    protected override bool HasKeyboardFocusCore() => _canvas.IsFocused && _canvas.FocusedBar == _index;
    protected override void SetFocusCore() => _canvas.FocusBar(_index);
    protected override void BringIntoViewCore() => _canvas.FocusBar(_index);
    protected override string? GetAccessKeyCore() => null;
    protected override string? GetAcceleratorKeyCore() => null;
    protected override AutomationPeer? GetLabeledByCore() => null;
    protected override bool ShowContextMenuCore() => false;
}
