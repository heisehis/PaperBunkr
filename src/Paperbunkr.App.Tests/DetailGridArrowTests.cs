using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>The Detail screen's issue grid: arrow keys move through it in two dimensions, including up and down between rows.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DetailGridArrowTests : IDisposable
{
    private readonly string? _original;
    private readonly string _db;

    public DetailGridArrowTests()
    {
        _original = PaperbunkrDbContext.DatabasePathOverride;
        _db = Path.Combine(Path.GetTempPath(), $"pb_detailgrid_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _db;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _original;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }

    private int SeedSeries(int issues)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = "Grid Series" };
        context.Series.Add(series);
        context.SaveChanges();
        for (int i = 1; i <= issues; i++)
        {
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = i.ToString() });
        }

        context.SaveChanges();
        return series.Id;
    }

    [Fact]
    public void Down_And_Up_MoveBetweenRowsOfTheIssueGrid()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            int seriesId = SeedSeries(40);
            var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
            vm.LoadSeries(seriesId);
            var screen = new DetailScreen { DataContext = vm };
            var input = ReaderTestInput.Create();
            screen.InputService = input;
            var window = new Window { Content = screen, Width = 1300, Height = 900 };
            InputHost.Attach(window, input);
            window.Show();
            RunLayout(window);

            var tiles = screen.GetVisualDescendants().OfType<Control>().Where(c => c.Focusable && c.IsEffectivelyVisible && c.DataContext is IssueCardSample).ToList();
            Assert.True(tiles.Count > 8, $"tiles realised: {tiles.Count}");
            var first = tiles[0];
            first.Focus(NavigationMethod.Tab);
            RunLayout(window);

            string Focused() => (window.FocusManager!.GetFocusedElement() as Control)?.DataContext is IssueCardSample s ? s.Title : "(not a tile)";
            string start = Focused();

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            RunLayout(window);
            string afterRight = Focused();

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            RunLayout(window);
            string afterDown = Focused();

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            RunLayout(window);
            string afterUp = Focused();

            Assert.True(start != afterRight, $"start {start} afterRight {afterRight}");
            Assert.True(afterDown != afterRight, $"afterRight {afterRight} afterDown {afterDown}");
            Assert.True(afterUp != afterDown, $"afterDown {afterDown} afterUp {afterUp}");
            window.Close();
        });
    }

    [Theory]
    [InlineData("Continuity")]
    [InlineData("Wanted")]
    [InlineData("Preferences")]
    public void TheScreensKeyHandler_MovesFocusBetweenControlsInsideIt(string screenName)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            Control screen = screenName switch
            {
                "Continuity" => new Paperbunkr.App.Views.ContinuityScreen(),
                "Wanted" => new WantedScreen(),
                _ => new PreferencesScreen(),
            };
            var window = new Window { Content = screen, Width = 1300, Height = 900 };
            window.Show();
            RunLayout(window);

            // Two live buttons, side by side, added inside the screen: whatever the screen's own buttons are doing without a view model, an arrow press on one of these has to reach the screen's
            // directional handler and move focus to the other.
            var root = Assert.IsAssignableFrom<Panel>(((UserControl)screen).Content);
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            var left = new Button { Content = "left", Focusable = true };
            var right = new Button { Content = "right", Focusable = true };
            row.Children.Add(left);
            row.Children.Add(right);
            root.Children.Add(row);
            RunLayout(window);

            left.Focus(NavigationMethod.Tab);
            RunLayout(window);
            Assert.True(left.IsFocused, "the first test button took focus");

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            RunLayout(window);

            Assert.True(right.IsFocused, $"focus is on {(window.FocusManager!.GetFocusedElement() as Control)?.GetType().Name}");
            window.Close();
        });
    }
}
