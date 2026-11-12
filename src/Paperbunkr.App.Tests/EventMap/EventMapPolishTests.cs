using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Paperbunkr.App.Controls.EventMap;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>M6b polish (docs/superpowers/specs/2026-11-11-continuity-map-polish-design.md): read path, You are here, trace, band art, export.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventMapPolishTests
{
    // ===================== Pure =====================

    [Fact]
    public void ReadPathRunsUpToTheLastReadIssueAndInProgressCountsAsUnread()
    {
        var layout = Layout(Source("No match",
            Row(SeriesA, "1", 1, read: EventMapReadState.Read),
            Row(SeriesB, "1", 2, read: EventMapReadState.Read),
            Row(SeriesA, "2", 3, read: EventMapReadState.InProgress),
            Row(SeriesB, "2", 4)));
        bool IsRead(int i) => layout.Cells[i].Row.ReadState == EventMapReadState.Read;
        var read = EventMapReadPath.Classify(layout, IsRead);

        Assert.Equal(layout.Edges.Count, read.Length);
        for (int e = 0; e < layout.Edges.Count; e++)
        {
            Assert.Equal(IsRead(layout.Edges[e].To), read[e]);
        }

        Assert.Contains(true, read);
        Assert.Contains(false, read);
        Assert.Equal(2, EventMapReadPath.NextUp(layout, IsRead));       // the in-progress issue is next
    }

    [Fact]
    public void NextUpPrefersTheFirstUnreadTrunkIssueOnASpineEvent()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));
        int? next = EventMapReadPath.NextUp(layout, _ => false);
        Assert.NotNull(next);
        Assert.True(layout.Cells[next!.Value].IsTrunk);
        Assert.Null(EventMapReadPath.NextUp(layout, _ => true));
    }

    [Fact]
    public void TraceHopsGrowOutwardFromTheSelectedCard()
    {
        var layout = Layout(Source("No match", Row(SeriesA, "1", 1), Row(SeriesA, "2", 2), Row(SeriesA, "3", 3), Row(SeriesA, "4", 4)));
        var all = layout.Cells.Select(c => c.Index).ToHashSet();
        var hops = EventMapTrace.Hops(layout, 1, all);
        Assert.Equal(0, hops[1]);
        Assert.Equal(1, hops[0]);
        Assert.Equal(1, hops[2]);
        Assert.Equal(2, hops[3]);

        Assert.Equal(60, EventMapTrace.Stagger(2, 1000));
        Assert.True(EventMapTrace.Stagger(10, 300) < 60, "many hops compress the stagger to fit the Travel duration");
        Assert.Equal(0, EventMapTrace.Stagger(1, 300));
    }

    [Fact]
    public void TrimCutsAPolylineByLength()
    {
        var line = new[] { new Point(0, 0), new Point(10, 0), new Point(10, 10) };
        Assert.Equal(new[] { new Point(0, 0), new Point(5, 0) }, EventMapTrace.Trim(line, 0.25));
        Assert.Equal(new[] { new Point(0, 0), new Point(10, 0), new Point(10, 5) }, EventMapTrace.Trim(line, 0.75));
        Assert.Same(line, EventMapTrace.Trim(line, 1));
    }

    [Fact]
    public void ExportKeepsTheCurrentLookWhenItFitsAndStepsDownOtherwise()
    {
        var small = Layout(Source("No match", Row(SeriesA, "1", 1), Row(SeriesB, "1", 2)));
        Assert.Equal(1.5, EventMapExportScale.Choose(small, 1.5, 180, 200));

        // 400 cards: Covers would need every cover decoded, so a Covers export steps down to Standard or smaller.
        var many = EventMapLayout.Compute(Source("No match", Enumerable.Range(0, 400).Select(i => Row(10 + (i % 4), $"{i}", i + 1)).ToArray()),
            SpineChoice.Relay, EventMapFilter.All, EventMapDensity.Standard, relayPacked: false);
        double? scale = EventMapExportScale.Choose(many, 1.5, 180, 200);
        Assert.NotNull(scale);
        Assert.True(EventMapCardLooks.For(EventMapMetrics.ForScale(scale!.Value).CardWidth) < EventMapCardLook.Covers);
        var size = many.TotalSizeFor(EventMapMetrics.ForScale(scale.Value));
        Assert.True(size.Width + 180 <= EventMapExportScale.MaxPixels);

        // 3,000 cards one per column: even Dots is wider than 16,000 px, so the export is refused.
        var huge = EventMapLayout.Compute(Source("No match", Enumerable.Range(0, 3000).Select(i => Row(10 + (i % 4), $"{i}", i + 1)).ToArray()),
            SpineChoice.Relay, EventMapFilter.All, EventMapDensity.Standard, relayPacked: false);
        Assert.Null(EventMapExportScale.Choose(huge, 1, 180, 200));
    }

    // ===================== Canvas =====================

    [Fact]
    public void TraceRunsOnlyWhenMotionIsAllowed()
    {
        var previous = MotionPolicy.Preference;
        try
        {
            foreach (var (mode, expectTrace) in new[] { (MotionPreferenceMode.Off, false), (MotionPreferenceMode.Full, true) })
            {
                MotionPolicy.SetPreference(mode);
                var (canvas, layout, window) = Host(Source("No match", Row(SeriesA, "1", 1), Row(SeriesA, "2", 2), Row(SeriesB, "1", 3)));
                canvas.SelectedIndex = 1;
                canvas.RelatedSet = layout.RelatedSet(1);
                Assert.Equal(expectTrace, canvas.IsTracing);
                window.Close();
            }
        }
        finally
        {
            MotionPolicy.SetPreference(previous);
        }
    }

    [Fact]
    public void YouAreHereIsStillWithoutContinuousMotionAndNamedForScreenReaders()
    {
        var previous = MotionPolicy.Preference;
        MotionPolicy.SetPreference(MotionPreferenceMode.Off);
        try
        {
            var (canvas, _, window) = Host(Source("No match", Row(SeriesA, "1", 1, read: EventMapReadState.Read), Row(SeriesB, "1", 2)));
            Render(window);
            Assert.Equal(1, canvas.NextUpIndex);
            Assert.False(canvas.IsPulseRunning);

            var peer = ControlAutomationPeer.CreatePeerForElement(canvas);
            Assert.Contains(peer.GetChildren(), p => p.GetName()!.EndsWith(", next to read", StringComparison.Ordinal));
            window.Close();
        }
        finally
        {
            MotionPolicy.SetPreference(previous);
        }
    }

    [Fact]
    public void BandArtLoadsForWideEventBandsOnly()
    {
        var previousLoader = EventMapCanvas.BandArtLoader;
        var asked = new List<int>();
        EventMapCanvas.BandArtLoader = id =>
        {
            lock (asked) asked.Add(id);
            // A decoded bitmap, as StoryEventCoverCache returns (it decodes the cached cover file).
            using var source = new RenderTargetBitmap(new PixelSize(200, 300));
            using var stream = new MemoryStream();
            source.Save(stream);
            stream.Position = 0;
            return new Bitmap(stream);
        };
        try
        {
            Theming.FoundationStyleTests.WithFoundation("default", () =>
            {
                var map = ContinuityMapBuilder.Build(ContinuityMapTests.Sample(), ContinuityMapOptions.Default);
                var layout = EventMapLayout.ComputeBlocks(map, EventMapDensity.Standard);
                var canvas = new EventMapCanvas
                {
                    Layout = layout,
                    Cards = layout.Cells.Select(c => new EventMapCardViewModel(c, layout.Tracks[c.Track])).ToList(),
                    RulerHeight = 50,
                };
                canvas.UseSynchronousCovers();              // band art loads inline too, so one render is enough
                var window = new Window { Width = 1400, Height = 400, Content = canvas };
                window.Show();
                window.UpdateLayout();
                Render(window);
                Assert.True(canvas.BandArtCount >= 1, $"asked {asked.Count}, blocks {string.Join(",", layout.Blocks.Select(b => b.Kind + ":" + b.EventId + ":" + layout.BlockColumns(b)))}, scale {canvas.Scale}");
                Assert.All(asked, id => Assert.Contains(layout.Blocks, b => b.EventId == id && b.Kind == EventMapBlockKind.Event));
                window.Close();
            });
        }
        finally
        {
            EventMapCanvas.BandArtLoader = previousLoader;
        }
    }

    [Fact]
    public void ProgressLineCanBeTurnedOff()
    {
        var vm = new EventMapViewModel(load: _ => Source("No match", Row(SeriesA, "1", 1)), saveSpine: (_, _) => { },
            setRead: (_, read) => read ? EventMapReadState.Read : EventMapReadState.Unread, openReader: (_, _) => { },
            activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }), runInBackground: work => Task.FromResult(work()));
        Assert.True(vm.ShowProgressLine);
        Assert.Equal("Hide progress line", vm.ProgressLineLabel);
        vm.ToggleProgressLineCommand.Execute(null);
        Assert.False(vm.ShowProgressLine);
        Assert.Equal("Show progress line", vm.ProgressLineLabel);
    }

    // ===================== Export helper =====================

    [Fact]
    public void ExporterWritesAPngOfTheRequestedSizeAndAlwaysRemovesTheControl()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            var host = new Panel();
            var window = new Window { Width = 200, Height = 200, Content = host };
            window.Show();
            string path = Path.Combine(Path.GetTempPath(), $"pb-export-{Guid.NewGuid():N}.png");
            try
            {
                var content = new Border { Background = Avalonia.Media.Brushes.Red };
                ControlImageExporter.RenderToPng(host, content, new Size(320, 180), path);
                Assert.Empty(host.Children);
                using (var saved = new Bitmap(path))
                {
                    Assert.Equal(new PixelSize(320, 180), saved.PixelSize);
                }

                Assert.Throws<ArgumentOutOfRangeException>(() => ControlImageExporter.RenderToPng(host, new Border(), new Size(20000, 10), path));
                Assert.Throws<InvalidOperationException>(() => ControlImageExporter.RenderToPng(host, new ThrowingControl(), new Size(50, 50), path));
                Assert.Empty(host.Children);
                Assert.False(File.Exists(path), "a failed render leaves no file behind");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                window.Close();
            }
        });
    }

    private sealed class ThrowingControl : Control
    {
        protected override Size MeasureOverride(Size availableSize) => throw new InvalidOperationException("boom");
    }

    private static (EventMapCanvas Canvas, EventMapLayoutResult Layout, Window Window) Host(EventMapSource source)
    {
        var layout = Layout(source);
        var canvas = new EventMapCanvas { Layout = layout, Cards = layout.Cells.Select(c => new EventMapCardViewModel(c, layout.Tracks[c.Track])).ToList() };
        var window = new Window { Width = 900, Height = 300, Content = canvas };
        window.Show();
        window.UpdateLayout();
        TestDispatcher.Drain();
        return (canvas, layout, window);
    }

    private static void Render(Window window)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window);
        TestDispatcher.Drain();
    }
}
