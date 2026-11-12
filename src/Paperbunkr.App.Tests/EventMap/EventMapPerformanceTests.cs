using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Controls.EventMap;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;
using Xunit.Abstractions;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventMapPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void DotsFrameReportsTimingForATwoThousandIssueMap()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            var rows = new List<EventMapRow> { Row(Crisis, "0", 0) };
            rows.AddRange(Enumerable.Range(0, 2100).Select(i => Row(10 + (i % 20), (i + 1).ToString(), i + 1)));
            var layout = Layout(Source("Crisis", rows.ToArray()));
            var cards = layout.Cells.Select(c => new EventMapCardViewModel(c, layout.Tracks[c.Track])).ToList();
            var canvas = new EventMapCanvas { Layout = layout, Cards = cards, Scale = 0.06 };
            var window = new Window { Width = 1366, Height = 768, Content = canvas };
            window.Show(); window.UpdateLayout(); TestDispatcher.Drain();
            using var bitmap = new RenderTargetBitmap(new PixelSize(1366, 768));
            bitmap.Render(window);
            Assert.True(canvas.LastFrame.Cells >= 2000, $"Only {canvas.LastFrame.Cells} cards were visible");
            var elapsed = new List<double>();
            for (int i = 0; i < 10; i++)
            {
                var watch = Stopwatch.StartNew();
                bitmap.Render(window);
                watch.Stop();
                elapsed.Add(watch.Elapsed.TotalMilliseconds);
            }
            elapsed.Sort();
            output.WriteLine($"2,101-card Dots frame median: {elapsed[elapsed.Count / 2]:F2} ms (headless bitmap render, Debug)");
            window.Close();
        });
    }
}
