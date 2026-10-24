using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Views.Stats;

/// <summary>
/// Weekday x hour grid for the Reading rhythm card (docs/superpowers/specs/2026-10-12-insights-pitch-completion-design.md §1): seven rows
/// (Sunday first, like <see cref="ActivityHeatmap"/>), 24 columns, each cell the number of reading sessions that started in that local hour,
/// drawn in the app's accent at an opacity that scales with the busiest cell. Hovering a cell shows "Tue 20:00 · 14 sessions".
/// </summary>
public sealed class RhythmHeatmap : Control
{
    private const double LabelWidth = 30;

    private const double AxisHeight = 14;

    private const double MaxCell = 26;

    private static readonly string[] Days = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    public static readonly StyledProperty<RhythmData?> DataProperty =
        AvaloniaProperty.Register<RhythmHeatmap, RhythmData?>(nameof(Data));

    static RhythmHeatmap()
    {
        AffectsRender<RhythmHeatmap>(DataProperty);
        AffectsMeasure<RhythmHeatmap>(DataProperty);
    }

    public RhythmData? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? LabelWidth + 24 * 20 : availableSize.Width;
        double cell = Math.Clamp((width - LabelWidth) / 24, 8, MaxCell);
        return new Size(LabelWidth + 24 * cell, AxisHeight + 7 * cell);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data || Bounds.Width <= LabelWidth || Bounds.Height <= AxisHeight)
        {
            return;
        }

        double cell = Math.Min(MaxCell, Math.Min((Bounds.Width - LabelWidth) / 24, (Bounds.Height - AxisHeight) / 7));
        if (cell <= 0)
        {
            return;
        }

        var accent = ResolveColor("PbAccentColor", Color.FromRgb(0xC9, 0x80, 0x3F));
        var track = ResolveBrush("PbSurface3Brush", Color.FromRgb(0x1B, 0x1E, 0x24));
        var muted = ResolveBrush("PbTextMutedBrush", Color.FromRgb(0x8A, 0x90, 0x9A));
        int max = 0;
        foreach (int v in data.SessionsByWeekdayHour)
        {
            max = Math.Max(max, v);
        }

        for (int hour = 0; hour < 24; hour += 3)
        {
            DrawText(context, hour.ToString("00", CultureInfo.InvariantCulture), muted, LabelWidth + hour * cell, 0);
        }

        for (int day = 0; day < 7; day++)
        {
            DrawText(context, Days[day], muted, 0, AxisHeight + day * cell + Math.Max(0, (cell - 12) / 2));
            for (int hour = 0; hour < 24; hour++)
            {
                int count = data.SessionsByWeekdayHour[day, hour];
                IBrush brush = count == 0
                    ? track
                    : new SolidColorBrush(accent, max <= 1 ? 1.0 : 0.25 + 0.75 * count / max);
                context.DrawRectangle(brush, null, new Rect(LabelWidth + hour * cell + 1, AxisHeight + day * cell + 1, cell - 2, cell - 2), 2, 2);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        ToolTip.SetTip(this, TipAt(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ToolTip.SetTip(this, null);
    }

    private string? TipAt(Point p)
    {
        if (Data is not { } data)
        {
            return null;
        }

        double cell = Math.Min(MaxCell, Math.Min((Bounds.Width - LabelWidth) / 24, (Bounds.Height - AxisHeight) / 7));
        if (cell <= 0)
        {
            return null;
        }

        int hour = (int)Math.Floor((p.X - LabelWidth) / cell);
        int day = (int)Math.Floor((p.Y - AxisHeight) / cell);
        if (hour is < 0 or > 23 || day is < 0 or > 6)
        {
            return null;
        }

        int count = data.SessionsByWeekdayHour[day, hour];
        return $"{Days[day]} {hour:00}:00 · {count:N0} {(count == 1 ? "session" : "sessions")}";
    }

    private void DrawText(DrawingContext context, string text, IBrush brush, double x, double y)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 10, brush);
        context.DrawText(formatted, new Point(x, y));
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);

    private Color ResolveColor(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is Color color ? color : fallback;
}
