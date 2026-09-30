using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Paperbunkr.App.Controls.EventMap;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>
/// Headless tests for <see cref="EventMapSurface"/>'s virtualization and <see cref="EventMapEdgeLayer"/>'s skin
/// reactivity (docs/superpowers/specs/2026-09-25-event-map-design.md "Headless controls").
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class EventMapSurfaceTests
{
    /// <summary>Layout, then pump the dispatcher (the ScrollViewer publishes its viewport/extent from there), then layout again - VirtualListTests' pattern.</summary>
    private static void RunLayout(Window window)
    {
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
    }

    /// <summary>The Fluent theme (ScrollViewer's template) for the duration of one test, as VirtualListTests does.</summary>
    private static void WithTheme(Action body)
    {
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        try
        {
            body();
        }
        finally
        {
            Application.Current!.Styles.Remove(theme);
        }
    }

    /// <summary>A relay map of <paramref name="rows"/> rows cycling through three series.</summary>
    private static (EventMapLayoutResult Layout, List<EventMapCardViewModel> Cards) RelayMap(int rows, EventMapDensity density = EventMapDensity.Standard)
    {
        var source = Source("Unrelated", Enumerable.Range(0, rows).Select(i => Row(X + (i % 3), (i + 1).ToString(), i + 1)).ToArray());
        var layout = EventMapLayout.Compute(source, SpineResolver.Resolve(source), EventMapFilter.All, density);
        return (layout, layout.Cells.Select(c => new EventMapCardViewModel(c, layout.Tracks[c.Track])).ToList());
    }

    private static (Window Window, ScrollViewer Scroller, EventMapSurface Surface) Host(EventMapLayoutResult layout, IReadOnlyList<EventMapCardViewModel> cards)
    {
        var surface = new EventMapSurface { Layout = layout, Cards = cards, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = surface,
        };
        var window = new Window { Content = scroller, Width = 600, Height = 300 };
        window.Show();
        RunLayout(window);
        RunLayout(window);          // the effective viewport arrives after the first arrange
        return (window, scroller, surface);
    }

    [Fact]
    public void RealizedCards_AreTheVisibleCellsPlusOverscan()
    {
        WithTheme(() =>
        {
            var (layout, cards) = RelayMap(40);
            var (window, scroller, surface) = Host(layout, cards);

            var expected = layout.CellsIn(layout.VisibleRange(new Rect(scroller.Offset.X, scroller.Offset.Y, scroller.Viewport.Width, scroller.Viewport.Height))).OrderBy(i => i).ToList();

            Assert.Equal(expected, surface.RealizedIndices.OrderBy(i => i));
            Assert.True(surface.RealizedCount < cards.Count);
            window.Close();
        });
    }

    [Fact]
    public void Scrolling_RecyclesCards_PoolNeverGrowsPastTheLargestVisibleSet()
    {
        WithTheme(() =>
        {
            var (layout, cards) = RelayMap(60);
            var (window, scroller, surface) = Host(layout, cards);
            int maxRealized = surface.RealizedCount;

            for (int step = 1; step <= 20; step++)
            {
                scroller.Offset = new Vector(step * 250, 0);
                RunLayout(window);
                RunLayout(window);
                maxRealized = Math.Max(maxRealized, surface.RealizedCount);
                var viewport = new Rect(scroller.Offset.X, scroller.Offset.Y, scroller.Viewport.Width, scroller.Viewport.Height);
                Assert.Equal(layout.CellsIn(layout.VisibleRange(viewport)).OrderBy(i => i), surface.RealizedIndices.OrderBy(i => i));
            }

            Assert.True(surface.CreatedCount <= maxRealized + 1, $"created {surface.CreatedCount} cards for at most {maxRealized} visible");
            window.Close();
        });
    }

    [Fact]
    public void AFiveHundredRowEvent_StaysBounded()
    {
        WithTheme(() =>
        {
            var (layout, cards) = RelayMap(500);
            var (window, scroller, surface) = Host(layout, cards);

            scroller.Offset = new Vector(layout.TotalSize.Width / 2, 0);
            RunLayout(window);
            RunLayout(window);

            Assert.Equal(new Size(500 * 170, 3 * 84), surface.Bounds.Size);
            Assert.InRange(surface.RealizedCount, 1, 20);
            Assert.InRange(surface.CreatedCount, 1, 40);
            window.Close();
        });
    }

    [Fact]
    public void RecycledCard_IsReboundToTheCellItNowShows()
    {
        WithTheme(() =>
        {
            var (layout, cards) = RelayMap(60);
            var (window, scroller, surface) = Host(layout, cards);

            scroller.Offset = new Vector(20 * 170, 0);
            RunLayout(window);
            RunLayout(window);

            foreach (int index in surface.RealizedIndices)
            {
                Assert.Same(cards[index], surface.CardAt(index)!.DataContext);
            }

            window.Close();
        });
    }

    [Fact]
    public void EdgeLayer_ResolvesSkinColoursAtRenderTime()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));
        var layer = new EventMapEdgeLayer { Layout = layout };
        var laneEdge = layout.Edges.First(e => e.Kind == EventMapEdgeKind.Sequence && e.ColorIndex == 0);   // lane A → PbChartBlue

        layer.Resources["PbChartBlueColor"] = Colors.Red;
        var before = ((ImmutableSolidColorBrush)layer.PenFor(laneEdge).Brush!).Color;
        layer.Resources["PbChartBlueColor"] = Colors.Lime;
        var after = ((ImmutableSolidColorBrush)layer.PenFor(laneEdge).Brush!).Color;

        Assert.Equal(Colors.Red, before);
        Assert.Equal(Colors.Lime, after);
    }

    [Fact]
    public void EdgeLayer_SelectionWidensRelatedEdges_AndFadesTheRest()
    {
        var (layout, _) = RelayMap(6);
        var layer = new EventMapEdgeLayer { Layout = layout };
        var edge = layout.Edges[0];
        double baseWidth = layer.PenFor(edge).Thickness;

        layer.RelatedSet = new HashSet<int> { edge.From, edge.To };
        var related = layer.PenFor(edge);
        layer.RelatedSet = new HashSet<int> { 99 };
        var faded = layer.PenFor(edge);

        Assert.Equal(baseWidth * 1.5, related.Thickness, 3);
        Assert.Equal(0.25, ((ImmutableSolidColorBrush)faded.Brush!).Opacity, 3);
    }


    /// <summary>
    /// The continuity map's lane headers follow the map's vertical scroll (docs/superpowers/specs/2026-09-27-continuity-map-design.md
    /// §4). Regression: the header list was arranged at the visible height, so once scrolled every lane past the first screen was
    /// clipped away and the column went blank.
    /// </summary>
    [Fact]
    public void ContinuityMap_LaneHeaders_KeepTheirFullHeight_WhenScrolled()
    {
        WithTheme(() =>
        {
            // The app-level tokens the view's StaticResources need (App.axaml isn't loaded in the headless suite).
            var resources = Application.Current!.Resources;
            var tokens = new Dictionary<string, object>
            {
                ["PbMotionEase"] = new Avalonia.Animation.Easings.CubicEaseOut(),
                ["PbIconSizeXs"] = 14d,
                ["PbIconSizeSm"] = 16d,
                ["PbIconSizeLg"] = 24d,
            };
            var added = tokens.Keys.Where(k => !resources.ContainsKey(k)).ToList();
            foreach (var key in added)
            {
                resources[key] = tokens[key];
            }

            try
            {
                var lanes = Enumerable.Range(1, 20).Select(i => new ContinuityLane(i, $"Series {i}", false)).ToArray();
                var loose = Enumerable.Range(1, 20).Select(i => new EventMapRow(i, i, 0, Paperbunkr.Data.Entities.EventMembershipRole.Core,
                    i, $"Series {i}", "1", 2000 + i, 1, false, EventMapReadState.Unread)).ToArray();
                var data = new ContinuityMapData(7, "Big", lanes, Array.Empty<ContinuityEventData>(), Array.Empty<ContinuityRelationData>(), loose,
                    new Dictionary<int, IReadOnlyList<EventMapEventRef>>(), PendingDuplicatePairs: 0);
                var vm = new EventMapViewModel(
                    activity: new Paperbunkr.App.Services.ActivityService(dispatch: a => a(), recordRun: _ => { }),
                    runInBackground: work => Task.FromResult(work()),
                    loadContinuity: _ => data);
                vm.LoadContinuityAsync(7).GetAwaiter().GetResult();

                var view = new Paperbunkr.App.Views.EventMapView { DataContext = vm };
                var window = new Window { Content = view, Width = 900, Height = 420 };
                window.Show();
                RunLayout(window);
                RunLayout(window);

                var scroller = view.FindControl<ScrollViewer>("Scroller")!;
                var headers = view.FindControl<ItemsControl>("LaneHeaders")!;
                double lanesHeight = vm.LaneHeaders.Sum(h => h.Height);
                Assert.True(lanesHeight > scroller.Viewport.Height, "the test needs more lanes than fit");

                scroller.Offset = new Vector(0, scroller.Extent.Height - scroller.Viewport.Height);
                RunLayout(window);

                Assert.True(headers.Bounds.Height >= lanesHeight - 0.5, $"headers {headers.Bounds.Height} < lanes {lanesHeight}");
                var last = headers.ContainerFromIndex(vm.LaneHeaders.Count - 1)!;
                double lastTop = last.TranslatePoint(new Point(0, 0), scroller)!.Value.Y;
                Assert.InRange(lastTop, 0, scroller.Viewport.Height);
                window.Close();
            }
            finally
            {
                foreach (var key in added)
                {
                    resources.Remove(key);
                }
            }
        });
    }
}