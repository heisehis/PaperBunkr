using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Keyboard behaviour that only shows up in the real main window (the shell's key handlers, the real input service, the app's real styles), with a library that has something in it. A screen hosted
/// bare in a test window has repeatedly passed where the real one failed, so these walk the real thing.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class RealWindowKeyboardTests : IDisposable
{
    private readonly string? _original;
    private readonly string _db;
    private readonly CoverCacheTestRedirect _cover;

    public RealWindowKeyboardTests()
    {
        _original = PaperbunkrDbContext.DatabasePathOverride;
        _db = Path.Combine(Path.GetTempPath(), $"pb_realwindow_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _db;
        _cover = new CoverCacheTestRedirect();
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_db}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _original;
        _cover.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }

    private static string Describe(object? element) =>
        element is Control c ? $"{c.GetType().Name}[{string.Join(",", c.Classes.Where(k => !k.StartsWith(':')))}]@{(TopLevel.GetTopLevel(c) is { } root ? c.TranslatePoint(default, root)?.X : null):0},{(TopLevel.GetTopLevel(c) is { } root2 ? c.TranslatePoint(default, root2)?.Y : null):0}" : "(none)";

    private static void SeedSeries(int series, int issuesEach, bool inProgress)
    {
        using var context = PaperbunkrDb.CreateContext();
        for (int s = 1; s <= series; s++)
        {
            var entity = new Series { Name = $"Series {s:00}", Publisher = s % 2 == 0 ? "Even Press" : "Odd Press" };
            context.Series.Add(entity);
            context.SaveChanges();
            for (int i = 1; i <= issuesEach; i++)
            {
                context.Issues.Add(new Issue
                {
                    SeriesId = entity.Id,
                    Number = i.ToString(),
                    FilePath = $"s{s}i{i}.cbz",
                    LastPageRead = inProgress ? 30 : 0,
                    PageCount = 100,
                    AddedTime = DateTime.UtcNow,
                    OpenedTime = inProgress ? DateTime.UtcNow : null,
                });
            }
        }

        context.SaveChanges();
    }

    private static bool OnRail(Window window) => window.FocusManager!.GetFocusedElement() is Button b && b.Classes.Contains("rail");

    [Fact]
    public void Home_LeftFromTheFirstTile_GoesToTheNavRail()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            SeedSeries(series: 4, issuesEach: 2, inProgress: true);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            window.Show();
            vm.Home.LoadFromDatabase();
            RunLayout(window);

            var screen = window.GetVisualDescendants().OfType<HomeScreen>().First();
            var tiles = screen.GetVisualDescendants().OfType<PosterTile>().Where(t => t.IsEffectivelyVisible).OrderBy(t => t.TranslatePoint(default, window)!.Value.Y).ThenBy(t => t.TranslatePoint(default, window)!.Value.X).ToList();
            Assert.True(tiles.Count >= 2, $"tiles realised: {tiles.Count}");
            var first = tiles[0];
            first.Root.Focus(NavigationMethod.Directional);
            RunLayout(window);

            Press(window, Key.Right);
            Assert.False(OnRail(window), "Right stays on the shelf");
            Press(window, Key.Left);
            Press(window, Key.Left);

            Assert.True(OnRail(window), "Left from the first tile reaches the nav rail; focus is on " + Describe(window.FocusManager!.GetFocusedElement()));
            window.Close();
        });
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData(Paperbunkr.Data.Entities.LibraryViewMode.PosterGrid, false)]
    [InlineData(Paperbunkr.Data.Entities.LibraryViewMode.List, false)]
    [InlineData(Paperbunkr.Data.Entities.LibraryViewMode.DetailsTable, false)]
    [InlineData(Paperbunkr.Data.Entities.LibraryViewMode.PosterGrid, true)]
    public void Library_Grouped_ArrowKeysCarryOnFromOneGroupIntoTheNext(Paperbunkr.Data.Entities.LibraryViewMode? mode, bool bySeries)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            using (var context = PaperbunkrDb.CreateContext())
            {
                for (int n = 1; n <= 30; n++)
                {
                    var entity = new Series { Name = $"Series {n:00}", Publisher = $"Publisher {n % 3}" };
                    context.Series.Add(entity);
                    context.SaveChanges();
                    context.Issues.Add(new Issue { SeriesId = entity.Id, Number = "1", FilePath = $"s{n}.cbz", PageCount = 20, AddedTime = DateTime.UtcNow });
                }

                context.SaveChanges();
            }

            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            window.Show();
            vm.GoLibraryCommand.Execute(null);
            vm.Library.IssueList.ConfigureSortGroup(Paperbunkr.Data.Entities.IssueListSortField.Series, Paperbunkr.Data.Entities.SortDirection.Ascending, Paperbunkr.Data.Entities.IssueListGroupField.Publisher);
            if (mode is { } chosen)
            {
                vm.Library.SetViewModeCommand.Execute(chosen);
            }

            if (bySeries)
            {
                vm.Library.SetGranularityCommand.Execute(Paperbunkr.Data.Entities.LibraryContentGranularity.Series);
            }

            vm.Library.LoadFromDatabase();
            RunLayout(window);

            var screen = window.GetVisualDescendants().OfType<LibraryScreen>().First();
            Assert.True(vm.Library.IsGrouped, "the library is grouped");
            var cards = screen.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible).ToList();
            Assert.True(cards.Count > 6, $"cards realised: {cards.Count}");
            string first3 = string.Join(" | ", cards.Take(3).Select(c => c.DataContext?.GetType().Name + ":" + c.DataContext));
            static int GroupOf(object? element) => element is Control { DataContext: var data } && (data is Paperbunkr.App.Models.IssueListRow row ? row.SeriesName : data is Paperbunkr.App.Models.SeriesCardSample card ? card.Name : null) is { Length: > 2 } name && int.TryParse(name[^2..], out int n) ? n % 3 : -1;
            var first = cards.OrderBy(c => c.TranslatePoint(default, window)!.Value.Y).ThenBy(c => c.TranslatePoint(default, window)!.Value.X).First();
            first.Focus(NavigationMethod.Directional);
            RunLayout(window);

            var groupsSeen = new List<int> { GroupOf(window.FocusManager!.GetFocusedElement()) };
            var trail = new List<string> { Describe(window.FocusManager!.GetFocusedElement()) };
            for (int i = 0; i < 25; i++)
            {
                Press(window, Key.Down);
                var now = window.FocusManager!.GetFocusedElement();
                trail.Add(Describe(now));
                Assert.NotNull(now);
                if (GroupOf(now) is var g and >= 0 && g != groupsSeen[^1])
                {
                    groupsSeen.Add(g);
                }
            }

            Assert.True(groupsSeen.Count >= 2, "Down walked into a second group; first cards " + first3 + "; mode=" + vm.Library.ViewMode + " gran=" + vm.Library.Granularity + " panorama=" + vm.Library.IsPanoramaGrid + " tiles=" + vm.Library.IsTilesView + " trail:\n" + string.Join("\n", trail));
            window.Close();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Library_Grouped_RightAndLeftWalkEveryCardAcrossTheGroups(bool bySeries)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            using (var context = PaperbunkrDb.CreateContext())
            {
                for (int n = 1; n <= 30; n++)
                {
                    var entity = new Series { Name = $"Series {n:00}", Publisher = $"Publisher {n % 3}" };
                    context.Series.Add(entity);
                    context.SaveChanges();
                    context.Issues.Add(new Issue { SeriesId = entity.Id, Number = "1", FilePath = $"s{n}.cbz", PageCount = 20, AddedTime = DateTime.UtcNow });
                }

                context.SaveChanges();
            }

            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            window.Show();
            vm.GoLibraryCommand.Execute(null);
            vm.Library.IssueList.ConfigureSortGroup(Paperbunkr.Data.Entities.IssueListSortField.Series, Paperbunkr.Data.Entities.SortDirection.Ascending, Paperbunkr.Data.Entities.IssueListGroupField.Publisher);
            if (bySeries)
            {
                vm.Library.SetGranularityCommand.Execute(Paperbunkr.Data.Entities.LibraryContentGranularity.Series);
            }

            vm.Library.LoadFromDatabase();
            RunLayout(window);

            var screen = window.GetVisualDescendants().OfType<LibraryScreen>().First();
            var cards = screen.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible).ToList();
            var first = cards.OrderBy(c => c.TranslatePoint(default, window)!.Value.Y).ThenBy(c => c.TranslatePoint(default, window)!.Value.X).First();
            first.Focus(NavigationMethod.Directional);
            RunLayout(window);

            static string? NameOf(object? element) => element is Control { DataContext: var data } ? (data is Paperbunkr.App.Models.IssueListRow row ? row.SeriesName : data is Paperbunkr.App.Models.SeriesCardSample card ? card.Name : null) : null;
            var visited = new List<string>();
            for (int i = 0; i < 45; i++)
            {
                if (NameOf(window.FocusManager!.GetFocusedElement()) is { } name && (visited.Count == 0 || visited[^1] != name))
                {
                    visited.Add(name);
                }

                Press(window, Key.Right);
            }

            Assert.True(visited.Distinct().Count() >= 30, $"Right visited {visited.Distinct().Count()} of 30 cards: \nwhere: " + string.Join(", ", visited));
            window.Close();
        });
    }

    [Trait("Speed", "Slow")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Library_Grouped_BigGroups_DownGetsIntoTheNextGroup(bool bySeries)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            using (var context = PaperbunkrDb.CreateContext())
            {
                for (int n = 1; n <= 240; n++)
                {
                    var entity = new Series { Name = $"Series {n:000}", Publisher = $"Publisher {(n - 1) / 80}" };
                    context.Series.Add(entity);
                    context.SaveChanges();
                    context.Issues.Add(new Issue { SeriesId = entity.Id, Number = "1", FilePath = $"s{n}.cbz", PageCount = 20, AddedTime = DateTime.UtcNow });
                }

                context.SaveChanges();
            }

            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            window.Show();
            vm.GoLibraryCommand.Execute(null);
            vm.Library.IssueList.ConfigureSortGroup(Paperbunkr.Data.Entities.IssueListSortField.Series, Paperbunkr.Data.Entities.SortDirection.Ascending, Paperbunkr.Data.Entities.IssueListGroupField.Publisher);
            if (bySeries)
            {
                vm.Library.SetGranularityCommand.Execute(Paperbunkr.Data.Entities.LibraryContentGranularity.Series);
            }

            vm.Library.LoadFromDatabase();
            RunLayout(window);

            var screen = window.GetVisualDescendants().OfType<LibraryScreen>().First();
            var cards = screen.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible).ToList();
            var first = cards.OrderBy(c => c.TranslatePoint(default, window)!.Value.Y).ThenBy(c => c.TranslatePoint(default, window)!.Value.X).First();
            first.Focus(NavigationMethod.Directional);
            RunLayout(window);

            static int NumberOf(object? element) => element is Control { DataContext: var data } && (data is Paperbunkr.App.Models.IssueListRow row ? row.SeriesName : data is Paperbunkr.App.Models.SeriesCardSample card ? card.Name : null) is { Length: > 3 } name && int.TryParse(name[^3..], out int n) ? n : -1;
            var trail = new List<int>();
            int reached = 0;
            for (int i = 0; i < 120; i++)
            {
                Press(window, Key.Down);
                TestDispatcher.Drain();
                RunLayout(window);
                int n = NumberOf(window.FocusManager!.GetFocusedElement());
                trail.Add(n);
                reached = Math.Max(reached, n);
            }

            Assert.True(reached > 80, "Down got past the first group of 80 (highest series reached " + reached + "); trail: " + string.Join(",", trail.Take(60)) + "; focus now " + Describe(window.FocusManager!.GetFocusedElement()));

            // And back up again: from wherever that left focus, Up has to climb out of the later groups into the first.
            var upTrail = new List<int>();
            int lowest = int.MaxValue;
            for (int i = 0; i < 200; i++)
            {
                Press(window, Key.Up);
                TestDispatcher.Drain();
                RunLayout(window);
                int n = NumberOf(window.FocusManager!.GetFocusedElement());
                upTrail.Add(n);
                if (n > 0)
                {
                    lowest = Math.Min(lowest, n);
                }
            }

            Assert.True(lowest <= 20, "Up climbed back to the top of the first group (lowest series reached " + lowest + "); trail: " + string.Join(",", upTrail.Take(80)) + "; focus now " + Describe(window.FocusManager!.GetFocusedElement()));
            window.Close();
        });
    }

    [Fact]
    public void KeyboardShortcuts_DownWalksTheList_RightEntersTheEditor_AndLeftComesBackToTheSameRow()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            // The shell takes its input service from the locator at construction, and the default is the do-nothing one with no actions to list.
            var previousService = InputServiceLocator.Current;
            InputServiceLocator.Current = new InputService(InputActionCatalog.CreateWithCoreActions(), new MemoryKeymapStore());
            try
            {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            window.Show();
            vm.GoPreferencesCommand.Execute(null);
            vm.Preferences.ActiveSection = PreferencesSection.KeyboardShortcuts;
            RunLayout(window);

            var section = window.GetVisualDescendants().OfType<Paperbunkr.App.Views.Preferences.KeyboardShortcutsSection>().First();
            var rows = section.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ksRow") && b.IsEffectivelyVisible).ToList();
            Assert.True(rows.Count >= 5, $"list rows: {rows.Count}");
            rows[0].Focus(NavigationMethod.Directional);
            RunLayout(window);

            // Down walks the list a row at a time, and the editor follows the focused row.
            for (int i = 1; i <= 3; i++)
            {
                Press(window, Key.Down);
                Assert.True(ReferenceEquals(rows[i], window.FocusManager!.GetFocusedElement()), $"press {i}: focus is {Describe(window.FocusManager!.GetFocusedElement())}, expected {Describe(rows[i])}; row0 {Describe(rows[0])} y-positions {string.Join(",", rows.Take(5).Select(r => (int)r.TranslatePoint(default, window)!.Value.Y))}");
                Assert.Same(rows[i].DataContext, vm.Preferences.Shortcuts.SelectedRow);
            }

            // Right goes into the editor pane, and Left comes back to the row that is selected.
            Press(window, Key.Right);
            var inPane = window.FocusManager!.GetFocusedElement();
            Assert.True(inPane is Visual v && v.GetVisualAncestors().OfType<ScrollViewer>().Any(s => s.Name == "PaneScroll"), "Right entered the editor pane, focus is on " + Describe(inPane));
            Press(window, Key.Left);
            Assert.Same(rows[3], window.FocusManager!.GetFocusedElement());
            window.Close();
            }
            finally
            {
                InputServiceLocator.Current = previousService;
            }
        });
    }

    [Trait("Speed", "Slow")]
    [Theory]
    [InlineData(PreferencesSection.General)]
    [InlineData(PreferencesSection.Appearance)]
    [InlineData(PreferencesSection.Library)]
    [InlineData(PreferencesSection.Automation)]
    [InlineData(PreferencesSection.Reader)]
    [InlineData(PreferencesSection.KeyboardShortcuts)]
    [InlineData(PreferencesSection.Connections)]
    [InlineData(PreferencesSection.Plugins)]
    [InlineData(PreferencesSection.Advanced)]
    [InlineData(PreferencesSection.About)]
    public void Preferences_DownNeverLosesFocus_AndArrowsReachTheSidebarItemsToo(PreferencesSection section)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            window.Show();
            vm.GoPreferencesCommand.Execute(null);
            vm.Preferences.ActiveSection = section;
            RunLayout(window);

            var screen = window.GetVisualDescendants().OfType<PreferencesScreen>().First();
            var items = screen.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("prefNavItem") && b.IsEffectivelyVisible).ToList();
            Assert.True(items.Count >= 8, $"sidebar items: {items.Count}");
            items[0].Focus(NavigationMethod.Directional);
            RunLayout(window);

            var trail = new List<string>();
            for (int i = 0; i < 12; i++)
            {
                Press(window, Key.Down);
                var now = window.FocusManager!.GetFocusedElement();
                trail.Add(Describe(now));
                Assert.True(now is not null, "focus was lost after press " + (i + 1) + ": " + string.Join(" -> ", trail));
            }

            Assert.True(trail.Distinct().Count() >= 6, "Down walked down the sidebar: " + string.Join(" -> ", trail));
            window.Close();
        });
    }

    [Trait("Speed", "Slow")]
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Library_Alphabetical_ArrowsLeaveTheToolbarForTheGrid_AndCrossTheLetterGroups(bool bySeries, bool alphabetical)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            using (var context = PaperbunkrDb.CreateContext())
            {
                for (int n = 0; n < 100; n++)
                {
                    char letter = (char)('A' + (n / 20));
                    var entity = new Series { Name = $"{letter}series {n:000}" };
                    context.Series.Add(entity);
                    context.SaveChanges();
                    context.Issues.Add(new Issue { SeriesId = entity.Id, Number = "1", FilePath = $"s{n}.cbz", PageCount = 20, AddedTime = DateTime.UtcNow });
                }

                context.SaveChanges();
            }

            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            window.Show();
            vm.GoLibraryCommand.Execute(null);
            vm.Library.IssueList.ConfigureSortGroup(Paperbunkr.Data.Entities.IssueListSortField.Series, Paperbunkr.Data.Entities.SortDirection.Ascending,
                alphabetical ? Paperbunkr.Data.Entities.IssueListGroupField.Series : Paperbunkr.Data.Entities.IssueListGroupField.None);
            if (bySeries)
            {
                vm.Library.SetGranularityCommand.Execute(Paperbunkr.Data.Entities.LibraryContentGranularity.Series);
            }

            vm.Library.LoadFromDatabase();
            RunLayout(window);

            var screen = window.GetVisualDescendants().OfType<LibraryScreen>().First();
            bool IsCard(object? e) => e is Button b && b.Classes.Contains("card");

            // From a toolbar button, a few Down presses must reach the grid.
            var toolbarButton = screen.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled && b.Focusable && !b.Classes.Contains("card")
                && b.TranslatePoint(default, window)!.Value.Y < 90 && b.TranslatePoint(default, window)!.Value.X > 300).OrderBy(b => b.TranslatePoint(default, window)!.Value.X).First();
            toolbarButton.Focus(NavigationMethod.Directional);
            RunLayout(window);
            var toolbarTrail = new List<string>();
            for (int i = 0; i < 6 && !IsCard(window.FocusManager!.GetFocusedElement()); i++)
            {
                Press(window, Key.Down);
                toolbarTrail.Add(Describe(window.FocusManager!.GetFocusedElement()));
            }

            Assert.True(IsCard(window.FocusManager!.GetFocusedElement()), "Down from the toolbar reached the grid: " + string.Join(" -> ", toolbarTrail));

            // And in the grid, Down has to carry on past the first group (20 series each).
            static string? NameOf(object? element) => element is Control { DataContext: var data } ? (data is Paperbunkr.App.Models.IssueListRow row ? row.SeriesName : data is Paperbunkr.App.Models.SeriesCardSample card ? card.Name : null) : null;
            var letters = new List<char>();
            for (int i = 0; i < 80; i++)
            {
                if (NameOf(window.FocusManager!.GetFocusedElement()) is { Length: > 0 } name && (letters.Count == 0 || letters[^1] != name[0]))
                {
                    letters.Add(name[0]);
                }

                Press(window, Key.Down);
                TestDispatcher.Drain();
            }

            Assert.True(letters.Count >= 2 || !alphabetical, "Down reached a second letter group: " + string.Join(",", letters) + " ... focus on " + Describe(window.FocusManager!.GetFocusedElement()));

            // And back up through the groups to the first.
            var up = new List<char>();
            for (int i = 0; i < 120; i++)
            {
                Press(window, Key.Up);
                TestDispatcher.Drain();
                if (NameOf(window.FocusManager!.GetFocusedElement()) is { Length: > 0 } name && (up.Count == 0 || up[^1] != name[0]))
                {
                    up.Add(name[0]);
                }
            }

            Assert.True(!alphabetical || up.Contains('A'), "Up climbed back to the first group: " + string.Join(",", up) + " ... focus on " + Describe(window.FocusManager!.GetFocusedElement()));
            window.Close();
        });
    }
}
