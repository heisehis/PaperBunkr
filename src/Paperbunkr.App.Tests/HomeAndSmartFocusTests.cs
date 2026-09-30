using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Keyboard focus for Home and Smart Lists (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-detail-home-smart-design.md):
/// focus lands inside with no prior click, survives the content being rebuilt, and never pulls focus from a sibling region.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class HomeAndSmartFocusTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public HomeAndSmartFocusTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_home_smart_focus_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static (Window Window, Control Screen, Button Sibling) Host(Control screen)
    {
        var sibling = new Button { Content = "Rail" };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(sibling);
        Grid.SetRow(screen, 1);
        grid.Children.Add(screen);
        var window = new Window { Content = grid, Width = 1200, Height = 900 };
        window.Show();
        RunLayout(window);
        return (window, screen, sibling);
    }

    private static void SeedInProgressSeries(string name)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = series.Id, LastPageRead = 30, PageCount = 100, AddedTime = DateTime.UtcNow, OpenedTime = DateTime.UtcNow });
        context.SaveChanges();
    }

    private static int SeedSmartListOfEverything()
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = "Series One" };
        context.Series.Add(series);
        context.SaveChanges();
        foreach (var number in new[] { "1", "2", "3" })
        {
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = number });
        }

        var list = new SmartList
        {
            Name = "Everything",
            IsSystem = false,
            RootGroup = new SmartListConditionGroup { Mode = SmartListGroupMode.And, Conditions = new List<SmartListCondition>() },
        };
        context.SmartLists.Add(list);
        context.SaveChanges();
        return list.Id;
    }

    /// <summary>The first tile's focus ring lives in its 6px gutter (PosterTile.axaml), which sits left of the page's content column. Any
    /// ancestor between the tile and the page scroller that clips would cut that side of the ring off - SectionsHost did, being an
    /// ItemsControl (they clip by default). So every clipping ancestor below the page scroller must contain the whole tile.</summary>
    [Fact]
    public void Home_FirstTile_IsNotClippedByAnythingInsideThePage()
    {
        WithThemeAndTokens(() =>
        {
            SeedInProgressSeries("In Progress");
            var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });
            var (window, screen, _) = Host(new HomeScreen { DataContext = vm });
            RunLayout(window);

            var tile = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(screen).OfType<PosterTile>().First();
            var tileRect = new Rect(tile.TranslatePoint(default, window)!.Value, tile.Bounds.Size);
            var cutters = Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(tile).OfType<Control>()
                .TakeWhile(a => a.Name != "PageScroller")
                .Where(a => a.ClipToBounds)
                .Select(a => (Control: a, Rect: new Rect(a.TranslatePoint(default, window)!.Value, a.Bounds.Size)))
                .Where(a => !a.Rect.Contains(tileRect))
                .Select(a => $"{a.Control.GetType().Name}#{a.Control.Name} {a.Rect}")
                .ToList();

            Assert.True(cutters.Count == 0, $"tile {tileRect} is clipped by: {string.Join("; ", cutters)}");
            window.Close();
        });
    }

    [Fact]
    public void Home_NoPriorClick_FocusLandsOnAShelfTile()
    {
        WithThemeAndTokens(() =>
        {
            SeedInProgressSeries("In Progress");
            var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });
            var (window, screen, _) = Host(new HomeScreen { DataContext = vm });

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");
            window.Close();
        });
    }

    [Fact]
    public void Home_RebuildingTheSections_KeepsFocusInsideTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            SeedInProgressSeries("In Progress");
            var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });
            var (window, screen, _) = Host(new HomeScreen { DataContext = vm });
            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");

            var sections = vm.Sections.ToList();
            vm.Sections.Clear();
            foreach (var section in sections)
            {
                vm.Sections.Add(section);
            }

            RunLayout(window);

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");
            window.Close();
        });
    }

    [Fact]
    public void Home_ARebuildWhileFocusIsInASiblingRegion_DoesNotStealFocus()
    {
        WithThemeAndTokens(() =>
        {
            SeedInProgressSeries("In Progress");
            var vm = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });
            var (window, _, sibling) = Host(new HomeScreen { DataContext = vm });
            sibling.Focus();
            RunLayout(window);

            var sections = vm.Sections.ToList();
            vm.Sections.Clear();
            foreach (var section in sections)
            {
                vm.Sections.Add(section);
            }

            RunLayout(window);

            Assert.Same(sibling, Focused(window));
            window.Close();
        });
    }

    [Fact]
    public void FocusRings_AreNotClipped_OnHomeAndSmart()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            SeedInProgressSeries("In Progress");
            int listId = SeedSmartListOfEverything();
            var home = new HomeScreen { DataContext = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { }) };
            var (window, homeScreen, _) = Host(home);
            RunLayout(window);
            var clipped = ClippedFocusRings(window, homeScreen).Select(c => "[home] " + c).ToList();
            window.Close();

            var smartVm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
            var (smartWindow, smartScreen, _) = Host(new SmartScreen { DataContext = smartVm });
            smartVm.LoadSmartList(listId);
            RunLayout(smartWindow);
            clipped.AddRange(ClippedFocusRings(smartWindow, smartScreen).Select(c => "[smart] " + c));
            smartWindow.Close();
            Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
        });
    }

    [Fact]
    public void Smart_NoPriorClick_FocusLandsOnAResultCard()
    {
        WithThemeAndTokens(() =>
        {
            int listId = SeedSmartListOfEverything();
            var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
            var (window, screen, _) = Host(new SmartScreen { DataContext = vm });
            vm.LoadSmartList(listId);
            RunLayout(window);

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");
            window.Close();
        });
    }

    [Fact]
    public void Smart_ReloadingTheResults_KeepsFocusInsideTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            int listId = SeedSmartListOfEverything();
            var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
            var (window, screen, _) = Host(new SmartScreen { DataContext = vm });
            vm.LoadSmartList(listId);
            RunLayout(window);
            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");

            vm.LoadSmartList(listId);
            RunLayout(window);

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");
            window.Close();
        });
    }

    [Fact]
    public void Smart_EscapeClosesGroupedReview_AndFocusStaysInsideTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            int listId = SeedSmartListOfEverything();
            var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
            var (window, screen, _) = Host(new SmartScreen { DataContext = vm });
            vm.LoadSmartList(listId);
            RunLayout(window);

            vm.IsGroupedReviewOpen = true;
            RunLayout(window);
            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)} while grouped review is open");

            Press(window, Key.Escape);

            Assert.False(vm.IsGroupedReviewOpen);
            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)} after closing");
            window.Close();
        });
    }
}
