using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>
/// The map's pinned column ruler (docs/superpowers/specs/2026-09-25-event-map-design.md §3 "Ruler"): column numbers for the visible
/// columns and, on an event map in spine mode, the trunk card's badge above each segment. On a continuity map
/// (docs/superpowers/specs/2026-09-27-continuity-map-design.md §4) it grows a band row: each block's name over its columns (click an
/// event's name to open its map) and the connectors between event bands - arrows for Prequel/Sequel/Continues, a bracket for a
/// Crossover, solid for yours, dashed for inferred or Wikidata, with the reason on hover. Draws only what is in view, offset by the
/// map's horizontal scroll.
/// </summary>
public sealed class EventMapRuler : Control
{
    public static readonly StyledProperty<EventMapLayoutResult?> LayoutProperty =
        AvaloniaProperty.Register<EventMapRuler, EventMapLayoutResult?>(nameof(Layout));

    public static readonly StyledProperty<IReadOnlyList<EventMapCardViewModel>?> CardsProperty =
        AvaloniaProperty.Register<EventMapRuler, IReadOnlyList<EventMapCardViewModel>?>(nameof(Cards));

    public static readonly StyledProperty<IReadOnlyList<EventMapConnector>?> ConnectorsProperty =
        AvaloniaProperty.Register<EventMapRuler, IReadOnlyList<EventMapConnector>?>(nameof(Connectors));

    public static readonly StyledProperty<double> OffsetXProperty =
        AvaloniaProperty.Register<EventMapRuler, double>(nameof(OffsetX));

    /// <summary>Band-row geometry: labels 0-17, connector lane 17-32, column numbers below.</summary>
    internal const double BandLabelBottom = 17;
    internal const double ConnectorY = 25;

    private static readonly ImmutableDashStyle Dashed = new(new[] { 3.0, 2.0 }, 0);

    static EventMapRuler()
    {
        AffectsRender<EventMapRuler>(LayoutProperty, CardsProperty, ConnectorsProperty, OffsetXProperty);
    }

    public EventMapRuler()
    {
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        ResourcesChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>An event band's name was clicked: the block index.</summary>
    public event Action<int>? BandClicked;

    public EventMapLayoutResult? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public IReadOnlyList<EventMapCardViewModel>? Cards
    {
        get => GetValue(CardsProperty);
        set => SetValue(CardsProperty, value);
    }

    public IReadOnlyList<EventMapConnector>? Connectors
    {
        get => GetValue(ConnectorsProperty);
        set => SetValue(ConnectorsProperty, value);
    }

    public double OffsetX
    {
        get => GetValue(OffsetXProperty);
        set => SetValue(OffsetXProperty, value);
    }

    /// <summary>The block whose band label sits at <paramref name="x"/> (ruler coordinates), or null.</summary>
    internal static int? BlockAt(EventMapLayoutResult layout, double offsetX, double x)
    {
        double column = (x + offsetX) / layout.Metrics.SlotWidth;
        foreach (var block in layout.Blocks)
        {
            var (first, last) = layout.BlockColumns(block);
            if (column >= first && column < last + 1)
            {
                return block.Index;
            }
        }

        return null;
    }

    /// <summary>x of a block's centre, in ruler coordinates.</summary>
    internal static double BlockCentre(EventMapLayoutResult layout, double offsetX, int blockIndex)
    {
        var (first, last) = layout.BlockColumns(layout.Blocks[blockIndex]);
        return ((first + last + 1) * layout.Metrics.SlotWidth / 2) - offsetX;
    }

    /// <summary>The connector under <paramref name="point"/> (its line, with a few pixels' slack), or null.</summary>
    internal static EventMapConnector? ConnectorAt(EventMapLayoutResult layout, IReadOnlyList<EventMapConnector> connectors, double offsetX, Point point)
    {
        for (int i = 0; i < connectors.Count; i++)
        {
            var c = connectors[i];
            double y = LaneY(i);
            double x1 = BlockCentre(layout, offsetX, c.FromBlock);
            double x2 = BlockCentre(layout, offsetX, c.ToBlock);
            if (Math.Abs(point.Y - y) <= 4 && point.X >= Math.Min(x1, x2) - 4 && point.X <= Math.Max(x1, x2) + 4)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>Alternate connectors sit a little higher/lower so overlapping ones stay tellable apart.</summary>
    private static double LaneY(int index) => ConnectorY + (index % 2 == 0 ? -2 : 3);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Layout is not { IsBlockMode: true } layout)
        {
            return;
        }

        var p = e.GetPosition(this);
        string? tip = null;
        if (Connectors is { Count: > 0 } connectors && ConnectorAt(layout, connectors, OffsetX, p) is { } connector)
        {
            tip = connector.Tooltip;
        }
        else if (p.Y <= BandLabelBottom && BlockAt(layout, OffsetX, p.X) is int block)
        {
            var b = layout.Blocks[block];
            tip = b.EventId is not null ? $"{b.Label} - open this event's map" : b.Label;
        }

        ToolTip.SetTip(this, tip);
        Cursor = tip is not null && p.Y <= BandLabelBottom && BlockAt(layout, OffsetX, p.X) is int hovered && layout.Blocks[hovered].EventId is not null
            ? new Cursor(StandardCursorType.Hand)
            : Cursor.Default;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Layout is { IsBlockMode: true } layout && e.GetPosition(this) is var p && p.Y <= BandLabelBottom
            && BlockAt(layout, OffsetX, p.X) is int block && layout.Blocks[block].EventId is not null)
        {
            BandClicked?.Invoke(block);
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        var layout = Layout;
        if (layout is null || layout.IsEmpty)
        {
            return;
        }

        var range = layout.VisibleRange(new Rect(OffsetX, 0, Bounds.Width, layout.Metrics.TrackHeight));
        if (range.IsEmpty)
        {
            return;
        }

        double slot = layout.Metrics.SlotWidth;
        var typeface = new Typeface(Avalonia.Controls.Documents.TextElement.GetFontFamily(this));
        var bold = new Typeface(typeface.FontFamily, weight: FontWeight.SemiBold);
        var muted = new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbTextMutedColor"));
        var accent = new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbAccentColor"));

        for (int c = range.FirstColumn; c <= range.LastColumn; c++)
        {
            if (layout.ColumnLabel(c) is not { } columnLabel)
            {
                continue;
            }

            var text = new FormattedText(columnLabel, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10, muted);
            double x = (c * slot) + (slot / 2) - OffsetX - (text.Width / 2);
            context.DrawText(text, new Point(x, Bounds.Height - text.Height - 3));
        }

        if (layout.IsBlockMode)
        {
            RenderBands(context, layout, slot, typeface, bold, muted, accent);
            return;
        }

        if (!layout.IsSpineMode || Cards is not { } cards)
        {
            return;
        }

        foreach (var segment in layout.Segments)
        {
            if (segment.TrunkCell is not int trunk || trunk >= cards.Count)
            {
                continue;
            }

            int column = layout.Cells[trunk].Column;
            if (column < range.FirstColumn || column > range.LastColumn)
            {
                continue;
            }

            var badge = new FormattedText(cards[trunk].Badge, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold, 10, accent)
            {
                MaxTextWidth = slot * 2,
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1,
            };
            double x = (column * slot) + (slot / 2) - OffsetX - Math.Min(badge.Width, slot * 2) / 2;
            context.DrawText(badge, new Point(x, 2));
        }
    }

    private void RenderBands(DrawingContext context, EventMapLayoutResult layout, double slot, Typeface typeface, Typeface bold, IBrush muted, IBrush accent)
    {
        var divider = new ImmutablePen(new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbBorderColor")), 1);
        foreach (var block in layout.Blocks)
        {
            var (firstColumn, lastColumn) = layout.BlockColumns(block);
            double left = (firstColumn * slot) - OffsetX;
            double right = ((lastColumn + 1) * slot) - OffsetX;
            if (right < 0 || left > Bounds.Width)
            {
                continue;
            }

            context.DrawLine(divider, new Point(Math.Round(left) + 0.5, 0), new Point(Math.Round(left) + 0.5, BandLabelBottom));

            // The label stays readable while its band is only partly in view.
            double visibleLeft = Math.Max(left, 0) + 6;
            double visibleRight = Math.Min(right, Bounds.Width) - 6;
            if (visibleRight - visibleLeft < 20)
            {
                continue;
            }

            var label = new FormattedText(block.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                block.Kind == EventMapBlockKind.Event ? bold : typeface, 10.5, block.Kind == EventMapBlockKind.Event ? accent : muted)
            {
                MaxTextWidth = visibleRight - visibleLeft,
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1,
            };
            context.DrawText(label, new Point(visibleLeft, 2));
        }

        if (Connectors is not { Count: > 0 } connectors)
        {
            return;
        }

        var small = new Typeface(typeface.FontFamily);
        for (int i = 0; i < connectors.Count; i++)
        {
            var c = connectors[i];
            double x1 = BlockCentre(layout, OffsetX, c.FromBlock);
            double x2 = BlockCentre(layout, OffsetX, c.ToBlock);
            if (Math.Max(x1, x2) < 0 || Math.Min(x1, x2) > Bounds.Width)
            {
                continue;
            }

            double y = LaneY(i);
            var pen = new ImmutablePen(new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbAccentColor"), c.IsYours ? 1.0 : 0.8),
                1.25, c.IsYours ? null : Dashed, PenLineCap.Round);
            context.DrawLine(pen, new Point(x1, y), new Point(x2, y));
            if (c.IsOrdered)
            {
                // Arrowhead at the later band.
                context.DrawLine(pen, new Point(x2, y), new Point(x2 - 5, y - 3));
                context.DrawLine(pen, new Point(x2, y), new Point(x2 - 5, y + 3));
            }
            else
            {
                // Crossover: a bracket - ticks at both ends.
                context.DrawLine(pen, new Point(x1, y - 3), new Point(x1, y + 3));
                context.DrawLine(pen, new Point(x2, y - 3), new Point(x2, y + 3));
            }

            var text = new FormattedText(c.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, small, 9, muted);
            double mid = (x1 + x2) / 2;
            context.FillRectangle(new ImmutableSolidColorBrush(EventMapPalette.ResolveColor(this, "PbChromeColor")),
                new Rect(mid - (text.Width / 2) - 3, y - (text.Height / 2), text.Width + 6, text.Height));
            context.DrawText(text, new Point(mid - (text.Width / 2), y - (text.Height / 2)));
        }
    }
}
