using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Up and Down move through the rows of the Wanted queue and the Series tab.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class WantedArrowKeyTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 9, 19);
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_wanted_arrows_{Guid.NewGuid():N}.db");

    public WantedArrowKeyTests()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath};Foreign Keys=True").Options);

    private WantedScreenViewModel CreateVm() => new(
        NewContext, _ => Task.CompletedTask, _ => { }, () => { }, _ => Task.CompletedTask,
        new GrabService(NewContext, _ => null, new ChannelEventPublisher()),
        post: a => a(), today: () => Today, notify: (_, _) => { });

    private void Seed(int volumes)
    {
        using var context = NewContext();
        for (int v = 1; v <= volumes; v++)
        {
            var series = new Series { Name = $"Series {v}" };
            context.Series.Add(series);
            context.SaveChanges();
            var watched = WantedService.TrackVolume(context, new ComicVineVolume(100 + v, $"Series {v}", "Image", 1992, 300, null), series.Id, watchFutureReleases: false);
            WantedService.RefreshCatalog(context, watched, new[]
            {
                new ComicVineIssue((v * 10) + 1, "1", null, Today.AddDays(-7), null, null, 100 + v),
                new ComicVineIssue((v * 10) + 2, "2", null, Today.AddDays(-1), null, null, 100 + v),
            });
            WantedService.Request(context, watched, context.CatalogIssues.Single(c => c.ExternalIssueId == (v * 10) + 1));
            WantedService.Request(context, watched, context.CatalogIssues.Single(c => c.ExternalIssueId == (v * 10) + 2));
        }

        context.SaveChanges();
    }

    private static string Describe(object? element) =>
        element is Control c ? $"{c.GetType().Name}[{string.Join(",", c.Classes.Where(k => !k.StartsWith(':')))}]@{(c.GetVisualAncestors().LastOrDefault() is Visual top ? c.TranslatePoint(default, top)?.Y : null):0}" : "(none)";

    [Theory]
    [InlineData("queue")]
    [InlineData("series")]
    public void UpAndDown_MoveFocusThroughTheRows(string tab)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            Seed(4);
            var vm = CreateVm();
            vm.Refresh();
            if (tab == "series")
            {
                vm.GoSeriesCommand.Execute(null);
            }

            var screen = new WantedScreen { DataContext = vm };
            var input = ReaderTestInput.Create();
            screen.InputService = input;
            var window = new Window { Content = screen, Width = 1200, Height = 900 };
            InputHost.Attach(window, input);
            window.Show();
            RunLayout(window);

            // Start from a control in the list body, not the tab header: the first button that is not a tab or a chip.
            var rows = screen.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled && b.Focusable && !b.Classes.Contains("tab") && !b.Classes.Contains("pbChip") && b.TranslatePoint(default, screen)?.Y > 120)
                .OrderBy(b => b.TranslatePoint(default, screen)!.Value.Y)
                .ToList();
            Assert.True(rows.Count >= 3, $"row buttons: {rows.Count}");
            var first = rows[0];
            first.Focus(NavigationMethod.Tab);
            RunLayout(window);
            var trail = new List<string> { Describe(window.FocusManager!.GetFocusedElement()) };

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            RunLayout(window);
            var afterDown = window.FocusManager!.GetFocusedElement();
            trail.Add(Describe(afterDown));

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            RunLayout(window);
            var afterUp = window.FocusManager!.GetFocusedElement();
            trail.Add(Describe(afterUp));

            string text = string.Join(" -> ", trail);
            Assert.False(ReferenceEquals(afterDown, first), "Down moved: " + text);
            Assert.True(ReferenceEquals(afterUp, first), "Up came back: " + text);
            window.Close();
        });
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("series")]
    public void Down_KeepsGoingPastTheRowsThatFitOnScreen(string tab)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            Seed(40);
            var vm = CreateVm();
            vm.Refresh();
            if (tab == "series")
            {
                vm.GoSeriesCommand.Execute(null);
            }

            var screen = new WantedScreen { DataContext = vm };
            var input = ReaderTestInput.Create();
            screen.InputService = input;
            var window = new Window { Content = screen, Width = 1200, Height = 700 };
            InputHost.Attach(window, input);
            window.Show();
            RunLayout(window);

            var first = screen.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled && b.Focusable && !b.Classes.Contains("tab") && !b.Classes.Contains("pbChip") && b.TranslatePoint(default, screen)?.Y > 120)
                .OrderBy(b => b.TranslatePoint(default, screen)!.Value.Y).First();
            first.Focus(NavigationMethod.Tab);
            RunLayout(window);

            var visited = new HashSet<object>();
            for (int i = 0; i < 40; i++)
            {
                if (window.FocusManager!.GetFocusedElement() is { } f)
                {
                    visited.Add(f);
                }

                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                RunLayout(window);
                TestDispatcher.Drain();
            }

            // The list recycles its row containers, so counting distinct controls says nothing; how far the list has scrolled does.
            var sv = screen.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.IsEffectivelyVisible && v.Extent.Height > v.Viewport.Height * 2);
            Assert.True(sv.Offset.Y > sv.Viewport.Height * 2, $"after 40 Down presses the list is scrolled to {sv.Offset.Y:0} of {sv.Extent.Height:0} (viewport {sv.Viewport.Height:0}); focus on {Describe(window.FocusManager!.GetFocusedElement())}");
            window.Close();
        });
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("series")]
    public void FromTheTabHeader_DownEntersTheRows_AndUpComesBack(string tab)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            Seed(6);
            var vm = CreateVm();
            vm.Refresh();
            if (tab == "series")
            {
                vm.GoSeriesCommand.Execute(null);
            }

            var screen = new WantedScreen { DataContext = vm };
            var input = ReaderTestInput.Create();
            screen.InputService = input;
            var window = new Window { Content = screen, Width = 1200, Height = 800 };
            InputHost.Attach(window, input);
            window.Show();
            RunLayout(window);

            var activeTab = screen.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("tab") && b.Classes.Contains("active"));
            bool took = activeTab.Focus(NavigationMethod.Tab);
            RunLayout(window);
            Assert.True(ReferenceEquals(activeTab, window.FocusManager!.GetFocusedElement()), $"Focus() returned {took}; enabled={activeTab.IsEffectivelyEnabled} visible={activeTab.IsEffectivelyVisible} focusable={activeTab.Focusable} bounds={activeTab.Bounds}; focus is on {Describe(window.FocusManager!.GetFocusedElement())}");

            var trail = new List<string> { Describe(activeTab) };
            for (int i = 0; i < 4; i++)
            {
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                RunLayout(window);
                trail.Add(Describe(window.FocusManager!.GetFocusedElement()));
            }

            Assert.True(trail.Distinct().Count() >= 4, "Down from the tab walks down the screen: " + string.Join(" -> ", trail));
            for (int i = 0; i < 4; i++)
            {
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                RunLayout(window);
            }

            // Up moves back up the screen (it need not retrace the same controls).
            var top = window.FocusManager!.GetFocusedElement() as Control;
            Assert.NotNull(top);
            Assert.True(top!.TranslatePoint(default, screen)!.Value.Y <= activeTab.TranslatePoint(default, screen)!.Value.Y + 120, "Up climbed back toward the tabs");
            window.Close();
        });
    }

    private sealed class Region : UserControl
    {
        public Region() => KeyDown += (_, e) => e.Handled = Paperbunkr.App.Views.FocusReclaimer.TryMoveDirectionally(this, e);
    }

    [Fact]
    public void UpAndDown_FindTheRowAboveOrBelow_EvenWhenItsButtonsAreInOtherColumns()
    {
        WithThemeAndTokens(() =>
        {
            // Row 1 has a button only at the far right; row 2 has one only at the far left, so nothing overlaps horizontally.
            var region = new Region();
            var stack = new StackPanel { Spacing = 30 };
            var rightOnly = new Button { Content = "right", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Width = 100 };
            var leftOnly = new Button { Content = "left", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Width = 100 };
            var rightAgain = new Button { Content = "right 2", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Width = 100 };
            stack.Children.Add(rightOnly);
            stack.Children.Add(leftOnly);
            stack.Children.Add(rightAgain);
            region.Content = stack;
            var window = new Window { Content = region, Width = 800, Height = 400 };
            window.Show();
            RunLayout(window);

            rightOnly.Focus(NavigationMethod.Tab);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            RunLayout(window);
            Assert.True(ReferenceEquals(leftOnly, window.FocusManager!.GetFocusedElement()), "after first Down: " + ((window.FocusManager!.GetFocusedElement() as Button)?.Content ?? "(none)"));

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            RunLayout(window);
            Assert.Same(rightAgain, window.FocusManager!.GetFocusedElement());

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            RunLayout(window);
            Assert.Same(leftOnly, window.FocusManager!.GetFocusedElement());
            window.Close();
        });
    }
}
