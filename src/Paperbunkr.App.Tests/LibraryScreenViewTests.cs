using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Headless checks for Library's Phase 1 keyboard-focus-reclaim fix (docs/superpowers/specs/2026-09-28-keyboard-focus-reclaim-design.md):
/// initial focus with no prior click, and that a view-mode switch, a grouping toggle, and a search/filter reset - each of which swaps the
/// active grid's content in place while LibraryScreen stays visible and attached throughout - never drop focus outside the screen. No
/// existing headless view-rendering test covers LibraryScreen at all (<c>LibraryScreenViewModelTests</c> only exercises the VM), so this is
/// a new file, following <c>ReadingListsScreenViewTests</c>'s harness.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryScreenViewTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public LibraryScreenViewTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_library_view_test_{Guid.NewGuid():N}.db");
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

    /// <summary>Every Display mode is per-issue, so a series with no issues contributes no rows at all - matches
    /// <c>LibraryScreenViewModelTests</c>'s own <c>CreateSeriesWithIssue</c> helper.</summary>
    private static void CreateSeriesWithIssues(string seriesName, int issueCount, string? publisher = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = seriesName, ContentType = ContentType.Comic, Publisher = publisher };
        context.Series.Add(series);
        context.SaveChanges();
        for (int n = 1; n <= issueCount; n++)
        {
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = n.ToString(), Publisher = publisher });
        }

        context.SaveChanges();
    }

    /// <summary>The Fluent theme and the app tokens the view's StaticResources need (App.axaml isn't loaded headless) - same baseline
    /// Reading Lists' and Continuity's own view-test harnesses use.</summary>
    private static void WithThemeAndTokens(Action body)
    {
        TestAppBuilder.EnsureInitialized();
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        var resources = Application.Current.Resources;
        var tokens = new Dictionary<string, object>
        {
            ["PbMotionEase"] = new Avalonia.Animation.Easings.CubicEaseOut(),
            ["PbIconSizeXs"] = 14d,
            ["PbIconSizeSm"] = 16d,
            ["PbIconSizeLg"] = 24d,
            ["PbRadiusChip"] = new CornerRadius(6),
            ["PbElevationShadow"] = Avalonia.Media.BoxShadows.Parse("0 2 8 0 #40000000"),
            ["PbDisplayFontFamily"] = new Avalonia.Media.FontFamily("avares://Paperbunkr.App/Assets/Fonts/#Bebas Neue"),
        };
        var added = tokens.Keys.Where(k => !resources.ContainsKey(k)).ToList();
        foreach (var key in added)
        {
            resources[key] = tokens[key];
        }

        try
        {
            body();
        }
        finally
        {
            foreach (var key in added)
            {
                resources.Remove(key);
            }

            Application.Current!.Styles.Remove(theme);
        }
    }

    private static void RunLayout(Window window)
    {
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
    }

    private static (LibraryScreenViewModel Vm, Window Window) Show()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        var window = new Window { Content = new LibraryScreen { DataContext = vm }, Width = 1400, Height = 900 };
        window.Show();
        RunLayout(window);
        return (vm, window);
    }

    private static Visual? Focused(Window window) => window.FocusManager?.GetFocusedElement() as Visual;

    private static bool IsCard(Visual? visual) => visual is Button b && b.Classes.Contains("card");

    private static void Press(Window window, Key key)
    {
        string physical = key is Key.Left or Key.Right or Key.Up or Key.Down ? $"Arrow{key}" : key.ToString();
        window.KeyPress(key, RawInputModifiers.None, (PhysicalKey)Enum.Parse(typeof(PhysicalKey), physical), null);
        RunLayout(window);
    }

    [Fact]
    public void FocusRings_AreNotClipped()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = FocusTestHarness.AppStyles();
            CreateSeriesWithIssues("Incredible Hulk", 3);
            CreateSeriesWithIssues("Amazing Spider-Man", 2);
            var (_, window) = Show();
            RunLayout(window);
            var clipped = FocusTestHarness.ClippedFocusRings(window, (Visual)window.Content!);
            window.Close();
            Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
        });
    }

    [Fact]
    public void NoPriorClick_FocusLandsOnACard()
    {
        WithThemeAndTokens(() =>
        {
            CreateSeriesWithIssues("Incredible Hulk", 3);
            var (_, window) = Show();

            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)}");
            window.Close();
        });
    }

    [Fact]
    public void CardsSuppressTheDefaultKeyboardFocusAdorner()
    {
        WithThemeAndTokens(() =>
        {
            CreateSeriesWithIssues("Incredible Hulk", 3);
            var (_, window) = Show();

            var card = Assert.IsType<Button>(Focused(window));
            Assert.Null(card.FocusAdorner);
            window.Close();
        });
    }

    [Fact]
    public void SwitchingViewMode_KeepsFocusOnACard()
    {
        WithThemeAndTokens(() =>
        {
            CreateSeriesWithIssues("Incredible Hulk", 3);
            var (vm, window) = Show();
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)}");

            vm.ViewMode = LibraryViewMode.List;
            RunLayout(window);
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)} after switching to List");

            vm.ViewMode = LibraryViewMode.DetailsTable;
            RunLayout(window);
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)} after switching to Details");

            vm.ViewMode = LibraryViewMode.PosterGrid;
            vm.GridCoverFit = LibraryGridCoverFit.Panorama;
            RunLayout(window);
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)} after switching to Panorama");
            window.Close();
        });
    }

    [Fact]
    public void TogglingGrouping_KeepsFocusOnACard_AcrossMultipleGroups()
    {
        WithThemeAndTokens(() =>
        {
            // Two publishers so grouping by Publisher forms two distinct groups - exercises the nested-virtualization retry
            // (VirtualizedFocus.FocusIndex has to wait for a group's own inner virtualized panel to realize its first card).
            CreateSeriesWithIssues("Incredible Hulk", 3, "Marvel");
            CreateSeriesWithIssues("Amazing Spider-Man", 3, "Marvel");
            CreateSeriesWithIssues("Sandman", 3, "DC/Vertigo");
            var (vm, window) = Show();
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)}");

            vm.IssueList.GroupField = IssueListGroupField.Publisher;
            RunLayout(window);
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)} after grouping by Publisher");

            vm.IssueList.GroupField = IssueListGroupField.None;
            RunLayout(window);
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)} after ungrouping");
            window.Close();
        });
    }

    [Fact]
    public void ASearchThatResetsTheActiveCollection_KeepsFocusOnACard()
    {
        WithThemeAndTokens(() =>
        {
            CreateSeriesWithIssues("Incredible Hulk", 3);
            CreateSeriesWithIssues("Amazing Spider-Man", 3);
            var (vm, window) = Show();
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)}");

            vm.SearchQuery = "Hulk";
            RunLayout(window);
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)} after searching");

            vm.SearchQuery = string.Empty;
            RunLayout(window);
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)} after clearing the search");
            window.Close();
        });
    }

    [Fact]
    public void TypingInTheSearchBox_SurvivesTheCollectionResetsItTriggers()
    {
        WithThemeAndTokens(() =>
        {
            CreateSeriesWithIssues("Incredible Hulk", 3);
            CreateSeriesWithIssues("Amazing Spider-Man", 3);
            var (vm, window) = Show();

            var searchBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchBox");
            searchBox.Focus();
            RunLayout(window);
            Assert.Same(searchBox, Focused(window));

            // No debounce (LibraryScreenViewModel's own documented philosophy) - each keystroke's SearchQuery
            // assignment resets the active collection synchronously, the same as a real keystroke would.
            foreach (char c in "Hulk")
            {
                vm.SearchQuery += c;
                RunLayout(window);
                Assert.Same(searchBox, Focused(window));
            }

            window.Close();
        });
    }
}
