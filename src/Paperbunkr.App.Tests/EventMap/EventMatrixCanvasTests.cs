using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Controls.EventMap;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventMatrixCanvasTests
{
    [Fact]
    public void VisibleCellPeersDescribeCountsAndExposeExpansionCards()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            var layout = Layout(Source("No match", Row(SeriesA, "1", 1, read: EventMapReadState.Read), Row(SeriesB, "2", 2)));
            var cards = layout.Cells.Select(c => new EventMapCardViewModel(c, layout.Tracks[c.Track])).ToArray();
            var canvas = new EventMatrixCanvas { Matrix = EventMatrixBuilder.Build(layout), Cards = cards };
            var window = new Window { Width = 600, Height = 300, Content = canvas };
            window.Show(); window.UpdateLayout(); TestDispatcher.Drain();
            using var bitmap = new RenderTargetBitmap(new PixelSize(600, 300));
            bitmap.Render(window);
            var peer = ControlAutomationPeer.CreatePeerForElement(canvas);
            var cell = peer.GetChildren().First();
            Assert.Contains("Adventure, 1–8: 1 issues, 1 read", cell.GetName());
            cell.GetProvider<IInvokeProvider>()!.Invoke();
            Assert.True(canvas.HasExpansion);
            Assert.Contains(cell.GetChildren(), p => p.GetName() == cards[0].AutomationName);
            Assert.True(canvas.CloseExpansion());
            canvas.Covers = true;
            Assert.Equal(64, canvas.RowHeight);
            window.Close();
        });
    }
}
