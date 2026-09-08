using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Views.Stats;

/// <summary>
/// Reading-activity calendar grid for the Stats screen (docs/superpowers/specs/2026-09-08-stats-v2-
/// mangabaka-design.md §6.3) - one cell per day, columns are weeks, rows are weekdays (Mon-Sun),
/// covering the last 26 weeks. Colored by event count that day at increasing opacity of the app's
/// own accent brush, same "app's real palette, not an imported color scale" approach every other
/// hand-rolled chart in this screen takes. History only exists from 2026-09-05 forward (the
/// ReadingEvent log's introduction), so this starts sparse and fills in over time - same accepted,
/// self-correcting trade-off the pace chart shipped with in v1.
/// </summary>
public sealed class ActivityHeatmap : Control
{
    private const int Weeks = 26;

    public static readonly StyledProperty<IReadOnlyDictionary<DateOnly, int>> DataProperty =
        AvaloniaProperty.Register<ActivityHeatmap, IReadOnlyDictionary<DateOnly, int>>(
            nameof(Data), new Dictionary<DateOnly, int>());

    static ActivityHeatmap()
    {
        AffectsRender<ActivityHeatmap>(DataProperty);
        AffectsMeasure<ActivityHeatmap>(DataProperty);
    }

    public IReadOnlyDictionary<DateOnly, int> Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? Weeks * 14 : availableSize.Width;
        return new Size(width, 7 * 14);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        double cell = Math.Min(Bounds.Width / Weeks, Bounds.Height / 7);
        if (cell <= 0)
        {
            return;
        }

        var accentColor = ResolveColor("PbAccentColor", Color.FromRgb(0xC9, 0x80, 0x3F));
        var track = ResolveBrush("PbSurface3Brush", Color.FromRgb(0x1B, 0x1E, 0x24));
        int max = Data.Count > 0 ? Data.Values.Max() : 0;

        // Sunday-first grid, most-recent week rightmost. Today's own weekday sets the last column's
        // bottom edge; walk back 26*7 days from there so every column is a full Sun-Sat week.
        var today = DateOnly.FromDateTime(DateTime.Now.Date);
        var gridStart = today.AddDays(-(int)today.DayOfWeek).AddDays(-7 * (Weeks - 1));

        for (int week = 0; week < Weeks; week++)
        {
            for (int day = 0; day < 7; day++)
            {
                var date = gridStart.AddDays(week * 7 + day);
                if (date > today)
                {
                    continue;
                }

                int count = Data.TryGetValue(date, out int c) ? c : 0;
                IBrush brush = count == 0
                    ? track
                    : new SolidColorBrush(accentColor, max <= 1 ? 1.0 : 0.25 + 0.75 * count / max);

                var rect = new Rect(week * cell + 1, day * cell + 1, cell - 2, cell - 2);
                context.DrawRectangle(brush, null, rect, 2, 2);
            }
        }
    }

    private IBrush ResolveBrush(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : new SolidColorBrush(fallback);

    private Color ResolveColor(string key, Color fallback)
        => this.TryFindResource(key, out object? value) && value is Color color ? color : fallback;
}
