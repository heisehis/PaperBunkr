using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Controls;
using Paperbunkr.App.ViewModels.EntityPages;
using Paperbunkr.App.Views.EntityPages;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.EntityPages;

/// <summary>Shared entity page controls, docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md §5.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class EntityPageControlsTests
{
    [Fact]
    public void Tiles_AreTheSameHeight_ForOneAndTwoLineNames()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            var shortName = new EntityTile { DataContext = new EntityTileItem(ComicMetadataEntityKind.Character, 1, "Storm", "32 appearances", null, null, "S") };
            var longName = new EntityTile { DataContext = new EntityTileItem(ComicMetadataEntityKind.Character, 2, "Captain America of Earth 616", "23 appearances", null, null, "CA") };
            var strip = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { shortName, longName } };
            var window = new Window { Width = 400, Height = 300, Content = strip }; window.Show(); RunLayout(window);

            Assert.True(shortName.Bounds.Height > 100);
            Assert.Equal(shortName.Bounds.Height, longName.Bounds.Height, 1);
            window.Close();
        });
    }

    [Fact]
    public void Tile_ShowsTheMonogramAndInset_OnlyWithoutAPortrait_AndSquircleIsSquare()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            var tile = new EntityTile { Shape = EntityTileShape.Squircle, DataContext = new EntityTileItem(ComicMetadataEntityKind.Creator, 1, "Sara Pichelli", "48 issues", null, "7", "SP") };
            var window = new Window { Width = 200, Height = 300, Content = tile }; window.Show(); RunLayout(window);

            var monogram = tile.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("entityMonogram"));
            Assert.True(monogram.IsEffectivelyVisible);
            Assert.True(tile.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("entityInset")).IsEffectivelyVisible);
            var art = tile.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("entityArt"));
            Assert.Equal(art.Bounds.Width, art.Bounds.Height, 1);
            window.Close();
        });
    }

    [Fact]
    public void Tile_RunsItsCommandWithItsItem()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            object? opened = null;
            var item = new EntityTileItem(ComicMetadataEntityKind.Team, 3, "X-Men", "21", null, null, "XM");
            var tile = new EntityTile { DataContext = item, Command = new RelayCommand<object?>(p => opened = p) };
            var window = new Window { Width = 200, Height = 300, Content = tile }; window.Show(); RunLayout(window);
            tile.GetVisualDescendants().OfType<Button>().First().Command!.Execute(item);
            Assert.Same(item, opened);
            window.Close();
        });
    }

    [Fact]
    public void YearChart_GivesTheBusiestYearFullHeight_AndNotesUndated()
    {
        var chart = YearChartModel.From(new[] { new EntityPageYear(2009, 2), new EntityPageYear(2012, 4), new EntityPageYear(null, 3) });
        Assert.Equal(new[] { "2009", "2012" }, chart.Columns.Select(c => c.Label));
        Assert.Equal(YearChartModel.Height, chart.Columns[1].BarHeight);
        Assert.Equal(YearChartModel.Height / 2, chart.Columns[0].BarHeight);
        Assert.Equal("2009", chart.FirstLabel);
        Assert.Equal("2012", chart.LastLabel);
        Assert.Equal("3 undated", chart.Note);
        Assert.False(YearChartModel.From(Array.Empty<EntityPageYear>()).HasColumns);
    }

    [Theory]
    [InlineData(0.25, 0.5, 400, 100, 100)]
    [InlineData(0.5, 0.5, 400, 200, 8)]
    [InlineData(0.99, 1.0, 400, 392, 8)]
    public void FractionPanel_PlacesASpanAlongTheTrack(double start, double end, double total, double x, double width)
    {
        var (gotX, gotWidth) = FractionPanel.Span(start, end, total, 8);
        Assert.Equal(x, gotX, 1);
        Assert.Equal(width, gotWidth, 1);
    }

    [Fact]
    public void PosterGrid_RealizesOnlyWhatIsOnScreen_InsideAPageScroller()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            var items = Enumerable.Range(1, 1000).Select(i => new EntityPosterItem(i, $"Issue #{i}", "2009 · Unread", null, null, new RelayCommand(() => { }))).ToList();
            var grid = new EntityPosterGrid { Items = items };
            var scroller = new ScrollViewer { Content = new StackPanel { Children = { new Border { Height = 300 }, grid } } };
            var window = new Window { Width = 1000, Height = 768, Content = scroller }; window.Show(); RunLayout(window);

            int realized = grid.GetVisualDescendants().OfType<Button>().Count();
            Assert.InRange(realized, 1, 99);
            window.Close();
        });
    }
}
