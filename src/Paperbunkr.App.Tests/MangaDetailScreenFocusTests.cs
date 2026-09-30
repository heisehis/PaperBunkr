using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Keyboard reach on the Manga detail screen (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-detail-home-smart-design.md):
/// focus lands on a chapter row with no prior click, Up/Down step between rows, a tab switch keeps focus inside, and no focus ring is clipped.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MangaDetailScreenFocusTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly int _seriesId;

    public MangaDetailScreenFocusTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_manga_focus_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        var series = new Series { Name = "Test Manga", ContentType = ContentType.Manga, Status = SeriesStatus.Ongoing };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;
        context.Issues.AddRange(
            new Issue { SeriesId = series.Id, Number = "1", LastPageRead = 10, PageCount = 10 },
            new Issue { SeriesId = series.Id, Number = "2", LastPageRead = 3, PageCount = 10 },
            new Issue { SeriesId = series.Id, Number = "3" },
            new Issue { SeriesId = series.Id, Number = "4" });
        context.SaveChanges();
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

    private (Window Window, MangaDetailScreen Screen, MangaDetailScreenViewModel Vm, Button Sibling) Show()
    {
        var vm = new MangaDetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);
        var screen = new MangaDetailScreen { DataContext = vm };
        var sibling = new Button { Content = "Rail" };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*") };
        grid.Children.Add(sibling);
        Grid.SetColumn(screen, 1);
        grid.Children.Add(screen);
        var window = new Window { Content = grid, Width = 1300, Height = 900 };
        window.Show();
        RunLayout(window);
        return (window, screen, vm, sibling);
    }

    private static List<Button> ChapterRows(Visual screen) =>
        screen.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("chapterRow") && b.IsEffectivelyVisible).ToList();

    [Fact]
    public void NoPriorClick_FocusLandsOnTheFirstChapterRow()
    {
        WithThemeAndTokens(() =>
        {
            using var tokens = AppResources();
            var (window, screen, _, _) = Show();

            var rows = ChapterRows(screen);
            Assert.NotEmpty(rows);
            Assert.Same(rows[0], Focused(window));
            window.Close();
        });
    }

    [Fact]
    public void UpDown_StepBetweenChapterRows()
    {
        WithThemeAndTokens(() =>
        {
            using var tokens = AppResources();
            var (window, screen, _, _) = Show();
            var rows = ChapterRows(screen);
            rows[0].Focus(NavigationMethod.Directional);
            RunLayout(window);

            Press(window, Key.Down);
            Assert.Same(rows[1], Focused(window));

            Press(window, Key.Down);
            Assert.Same(rows[2], Focused(window));

            Press(window, Key.Up);
            Assert.Same(rows[1], Focused(window));
            window.Close();
        });
    }

    [Fact]
    public void SwitchingTabs_KeepsFocusInsideTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            using var tokens = AppResources();
            var (window, screen, vm, _) = Show();
            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");

            foreach (var tab in new[] { "related", "details", "activity", "chapters" })
            {
                vm.MangaActiveTab = tab;
                RunLayout(window);
                Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)} after switching to {tab}");
            }

            window.Close();
        });
    }

    [Fact]
    public void ATabSwitch_DoesNotStealFocusFromASiblingRegion()
    {
        WithThemeAndTokens(() =>
        {
            using var tokens = AppResources();
            var (window, _, vm, sibling) = Show();
            sibling.Focus();
            RunLayout(window);

            vm.MangaActiveTab = "details";
            RunLayout(window);
            vm.MangaActiveTab = "chapters";
            RunLayout(window);

            Assert.Same(sibling, Focused(window));
            window.Close();
        });
    }

    [Fact]
    public void FocusRings_AreNotClipped()
    {
        WithThemeAndTokens(() =>
        {
            using var tokens = AppResources();
            using var styles = AppStyles();
            var (window, screen, vm, _) = Show();
            var clipped = new List<string>();
            foreach (var tab in new[] { "chapters", "related", "details", "activity" })
            {
                vm.MangaActiveTab = tab;
                RunLayout(window);
                clipped.AddRange(ClippedFocusRings(window, screen).Select(c => $"[{tab}] {c}"));
            }

            window.Close();
            Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
        });
    }
}
