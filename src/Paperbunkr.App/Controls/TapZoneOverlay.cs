using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Draws a tap zone layout as coloured regions labelled Previous / Next / Menu / Left / Right (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md section 4).
/// It samples <see cref="TapZoneResolver"/> itself, so what it shows is exactly what a tap does. Used as the live preview in Preferences and as the reader's brief "Show tap zones"
/// flash. Pure drawing: not hit-testable, colours come from theme resources so it follows the skin.
/// </summary>
public sealed class TapZoneOverlay : Control
{
    public static readonly StyledProperty<TapZoneLayout> LayoutProperty =
        AvaloniaProperty.Register<TapZoneOverlay, TapZoneLayout>(nameof(Layout), defaultValue: TapZoneLayout.Default);

    public static readonly StyledProperty<TapZoneInvert> InvertProperty =
        AvaloniaProperty.Register<TapZoneOverlay, TapZoneInvert>(nameof(Invert), defaultValue: TapZoneInvert.None);

    public static readonly StyledProperty<bool> IsRightToLeftProperty =
        AvaloniaProperty.Register<TapZoneOverlay, bool>(nameof(IsRightToLeft));

    public static readonly StyledProperty<bool> IsContinuousProperty =
        AvaloniaProperty.Register<TapZoneOverlay, bool>(nameof(IsContinuous));

    /// <summary>Draws a page-shaped frame around the zones (Preferences preview) instead of filling the whole control (reader flash).</summary>
    public static readonly StyledProperty<bool> ShowFrameProperty =
        AvaloniaProperty.Register<TapZoneOverlay, bool>(nameof(ShowFrame));

    private const int Cells = 36;

    static TapZoneOverlay()
    {
        AffectsRender<TapZoneOverlay>(LayoutProperty, InvertProperty, IsRightToLeftProperty, IsContinuousProperty, ShowFrameProperty);
    }

    public TapZoneOverlay()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public TapZoneLayout Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }

    public TapZoneInvert Invert { get => GetValue(InvertProperty); set => SetValue(InvertProperty, value); }

    public bool IsRightToLeft { get => GetValue(IsRightToLeftProperty); set => SetValue(IsRightToLeftProperty, value); }

    public bool IsContinuous { get => GetValue(IsContinuousProperty); set => SetValue(IsContinuousProperty, value); }

    public bool ShowFrame { get => GetValue(ShowFrameProperty); set => SetValue(ShowFrameProperty, value); }

    /// <summary>What the overlay says when the layout has no zones at all (disabled, or the default in continuous mode).</summary>
    public static string EmptyCaption => "No tap zones";

    /// <summary>Short label drawn inside a region.</summary>
    internal static string LabelFor(TapAction action) => action switch
    {
        TapAction.Previous => "Previous",
        TapAction.Next => "Next",
        TapAction.Left => "Left",
        TapAction.Right => "Right",
        TapAction.Menu => "Menu",
        _ => string.Empty,
    };

    /// <summary>
    /// The layout as a list of rectangles on the unit square, one per run of equal actions (rows with identical runs are merged vertically). Sampled from the resolver
    /// with touch input so the menu area shows; empty when nothing would react.
    /// </summary>
    internal static IReadOnlyList<(Rect Rect, TapAction Action)> BuildRegions(TapZoneLayout layout, TapZoneInvert invert, bool rightToLeft, bool continuous)
    {
        var size = new Size(Cells, Cells);
        var rows = new List<List<(int Start, int End, TapAction Action)>>();
        for (int row = 0; row < Cells; row++)
        {
            var runs = new List<(int Start, int End, TapAction Action)>();
            int start = 0;
            var current = Sample(0, row);
            for (int col = 1; col < Cells; col++)
            {
                var action = Sample(col, row);
                if (action != current)
                {
                    runs.Add((start, col, current));
                    start = col;
                    current = action;
                }
            }

            runs.Add((start, Cells, current));
            rows.Add(runs);
        }

        var result = new List<(Rect, TapAction)>();
        int bandStart = 0;
        for (int row = 1; row <= Cells; row++)
        {
            if (row < Cells && SameRuns(rows[row], rows[bandStart]))
            {
                continue;
            }

            foreach (var (runStart, runEnd, action) in rows[bandStart])
            {
                if (action != TapAction.None)
                {
                    result.Add((new Rect(runStart / (double)Cells, bandStart / (double)Cells, (runEnd - runStart) / (double)Cells, (row - bandStart) / (double)Cells), action));
                }
            }

            bandStart = row;
        }

        return result;

        TapAction Sample(int col, int row) => TapZoneResolver.Resolve(new Point(col + 0.5, row + 0.5), size, layout, invert, TapInput.Touch, true, rightToLeft, false, continuous);

        static bool SameRuns(List<(int Start, int End, TapAction Action)> a, List<(int Start, int End, TapAction Action)> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    public override void Render(DrawingContext context)
    {
        var area = Bounds;
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        var frame = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (ShowFrame)
        {
            // A page-shaped (3:4) frame centred in the available space.
            double width = Math.Min(Bounds.Width, Bounds.Height * 0.75);
            double height = width / 0.75;
            frame = new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
            context.DrawRectangle(null, new Pen(ResolveBrush("PbBorderBrush", Color.FromRgb(0x44, 0x47, 0x52)), 1), frame, 4, 4);
        }

        var regions = BuildRegions(Layout, Invert, IsRightToLeft, IsContinuous);
        var typeface = new Typeface(FontFamily.Default);
        var textBrush = ResolveBrush("PbTextBrush", Colors.White);
        if (regions.Count == 0)
        {
            DrawCentered(context, EmptyCaption, frame, typeface, 12, textBrush);
            return;
        }

        foreach (var (unit, action) in regions)
        {
            var rect = new Rect(frame.X + (unit.X * frame.Width), frame.Y + (unit.Y * frame.Height), unit.Width * frame.Width, unit.Height * frame.Height).Deflate(1);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            using (context.PushOpacity(action == TapAction.Menu ? 0.16 : 0.32))
            {
                context.DrawRectangle(ResolveBrush(BrushKeyFor(action), Colors.Gray), null, rect);
            }

            if (rect.Width >= 46 && rect.Height >= 18)
            {
                DrawCentered(context, LabelFor(action), rect, typeface, 11, textBrush);
            }
        }
    }

    private static string BrushKeyFor(TapAction action) => action switch
    {
        TapAction.Previous or TapAction.Left => "PbAccentBrush",
        TapAction.Next or TapAction.Right => "PbSuccessBrush",
        _ => "PbTextFaintBrush",
    };

    private static void DrawCentered(DrawingContext context, string text, Rect area, Typeface typeface, double size, IBrush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush);
        context.DrawText(formatted, new Point(area.X + ((area.Width - formatted.Width) / 2), area.Y + ((area.Height - formatted.Height) / 2)));
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? found) && found is IBrush brush ? brush : new SolidColorBrush(fallback);
}
