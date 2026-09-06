using System.Linq;
using Avalonia.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Views;

/// <summary>
/// Code-behind for the Insights dashboard. Explicit partial class in the same commit as the .axaml
/// per the AVLN2000 build gotcha (see <see cref="EventsScreen"/>'s note). Also owns rendering the
/// two ScottPlot bar charts - ScottPlot's API is imperative, so the pace/ratings data can't be data-
/// bound; the view-model raises <see cref="InsightsScreenViewModel.PaceOrRatingsChanged"/> after each
/// refresh and this redraws them (docs/superpowers/specs/2026-09-05-insights-dashboard-design.md §9).
/// </summary>
public partial class InsightsScreen : UserControl
{
    private InsightsScreenViewModel? _subscribed;

    public InsightsScreen()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rebind();
    }

    private void Rebind()
    {
        if (_subscribed is not null)
        {
            _subscribed.PaceOrRatingsChanged -= RenderCharts;
        }

        _subscribed = DataContext as InsightsScreenViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PaceOrRatingsChanged += RenderCharts;
            if (_subscribed.Snapshot is { } snap)
            {
                RenderCharts(snap);
            }
        }
    }

    private void RenderCharts(InsightsSnapshot snapshot)
    {
        RenderPace(snapshot);
        RenderRatings(snapshot);
    }

    private void RenderPace(InsightsSnapshot snapshot)
    {
        var plot = PaceChart.Plot;
        plot.Clear();
        InsightsChartTheme.Apply(plot);

        var buckets = snapshot.Pace;
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

    /// <summary>Whole-number y-ticks only - "0.5 issues read" is nonsense.</summary>
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

    private void RenderRatings(InsightsSnapshot snapshot)
    {
        var plot = RatingsChart.Plot;
        plot.Clear();
        InsightsChartTheme.Apply(plot);

        var accent = InsightsChartTheme.Accent;
        var bars = snapshot.Ratings.Select((r, i) => new ScottPlot.Bar
        {
            Position = i,
            Value = r.Count,
            FillColor = accent,
            LineWidth = 0,
        }).ToList();

        plot.Add.Bars(bars);
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            snapshot.Ratings.Select((_, i) => (double)i).ToArray(),
            snapshot.Ratings.Select(r => $"{r.Stars}").ToArray());
        plot.Axes.Bottom.MajorTickStyle.Length = 0;
        plot.Axes.Title.Label.Text = string.Empty;
        IntegerLeftTicks(plot, snapshot.Ratings.Count == 0 ? 1 : snapshot.Ratings.Max(r => r.Count));
        plot.Axes.Margins(bottom: 0, top: 0.15);
        plot.HideLegend();
        RatingsChart.Refresh();
    }
}
