using Avalonia;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="GridKeyboardNavigation.Navigate{T}"/>'s pure spatial-movement core
/// (docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-design.md). Plain marker
/// objects stand in for grid items - not records/strings, so identity stays reference-based like
/// the real <c>SeriesCardSample</c>/<c>IssueCardSample</c> types this backs. No Avalonia app
/// context needed - <see cref="Rect"/> is a plain value type.
/// </summary>
public class GridKeyboardNavigationTests
{
    private sealed class Item
    {
        public string Name { get; init; } = "";
    }

    private static GridKeyboardNavigation.GridItem<Item> Tile(string name, double x, double y) =>
        new(new Item { Name = name }, new Rect(x, y, 150, 216));

    [Fact]
    public void Right_MovesToNextItemInListOrder()
    {
        var a = Tile("A", 0, 0);
        var b = Tile("B", 170, 0);
        var items = new[] { a, b };

        var result = GridKeyboardNavigation.Navigate(items, a.Item, GridNavigationDirection.Right);

        Assert.Same(b.Item, result);
    }

    [Fact]
    public void Right_AtLastItem_ClampsNoOp()
    {
        var a = Tile("A", 0, 0);
        var b = Tile("B", 170, 0);
        var items = new[] { a, b };

        var result = GridKeyboardNavigation.Navigate(items, b.Item, GridNavigationDirection.Right);

        Assert.Same(b.Item, result);
    }

    [Fact]
    public void Left_MovesToPreviousItemInListOrder()
    {
        var a = Tile("A", 0, 0);
        var b = Tile("B", 170, 0);
        var items = new[] { a, b };

        var result = GridKeyboardNavigation.Navigate(items, b.Item, GridNavigationDirection.Left);

        Assert.Same(a.Item, result);
    }

    [Fact]
    public void Left_AtFirstItem_ClampsNoOp()
    {
        var a = Tile("A", 0, 0);
        var b = Tile("B", 170, 0);
        var items = new[] { a, b };

        var result = GridKeyboardNavigation.Navigate(items, a.Item, GridNavigationDirection.Left);

        Assert.Same(a.Item, result);
    }

    [Fact]
    public void Home_JumpsToFirstItem()
    {
        var a = Tile("A", 0, 0);
        var b = Tile("B", 170, 0);
        var c = Tile("C", 340, 0);
        var items = new[] { a, b, c };

        var result = GridKeyboardNavigation.Navigate(items, c.Item, GridNavigationDirection.Home);

        Assert.Same(a.Item, result);
    }

    [Fact]
    public void End_JumpsToLastItem()
    {
        var a = Tile("A", 0, 0);
        var b = Tile("B", 170, 0);
        var c = Tile("C", 340, 0);
        var items = new[] { a, b, c };

        var result = GridKeyboardNavigation.Navigate(items, a.Item, GridNavigationDirection.End);

        Assert.Same(c.Item, result);
    }

    [Fact]
    public void Down_MovesToNearestXInNextRow()
    {
        var row0 = new[] { Tile("A0", 0, 0), Tile("A1", 170, 0), Tile("A2", 340, 0) };
        var row1 = new[] { Tile("B0", 0, 250), Tile("B1", 170, 250), Tile("B2", 340, 250) };
        var items = row0.Concat(row1).ToArray();

        var result = GridKeyboardNavigation.Navigate(items, row0[1].Item, GridNavigationDirection.Down);

        Assert.Same(row1[1].Item, result);
    }

    [Fact]
    public void Down_AtLastRow_ClampsNoOp()
    {
        var row0 = new[] { Tile("A0", 0, 0) };
        var row1 = new[] { Tile("B0", 0, 250) };
        var items = row0.Concat(row1).ToArray();

        var result = GridKeyboardNavigation.Navigate(items, row1[0].Item, GridNavigationDirection.Down);

        Assert.Same(row1[0].Item, result);
    }

    [Fact]
    public void Up_MovesToNearestXInPreviousRow()
    {
        var row0 = new[] { Tile("A0", 0, 0), Tile("A1", 170, 0), Tile("A2", 340, 0) };
        var row1 = new[] { Tile("B0", 0, 250), Tile("B1", 170, 250), Tile("B2", 340, 250) };
        var items = row0.Concat(row1).ToArray();

        var result = GridKeyboardNavigation.Navigate(items, row1[2].Item, GridNavigationDirection.Up);

        Assert.Same(row0[2].Item, result);
    }

    [Fact]
    public void Up_AtFirstRow_ClampsNoOp()
    {
        var row0 = new[] { Tile("A0", 0, 0) };
        var row1 = new[] { Tile("B0", 0, 250) };
        var items = row0.Concat(row1).ToArray();

        var result = GridKeyboardNavigation.Navigate(items, row0[0].Item, GridNavigationDirection.Up);

        Assert.Same(row0[0].Item, result);
    }

    [Fact]
    public void Down_RaggedLastRow_PicksOnlyAvailableItem()
    {
        var row0 = new[] { Tile("A0", 0, 0), Tile("A1", 170, 0), Tile("A2", 340, 0) };
        var row1 = new[] { Tile("B0", 0, 250), Tile("B1", 170, 250), Tile("B2", 340, 250) };
        var row2 = new[] { Tile("C0", 0, 500) };
        var items = row0.Concat(row1).Concat(row2).ToArray();

        var result = GridKeyboardNavigation.Navigate(items, row1[2].Item, GridNavigationDirection.Down);

        Assert.Same(row2[0].Item, result);
    }

    [Fact]
    public void Up_FromRaggedLastRow_PicksNearestXInFullRowAbove()
    {
        var row0 = new[] { Tile("A0", 0, 0), Tile("A1", 170, 0), Tile("A2", 340, 0) };
        var row1 = new[] { Tile("B0", 0, 250), Tile("B1", 170, 250), Tile("B2", 340, 250) };
        var row2 = new[] { Tile("C0", 0, 500) };
        var items = row0.Concat(row1).Concat(row2).ToArray();

        var result = GridKeyboardNavigation.Navigate(items, row2[0].Item, GridNavigationDirection.Up);

        Assert.Same(row1[0].Item, result);
    }

    [Fact]
    public void Down_PicksClosestXInRow_NotSameColumnIndex()
    {
        // Row above has 3 items (last at x=340); row below has only 2, deliberately placed so a
        // naive "clamp column index" strategy (2 -> max index 1) would pick the wrong one, while
        // true nearest-by-X picks the right one.
        var rowAbove = new[] { Tile("A0", 0, 0), Tile("A1", 170, 0), Tile("A2", 340, 0) };
        var rowBelow = new[] { Tile("B0", 310, 250), Tile("B1", 50, 250) };
        var items = rowAbove.Concat(rowBelow).ToArray();

        var result = GridKeyboardNavigation.Navigate(items, rowAbove[2].Item, GridNavigationDirection.Down);

        Assert.Same(rowBelow[0].Item, result);
    }

    [Fact]
    public void EmptyItemList_ReturnsCurrentItemUnchanged()
    {
        var current = new Item { Name = "Solo" };
        var items = Array.Empty<GridKeyboardNavigation.GridItem<Item>>();

        var result = GridKeyboardNavigation.Navigate(items, current, GridNavigationDirection.Right);

        Assert.Same(current, result);
    }

    [Fact]
    public void CurrentItemNotInList_ReturnsCurrentItemUnchanged()
    {
        var a = Tile("A", 0, 0);
        var stale = new Item { Name = "Stale" };
        var items = new[] { a };

        var result = GridKeyboardNavigation.Navigate(items, stale, GridNavigationDirection.Right);

        Assert.Same(stale, result);
    }
}
