using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Models;
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

        // None of these charts have an interactive zoom feature this screen relies on - just hover
        // tooltips (above) - so ScottPlot's default "mouse wheel zooms the plot" behavior only gets in
        // the way here: scrolling the Trends tab with the cursor over any chart would otherwise zoom
        // that one chart instead of scrolling the page (found on-screen review, 2026-09-23).
        // HandleMouseWheelEvent alone does NOT stop this - that flag only controls whether the pointer-
        // wheel event bubbles to the ScrollViewer, not whether ScottPlot's own zoom interaction fires.
        // The actual fix (per ScottPlot's own "Plot in a Scroll Viewer" Avalonia demo) is removing the
        // MouseWheelZoom response from each chart's UserInputProcessor outright.
        foreach (var chart in new[] { GrowthChart, PaceChart, BurnDownChart, RatingsChart, PublicationYearChart })
        {
            chart.HandleMouseWheelEvent = false;
            chart.UserInputProcessor.RemoveAll<ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom>();
        }
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
        RenderBurnDown(snapshot);
        RenderRatings(snapshot);
    }

    /// <summary>Single cumulative-line chart of overall library growth (design §6.4, Stack-by-category
    /// removed 2026-09-24 per user feedback - composition breakdowns now live in their own donut/bar
    /// cards instead of being overlaid here as multiple lines behind a wrapping legend). No legend at
    /// all - one line needs none - which also sidesteps the <c>Plot.ShowLegend(Edge)</c> panel-leak
    /// this chart used to trip on every re-render (see git history for the fix, since removed with the
    /// feature that caused it to matter).</summary>
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

        var months = unitList.Select(p => new DateTime(p.AddedDate.Year, p.AddedDate.Month, 1))
            .Distinct().OrderBy(d => d).ToList();
        double[] xs = months.Select((_, i) => (double)i).ToArray();

        var perMonth = unitList.GroupBy(p => new DateTime(p.AddedDate.Year, p.AddedDate.Month, 1))
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

        var scatter = plot.Add.Scatter(xs, ys);
        scatter.Color = InsightsChartTheme.Accent;
        scatter.LineWidth = 2;
        scatter.MarkerSize = 0;

        _growthMonths = months.Select(m => m.ToString("MMM yy")).ToList();
        _growthSeries = new List<(string Name, double[] Values)> { ("Total", ys) };
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
        IntegerLeftTicks(plot, (int)Math.Ceiling(running));
        plot.Axes.Margins(bottom: 0, top: 0.15);
        plot.HideLegend();

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

    /// <summary>Score distribution chart (Trends tab, Composition group) - the star-rating counts
    /// (<see cref="StatsSnapshot.Ratings"/>, always 5 buckets for 1-5 stars) as bars, same shape as
    /// <see cref="RenderPublicationYear"/>. Found never wired up at all during the 2026-09-23 Insights
    /// redesign's on-screen check: <see cref="RenderCharts"/> never called a rating-chart renderer, so
    /// <c>RatingsChart</c> sat on ScottPlot's default empty-axis range (-10 to 10) regardless of
    /// <see cref="StatsScreenViewModel.HasRatings"/> - a pre-existing bug, not something this redesign
    /// introduced. No hover wiring, matching <see cref="RenderBurnDown"/>'s own no-hover precedent.</summary>
    private void RenderRatings(StatsSnapshot snapshot)
    {
        var plot = RatingsChart.Plot;
        plot.Clear();
        InsightsChartTheme.Apply(plot);

        var buckets = snapshot.Ratings;
        if (buckets.Count == 0 || buckets.All(b => b.Count == 0))
        {
            RatingsChart.Refresh();
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
            buckets.Select(b => $"{b.Stars}★").ToArray());
        plot.Axes.Bottom.MajorTickStyle.Length = 0;
        IntegerLeftTicks(plot, buckets.Max(b => b.Count));
        plot.Axes.Margins(bottom: 0, top: 0.15);
        plot.HideLegend();
        RatingsChart.Refresh();
    }

    /// <summary>Backlog burn-down chart (docs/superpowers/specs/2026-09-22-insights-backlog-burndown-
    /// design.md) - a solid line for real snapshot history plus, when the backlog is trending down, a
    /// dashed continuation to the projected zero-crossing. No hover wiring (matches
    /// <see cref="RatingsChart"/>'s existing no-hover precedent - not every chart on this screen has one).
    /// Both series share the same x-space (day offset from the earlier of the two series' first dates) so
    /// the dashed line visually continues from where the solid one ends.</summary>
    private void RenderBurnDown(StatsSnapshot snapshot)
    {
        var plot = BurnDownChart.Plot;
        plot.Clear();
        InsightsChartTheme.Apply(plot);

        var burnDown = snapshot.BurnDown;
        var points = burnDown.Points;
        if (!burnDown.HasEnoughHistory && !burnDown.IsCleared || points.Count == 0)
        {
            BurnDownChart.Refresh();
            return;
        }

        var allDates = points.Select(p => p.Date).Concat(burnDown.ProjectedPoints?.Select(p => p.Date) ?? Array.Empty<DateOnly>()).ToList();
        var reference = allDates.Min();
        double X(DateOnly d) => d.DayNumber - reference.DayNumber;

        var solid = plot.Add.Scatter(points.Select(p => X(p.Date)).ToArray(), points.Select(p => (double)p.BacklogCount).ToArray());
        solid.Color = InsightsChartTheme.Accent;
        solid.LineWidth = 2;
        solid.MarkerSize = 0;

        int maxY = points.Max(p => p.BacklogCount);

        if (burnDown.ProjectedPoints is { } projected)
        {
            var dashed = plot.Add.Scatter(projected.Select(p => X(p.Date)).ToArray(), projected.Select(p => (double)p.BacklogCount).ToArray());
            dashed.Color = InsightsChartTheme.Muted;
            dashed.LineWidth = 2;
            dashed.MarkerSize = 0;
            dashed.LinePattern = ScottPlot.LinePattern.Dashed;
            maxY = System.Math.Max(maxY, projected.Max(p => p.BacklogCount));
        }

        int tickStep = System.Math.Max(1, allDates.Count / 8);
        var orderedDates = allDates.Distinct().OrderBy(d => d).ToList();
        var tickPositions = new System.Collections.Generic.List<double>();
        var tickLabels = new System.Collections.Generic.List<string>();
        for (int i = 0; i < orderedDates.Count; i += tickStep)
        {
            tickPositions.Add(X(orderedDates[i]));
            tickLabels.Add(orderedDates[i].ToString("MMM d"));
        }

        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(tickPositions.ToArray(), tickLabels.ToArray());
        plot.Axes.Bottom.MajorTickStyle.Length = 0;
        IntegerLeftTicks(plot, maxY);
        plot.Axes.Margins(bottom: 0, top: 0.15);
        plot.HideLegend();
        BurnDownChart.Refresh();
    }

    /// <summary>
    /// Renders the Recap tab's poster (docs/superpowers/specs/2026-09-23-insights-year-in-review-recap-
    /// design.md's Export section) and saves it as a PNG. <see cref="RenderTargetBitmap.Render"/> requires
    /// its target control to be attached to a visible window (verified against the real Avalonia docs
    /// during design, not guessed) - <see cref="RecapPosterView"/> is added to <c>PosterExportHost</c>
    /// (already part of this screen's live visual tree whenever the Recap tab could be showing) only for
    /// the duration of the render, then removed.
    /// </summary>
    private async void OnExportPosterClick(object? sender, RoutedEventArgs e)
    {
        if (_subscribed?.Recap.Snapshot is not { } snap)
        {
            return;
        }

        string? path = await new FilePickerService().PickSaveFileAsync(
            "Export Year in Review", $"paperbunkr-recap-{snap.Year}.png", "png", "PNG Image");
        if (path is null)
        {
            return;
        }

        var poster = new RecapPosterView
        {
            DataContext = RecapPosterDisplayModel.From(snap.Year, snap.IsCurrentYear, _subscribed.Recap.Tiles),
        };
        PosterExportHost.Children.Add(poster);
        try
        {
            var size = new Size(1080, 1920);
            poster.Measure(size);
            poster.Arrange(new Rect(size));
            var bitmap = new RenderTargetBitmap(new PixelSize(1080, 1920));
            bitmap.Render(poster);
            bitmap.Save(path);
        }
        catch (Exception ex)
        {
            _subscribed.Activity?.RaiseAlert(new ActivityAlert
            {
                Severity = ActivityAlertSeverity.Error,
                Title = "Couldn't export Year in Review",
                Detail = ex.Message,
                DedupeKey = "recap-export-failed",
            });
        }
        finally
        {
            PosterExportHost.Children.Remove(poster);
        }
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
