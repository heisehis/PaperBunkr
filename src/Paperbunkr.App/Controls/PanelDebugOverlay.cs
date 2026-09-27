using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.Services.Reader.Panels;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Draws the panels the detector found on the page on screen as numbered rectangles (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md section 5), so a user can see exactly
/// what guided view and smart zoom will use and report pages it gets wrong. A confident detection is drawn solid in the accent colour; a page the detector gave up on gets one dashed outline of the
/// whole page and a caption. Pure drawing: not hit-testable, colours from theme resources.
/// </summary>
public sealed class PanelDebugOverlay : Control
{
    public static readonly StyledProperty<Rect> PageRectProperty =
        AvaloniaProperty.Register<PanelDebugOverlay, Rect>(nameof(PageRect));

    public static readonly StyledProperty<IReadOnlyList<PanelRect>?> PanelsProperty =
        AvaloniaProperty.Register<PanelDebugOverlay, IReadOnlyList<PanelRect>?>(nameof(Panels));

    public static readonly StyledProperty<bool> ConfidentProperty =
        AvaloniaProperty.Register<PanelDebugOverlay, bool>(nameof(Confident));

    static PanelDebugOverlay()
    {
        AffectsRender<PanelDebugOverlay>(PageRectProperty, PanelsProperty, ConfidentProperty);
    }

    public PanelDebugOverlay()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>Where the page is on the canvas (screen space), so panel fractions can be placed on it.</summary>
    public Rect PageRect { get => GetValue(PageRectProperty); set => SetValue(PageRectProperty, value); }

    /// <summary>The panels in the orientation shown, in reading order.</summary>
    public IReadOnlyList<PanelRect>? Panels { get => GetValue(PanelsProperty); set => SetValue(PanelsProperty, value); }

    /// <summary>False when the single rectangle is only the whole page because detection gave up.</summary>
    public bool Confident { get => GetValue(ConfidentProperty); set => SetValue(ConfidentProperty, value); }

    /// <summary>The panels as rectangles on the canvas: each fraction scaled onto <paramref name="pageRect"/>.</summary>
    internal static IReadOnlyList<Rect> ToScreenRects(Rect pageRect, IReadOnlyList<PanelRect> panels)
    {
        var result = new List<Rect>(panels.Count);
        foreach (var p in panels)
        {
            result.Add(new Rect(pageRect.X + (p.X * pageRect.Width), pageRect.Y + (p.Y * pageRect.Height), p.Width * pageRect.Width, p.Height * pageRect.Height));
        }

        return result;
    }

    public override void Render(DrawingContext context)
    {
        if (Panels is not { Count: > 0 } panels || PageRect.Width <= 0 || PageRect.Height <= 0)
        {
            return;
        }

        var accent = ResolveBrush("PbAccentBrush", Color.FromRgb(0x5B, 0x8D, 0xEF));
        var text = ResolveBrush("PbAccentTextBrush", Colors.White);
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

        if (!Confident)
        {
            var dashed = new Pen(accent, 2, new DashStyle([4, 3], 0));
            context.DrawRectangle(null, dashed, PageRect.Deflate(3));
            DrawBadge(context, "No panels found - the whole page is one step", PageRect.Deflate(3).TopLeft + new Point(8, 8), accent, text, typeface, wide: true);
            return;
        }

        var rects = ToScreenRects(PageRect, panels);
        var pen = new Pen(accent, 2);
        for (int i = 0; i < rects.Count; i++)
        {
            using (context.PushOpacity(0.14))
            {
                context.DrawRectangle(accent, null, rects[i]);
            }

            context.DrawRectangle(null, pen, rects[i]);
            DrawBadge(context, (i + 1).ToString(CultureInfo.InvariantCulture), rects[i].TopLeft + new Point(6, 6), accent, text, typeface, wide: false);
        }
    }

    private static void DrawBadge(DrawingContext context, string label, Point at, IBrush fill, IBrush textBrush, Typeface typeface, bool wide)
    {
        var formatted = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 13, textBrush);
        double width = wide ? formatted.Width + 14 : Math.Max(22, formatted.Width + 10);
        var box = new Rect(at.X, at.Y, width, 22);
        context.DrawRectangle(fill, null, box, 9, 9);
        context.DrawText(formatted, new Point(box.X + ((box.Width - formatted.Width) / 2), box.Y + ((box.Height - formatted.Height) / 2)));
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? found) && found is IBrush brush ? brush : new SolidColorBrush(fallback);
}
