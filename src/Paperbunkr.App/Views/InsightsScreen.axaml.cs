using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Views;

/// <summary>
/// Code-behind for the Insights screen. Explicit partial class in the same commit as the .axaml per
/// the AVLN2000 build gotcha (see <see cref="EventsScreen"/>'s note). Owns rendering the Stats tab's
/// two ScottPlot bar charts (Reading pace, Publication year) - ScottPlot's API is imperative, so the
/// data can't be data-bound; <see cref="StatsScreenViewModel.ChartsChanged"/> fires after each Stats
/// refresh and this redraws them (docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-design.md §9).
/// </summary>
public partial class InsightsScreen : UserControl
{
    private InsightsScreenViewModel? _subscribed;

    // What each chart is currently showing, so a hover can be described (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #16).
    private IReadOnlyList<(string Label, int Value)> _paceBars = Array.Empty<(string, int)>();
    private IReadOnlyList<(string Label, int Value)> _yearBars = Array.Empty<(string, int)>();
    private IReadOnlyList<string> _growthMonths = Array.Empty<string>();
    private IReadOnlyList<(string Name, double[] Values)> _growthSeries = Array.Empty<(string, double[])>();
    private ScottPlot.Plottables.VerticalLine? _growthCursor;

    public InsightsScreen()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rebind();

        AttachHover(PaceChart, (x, y) => InsightsChartHover.DescribeBar(x, y, _paceBars, "finished"));
        AttachHover(PublicationYearChart, (x, y) => InsightsChartHover.DescribeBar(x, y, _yearBars, "issues"));
        AttachHover(GrowthChart, (x, _) => InsightsChartHover.DescribeGrowth(x, _growthMonths, _growthSeries), moveCursor: true);
    }

    /// <summary>Shows a value tooltip while the pointer is over a chart, and (growth chart) a vertical cursor line at the hovered month. The
    /// describer gets data-space coordinates and returns the text, or null for "nothing here".</summary>
    private void AttachHover(ScottPlot.Avalonia.AvaPlot chart, Func<double, double, string?> describe, bool moveCursor = false)
    {
        chart.PointerMoved += (_, e) =>
        {
            var point = e.GetPosition(chart);
            float scale = chart.DisplayScale;
            var coordinates = chart.Plot.GetCoordinates(new ScottPlot.Pixel((float)point.X * scale, (float)point.Y * scale));
            string? text = describe(coordinates.X, coordinates.Y);

            if (moveCursor && _growthCursor is not null)
            {
                bool show = text is not null;
                if (show)
                {
                    _growthCursor.Position = Math.Round(coordinates.X);
                }

                if (_growthCursor.IsVisible != show || show)
                {
                    _growthCursor.IsVisible = show;
                    chart.Refresh();
                }
            }

            if (text is null)
            {
                ToolTip.SetIsOpen(chart, false);
                return;
            }

            ToolTip.SetTip(chart, text);
            ToolTip.SetPlacement(chart, Avalonia.Controls.PlacementMode.Pointer);
            ToolTip.SetIsOpen(chart, true);
        };
        chart.PointerExited += (_, _) =>
        {
            ToolTip.SetIsOpen(chart, false);
            if (moveCursor && _growthCursor is { IsVisible: true })
            {
                _growthCursor.IsVisible = false;
                chart.Refresh();
            }
        };
    }

    private void Rebind()
    {
        if (_subscribed is not null)
        {
            _subscribed.Stats.ChartsChanged -= RenderCharts;
        }

        _subscribed = DataContext as InsightsScreenViewModel;
        if (_subscribed is not null)
        {
            _subscribed.Stats.ChartsChanged += RenderCharts;
            if (_subscribed.Stats.Snapshot is { } snap)
            {
                RenderCharts(snap);
            }
        }
    }

    private void RenderCharts(StatsSnapshot snapshot)
    {
        RenderPace(snapshot);
        RenderPublicationYear(snapshot);
        RenderLibraryGrowth(snapshot);
    }

    /// <summary>Cumulative-line chart, one line per category under the selected Stack-by (or a
    /// single "Total" line) - design §6.4. Implemented as separate cumulative lines rather than a
    /// literal filled stacked-area: ScottPlot 5 has no built-in stacked-area plottable, and manually
    /// computing fill-between-lines geometry is a correctness risk this project has already been
    /// burned by once this session (the blank-<c>StatCard</c> bug) for no real gain - multiple
    /// cumulative lines convey the same "growth over time, by category" information.</summary>
    private void RenderLibraryGrowth(StatsSnapshot snapshot)
    {
        var plot = GrowthChart.Plot;
        plot.Clear();
        InsightsChartTheme.Apply(plot);

        _growthMonths = Array.Empty<string>();
        _growthSeries = Array.Empty<(string, double[])>();
        _growthCursor = null;

        var vm = _subscribed?.Stats;
        var rawPoints = snapshot.LibraryGrowth.Points;
        if (vm is null || rawPoints.Count == 0)
        {
            GrowthChart.Refresh();
            return;
        }

        IEnumerable<GrowthPoint> units = vm.GrowthMeasure == GrowthMeasure.Series
            ? rawPoints.Where(p => p.SeriesId is null)
                .Concat(rawPoints.Where(p => p.SeriesId is not null)
                    .GroupBy(p => p.SeriesId!.Value)
                    .Select(g => g.OrderBy(p => p.AddedDate).First()))
            : rawPoints;

        var unitList = units.ToList();
        if (unitList.Count == 0)
        {
            GrowthChart.Refresh();
            return;
        }

        Func<GrowthPoint, string> categoryOf = vm.GrowthStackBy switch
        {
            GrowthStackBy.ReadingStatus => p => p.ReadingStatus,
            GrowthStackBy.MediaType => p => p.MediaType,
            GrowthStackBy.ContentRating => p => p.ContentRating,
            _ => _ => "Total",
        };

        var months = unitList.Select(p => new DateTime(p.AddedDate.Year, p.AddedDate.Month, 1))
            .Distinct().OrderBy(d => d).ToList();
        double[] xs = months.Select((_, i) => (double)i).ToArray();

        var palette = InsightsChartTheme.CategoricalPalette;
        var categories = unitList.GroupBy(categoryOf).OrderByDescending(g => g.Count()).Take(palette.Count).ToList();

        double maxCumulative = 0;
        var growthSeries = new List<(string Name, double[] Values)>();
        foreach (var (category, index) in categories.Select((c, i) => (c, i)))
        {
            var perMonth = category.GroupBy(p => new DateTime(p.AddedDate.Year, p.AddedDate.Month, 1))
                .ToDictionary(g => g.Key, g => g.Count());

            double[] ys = new double[months.Count];
            double running = 0;
            for (int i = 0; i < months.Count; i++)
            {
                if (perMonth.TryGetValue(months[i], out int added))
                {
                    running += added;
                }

                ys[i] = running;
            }

            maxCumulative = Math.Max(maxCumulative, running);
            growthSeries.Add((category.Key, ys));

            var scatter = plot.Add.Scatter(xs, ys);
            scatter.Color = palette[index];
            scatter.LineWidth = 2;
            scatter.MarkerSize = 0;
            scatter.LegendText = category.Key;
        }

        _growthMonths = months.Select(m => m.ToString("MMM yy")).ToList();
        _growthSeries = growthSeries;
        _growthCursor = plot.Add.VerticalLine(0);
        _growthCursor.IsVisible = false;
        _growthCursor.Color = InsightsChartTheme.Muted;
        _growthCursor.LineWidth = 1;

        int tickStep = Math.Max(1, months.Count / 8);
        var tickPositions = new List<double>();
        var tickLabels = new List<string>();
        for (int i = 0; i < months.Count; i += tickStep)
        {
            tickPositions.Add(i);
            tickLabels.Add(months[i].ToString("MMM yy"));
        }

        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(tickPositions.ToArray(), tickLabels.ToArray());
        plot.Axes.Bottom.MajorTickStyle.Length = 0;
        IntegerLeftTicks(plot, (int)Math.Ceiling(maxCumulative));
        plot.Axes.Margins(bottom: 0, top: 0.15);

        if (categories.Count > 1)
        {
            plot.ShowLegend(ScottPlot.Edge.Bottom);
        }
        else
        {
            plot.HideLegend();
        }

        GrowthChart.Refresh();
    }

    private void RenderPace(StatsSnapshot snapshot)
    {
        var plot = PaceChart.Plot;
        plot.Clear();
        InsightsChartTheme.Apply(plot);

        var buckets = snapshot.Pace;
        _paceBars = buckets.Select(b => (b.Label, b.Finished)).ToList();
        if (buckets.Count == 0)
        {
            PaceChart.Refresh();
            return;
        }

        var accent = InsightsChartTheme.Accent;
        var bars = buckets.Select((b, i) => new ScottPlot.Bar
        {
            Position = i,
            Value = b.Finished,
            FillColor = accent,
            LineWidth = 0,
        }).ToList();

        plot.Add.Bars(bars);
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            buckets.Select((_, i) => (double)i).ToArray(),
            buckets.Select(b => b.Label).ToArray());
        plot.Axes.Bottom.MajorTickStyle.Length = 0;
        IntegerLeftTicks(plot, buckets.Max(b => b.Finished));
        plot.Axes.Margins(bottom: 0, top: 0.15);
        plot.HideLegend();
        PaceChart.Refresh();
    }

    private void RenderPublicationYear(StatsSnapshot snapshot)
    {
        var plot = PublicationYearChart.Plot;
        plot.Clear();
        InsightsChartTheme.Apply(plot);

        var buckets = snapshot.PublicationYear;
        _yearBars = buckets.Select(b => (b.Year.ToString(), b.Count)).ToList();
        if (buckets.Count == 0)
        {
            PublicationYearChart.Refresh();
            return;
        }

        var accent = InsightsChartTheme.Accent;
        var bars = buckets.Select((b, i) => new ScottPlot.Bar
        {
            Position = i,
            Value = b.Count,
            FillColor = accent,
            LineWidth = 0,
        }).ToList();

        plot.Add.Bars(bars);
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            buckets.Select((_, i) => (double)i).ToArray(),
            buckets.Select(b => b.Year.ToString()).ToArray());
        plot.Axes.Bottom.MajorTickStyle.Length = 0;
        IntegerLeftTicks(plot, buckets.Max(b => b.Count));
        plot.Axes.Margins(bottom: 0, top: 0.15);
        plot.HideLegend();
        PublicationYearChart.Refresh();
    }

    /// <summary>Whole-number y-ticks only - "0.5 issues" is nonsense.</summary>
    private static void IntegerLeftTicks(ScottPlot.Plot plot, int max)
    {
        int top = System.Math.Max(1, max);
        int step = top <= 5 ? 1 : (int)System.Math.Ceiling(top / 5.0);
        var positions = new System.Collections.Generic.List<double>();
        for (int v = 0; v <= top; v += step)
        {
            positions.Add(v);
        }

        plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions.ToArray(), positions.Select(v => v.ToString("0")).ToArray());
    }
}
