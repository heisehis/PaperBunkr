using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Views;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Keyboard reach for the Home shelf tile (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-detail-home-smart-design.md):
/// Enter/Space open it, Delete dismisses, Left/Right/Home/End walk the shelf.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PosterTileKeyboardTests
{
    private static (Window Window, ItemsControl Shelf, List<object?> Opened, List<object?> Dismissed) Show(string[] items)
    {
        var opened = new List<object?>();
        var dismissed = new List<object?>();
        var open = new RelayCommand<object?>(opened.Add);
        var dismiss = new RelayCommand<object?>(dismissed.Add);
        var shelf = new ItemsControl
        {
            ItemsSource = items,
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }),
            ItemTemplate = new FuncDataTemplate<string>((item, _) => new PosterTile
            {
                Width = 120,
                Height = 200,
                TitleText = item,
                Command = open,
                CommandParameter = item,
                DismissCommand = dismiss,
            }),
        };
        var window = new Window { Content = shelf, Width = 800, Height = 400 };
        window.Show();
        RunLayout(window);
        return (window, shelf, opened, dismissed);
    }

    private static PosterTile Tile(ItemsControl shelf, int index) =>
        Assert.IsType<PosterTile>(shelf.ItemsPanelRoot!.Children[index] is ContentPresenter cp ? cp.Child : shelf.ItemsPanelRoot.Children[index]);

    [Fact]
    public void EnterAndSpace_OpenTheFocusedTile()
    {
        WithThemeAndTokens(() =>
        {
            var (window, shelf, opened, _) = Show(new[] { "A", "B", "C" });
            Tile(shelf, 1).Root.Focus();
            RunLayout(window);

            Press(window, Key.Enter);
            Press(window, Key.Space);

            Assert.Equal(new object?[] { "B", "B" }, opened);
            window.Close();
        });
    }

    [Fact]
    public void LeftRight_WalkALeadCardThatSitsOutsideTheRowsItemList()
    {
        WithThemeAndTokens(() =>
        {
            var lead = new PosterTile { Width = 120, Height = 200, TitleText = "lead" };
            var cards = new ItemsControl
            {
                ItemsSource = new[] { "A", "B" },
                ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }),
                ItemTemplate = new FuncDataTemplate<string>((item, _) => new PosterTile { Width = 120, Height = 200, TitleText = item }),
            };
            var below = new ItemsControl
            {
                ItemsSource = new[] { "X" },
                ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }),
                ItemTemplate = new FuncDataTemplate<string>((item, _) => new PosterTile { Width = 120, Height = 200, TitleText = item }),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { lead, cards } };
            var window = new Window
            {
                Width = 900,
                Height = 700,
                Content = new StackPanel
                {
                    Children = { new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = row }, below },
                },
            };
            window.Show();
            RunLayout(window);

            lead.Root.Focus();
            RunLayout(window);

            Press(window, Key.Right);
            Assert.Same(Tile(cards, 0).Root, Focused(window));

            Press(window, Key.Right);
            Press(window, Key.Right);
            Assert.Same(Tile(cards, 1).Root, Focused(window));

            Press(window, Key.Home);
            Assert.Same(lead.Root, Focused(window));

            Press(window, Key.End);
            Assert.Same(Tile(cards, 1).Root, Focused(window));

            Press(window, Key.Left);
            Press(window, Key.Left);
            Assert.Same(lead.Root, Focused(window));
            window.Close();
        });
    }

    [Fact]
    public void UpDown_StayOnTheScreen_AndReachTilesBelowTheVisibleArea()
    {
        WithThemeAndTokens(() =>
        {
            var decoy = new Button { Content = "outside the screen", Width = 200, Height = 30 };
            PosterTile MakeTile(string title) => new() { Width = 120, Height = 200, TitleText = title };
            var first = MakeTile("first");
            var second = MakeTile("second");
            var page = new ScrollViewer
            {
                Height = 260,
                Content = new StackPanel { Children = { first, new Border { Height = 400 }, second } },
            };
            var window = new Window { Width = 600, Height = 700, Content = new StackPanel { Children = { decoy, page } } };
            window.Show();
            RunLayout(window);

            first.Root.Focus();
            RunLayout(window);

            Press(window, Key.Up);
            Assert.Same(first.Root, Focused(window));

            Press(window, Key.Down);
            Assert.Same(second.Root, Focused(window));
            window.Close();
        });
    }

    [Fact]
    public void UpDown_MoveBetweenShelves()
    {
        WithThemeAndTokens(() =>
        {
            var (window, upper, _, _) = Show(new[] { "A", "B" });
            var lower = new ItemsControl
            {
                ItemsSource = new[] { "C", "D" },
                ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }),
                ItemTemplate = new FuncDataTemplate<string>((item, _) => new PosterTile { Width = 120, Height = 200, TitleText = item }),
            };
            var stack = new StackPanel();
            window.Content = null;
            stack.Children.Add(upper);
            stack.Children.Add(lower);
            window.Content = stack;
            RunLayout(window);

            Tile(upper, 0).Root.Focus();
            RunLayout(window);

            Press(window, Key.Down);
            Assert.Same(Tile(lower, 0).Root, Focused(window));

            Press(window, Key.Up);
            Assert.Same(Tile(upper, 0).Root, Focused(window));
            window.Close();
        });
    }

    [Fact]
    public void Delete_RunsTheDismissCommand()
    {
        WithThemeAndTokens(() =>
        {
            var (window, shelf, _, dismissed) = Show(new[] { "A", "B" });
            Tile(shelf, 0).Root.Focus();
            RunLayout(window);

            Press(window, Key.Delete);
            RunLayout(window);

            Assert.Equal(new object?[] { "A" }, dismissed);
            Assert.Same(Tile(shelf, 1).Root, Focused(window));
            window.Close();
        });
    }

    [Fact]
    public void LeftRightHomeEnd_WalkTheShelf()
    {
        WithThemeAndTokens(() =>
        {
            var (window, shelf, _, _) = Show(new[] { "A", "B", "C" });
            Tile(shelf, 0).Root.Focus();
            RunLayout(window);

            Press(window, Key.Right);
            Assert.Same(Tile(shelf, 1).Root, Focused(window));

            Press(window, Key.End);
            Assert.Same(Tile(shelf, 2).Root, Focused(window));

            Press(window, Key.Right);
            Assert.Same(Tile(shelf, 2).Root, Focused(window));

            Press(window, Key.Left);
            Assert.Same(Tile(shelf, 1).Root, Focused(window));

            Press(window, Key.Home);
            Assert.Same(Tile(shelf, 0).Root, Focused(window));
            window.Close();
        });
    }
}
