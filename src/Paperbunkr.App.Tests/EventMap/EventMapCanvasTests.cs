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
public class EventMapCanvasTests
{
    [Fact]
    public void FloatingInspectorCanRevealACardEvenWhenTheMapOtherwiseFits()
    {
        var previous = Paperbunkr.App.Theming.Motion.MotionPolicy.Preference;
        Paperbunkr.App.Theming.Motion.MotionPolicy.SetPreference(Paperbunkr.Data.Entities.MotionPreferenceMode.Off);
        try
        {
            var layout = Layout(Source("Event", Row(SeriesA, "1", 1)));
            var canvas = new EventMapCanvas { Layout = layout, ObscuredRight = 364 };
            var window = new Window { Width = 500, Height = 240, Content = canvas };
            window.Show(); window.UpdateLayout(); TestDispatcher.Drain();
            canvas.Reveal(0, 364);
            Assert.True(canvas.Offset.X > 0);
            Assert.True(layout.CardRect(0, canvas.Metrics).Right - canvas.Offset.X <= canvas.ContentViewport.Width - 364);
            window.Close();
        }
        finally { Paperbunkr.App.Theming.Motion.MotionPolicy.SetPreference(previous); }
    }

    [Fact]
    public void OverviewAppearsOnlyWhenTheMapDoesNotFit()
    {
        var previous = Paperbunkr.App.Theming.Motion.MotionPolicy.Preference;
        Paperbunkr.App.Theming.Motion.MotionPolicy.SetPreference(Paperbunkr.Data.Entities.MotionPreferenceMode.Off);
        try
        {
        var layout = Layout(Source("Event", Row(SeriesA, "1", 1)));
        var canvas = new EventMapCanvas { Layout = layout, Cards = layout.Cells.Select(c => new EventMapCardViewModel(c, layout.Tracks[c.Track])).ToList() };
        var strip = new EventMapOverviewStrip { Canvas = canvas };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(strip);
        Grid.SetRow(canvas, 1);
        grid.Children.Add(canvas);
        var window = new Window { Width = 500, Height = 240, Content = grid };
        window.Show(); window.UpdateLayout(); TestDispatcher.Drain(); window.UpdateLayout();
        Assert.False(strip.IsVisible);
        canvas.Scale = 2;
        window.UpdateLayout(); TestDispatcher.Drain(); window.UpdateLayout();
        Assert.True(strip.IsVisible);
        window.Close();
        }
        finally { Paperbunkr.App.Theming.Motion.MotionPolicy.SetPreference(previous); }
    }

    [Fact]
    public void DrawsOnlyVisibleCellsAndChangesDetailWithZoom()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            var rows = Enumerable.Range(0, 80).Select(i => Row(SeriesA, (i + 1).ToString(), i)).ToArray();
            var layout = Layout(Source("Event", rows));
            var cards = layout.Cells.Select(c => new EventMapCardViewModel(c, layout.Tracks[c.Track])).ToList();
            var canvas = new EventMapCanvas { Layout = layout, Cards = cards, Scale = 0.1, SelectedIndex = 0 };
            var window = new Window { Width = 500, Height = 240, Content = canvas };
            window.Show();
            window.UpdateLayout();
            TestDispatcher.Drain();
            using var bitmap = new RenderTargetBitmap(new PixelSize(500, 240));
            bitmap.Render(window);
            Assert.InRange(canvas.LastFrame.Cells, 1, cards.Count - 1);
            Assert.InRange(canvas.LastFrame.Edges, 0, 2);
            Assert.Equal(0, canvas.LastFrame.ColumnLabels);
            canvas.Scale = 0.6;
            bitmap.Render(window);
            Assert.True(canvas.LastFrame.ColumnLabels > 0);
            window.Close();
        });
    }

    [Fact]
    public void AutomationExposesVisibleCardsAndSelectsThroughTheCanvas()
    {
        var rows = Enumerable.Range(0, 30).Select(i => Row(SeriesA, (i + 1).ToString(), i)).ToArray();
        var layout = Layout(Source("Event", rows));
        var cards = layout.Cells.Select(c => new EventMapCardViewModel(c, layout.Tracks[c.Track])).ToList();
        var canvas = new EventMapCanvas { Layout = layout, Cards = cards };
        var window = new Window { Width = 500, Height = 240, Content = canvas };
        window.Show();
        window.UpdateLayout();
        TestDispatcher.Drain();
        var peer = ControlAutomationPeer.CreatePeerForElement(canvas);
        var children = peer.GetChildren();
        Assert.NotEmpty(children);
        Assert.True(children.Count < cards.Count);
        // The first card is also the next issue to read, which the name says (M6b §1).
        Assert.Equal(cards[0].AutomationName + ", next to read", children[0].GetName());
        Assert.Equal(cards[1].AutomationName, children[1].GetName());
        int selected = -1;
        canvas.CardPressed += (index, _) => selected = index;
        children[0].GetProvider<ISelectionItemProvider>()!.Select();
        Assert.Equal(0, selected);
        bool changed = false;
        peer.ChildrenChanged += (_, _) => changed = true;
        canvas.Offset = new Vector(340, 0);
        Assert.True(changed);
        window.Close();
    }
}
