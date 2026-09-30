using Avalonia.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

/// <summary>
/// Keyboard focus reclaim for the Wanted screen (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md): the
/// Queue/Series/Releases bodies are permanently attached and toggled by <c>IsVisible</c>, so a tab switch hides whatever held focus.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class WantedScreenFocusTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 9, 19);
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_wanted_focus_{Guid.NewGuid():N}.db");

    public WantedScreenFocusTests()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath};Foreign Keys=True").Options);

    private WantedScreenViewModel CreateVm() => new(
        NewContext,
        _ => Task.CompletedTask,
        _ => { },
        () => { },
        _ => Task.CompletedTask,
        new GrabService(NewContext, _ => null, new ChannelEventPublisher()),
        post: a => a(),
        today: () => Today,
        notify: (_, _) => { });

    private void SeedQueue()
    {
        using var context = NewContext();
        var series = new Series { Name = "Spawn" };
        context.Series.Add(series);
        context.SaveChanges();
        var watched = WantedService.TrackVolume(context, new ComicVineVolume(100, "Spawn", "Image", 1992, 300, null), series.Id, watchFutureReleases: false);
        WantedService.RefreshCatalog(context, watched, new[]
        {
            new ComicVineIssue(1, "261", null, Today.AddDays(-7), null, null, 100),
            new ComicVineIssue(2, "262", null, Today.AddDays(-1), null, null, 100),
        });
        WantedService.Request(context, watched, context.CatalogIssues.Single(c => c.ExternalIssueId == 1));
        WantedService.Request(context, watched, context.CatalogIssues.Single(c => c.ExternalIssueId == 2));
        context.SaveChanges();
    }

    private static (Window Window, WantedScreen Screen, Button Sibling) Show(WantedScreenViewModel vm)
    {
        var screen = new WantedScreen { DataContext = vm };
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

    [Fact]
    public void FocusRings_AreNotClipped()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            SeedQueue();
            var vm = CreateVm();
            vm.Refresh();
            var (window, screen, _) = Show(vm);
            RunLayout(window);
            var clipped = ClippedFocusRings(window, screen);
            window.Close();
            Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
        });
    }

    [Fact]
    public void NoPriorClick_WithRows_FocusLandsInsideTheScreenOnARowNotTheTabs()
    {
        WithThemeAndTokens(() =>
        {
            SeedQueue();
            var vm = CreateVm();
            vm.Refresh();
            var (window, screen, _) = Show(vm);

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");
            Assert.False(Focused(window) is Button b && b.Classes.Contains("tab"), "focus should be on a queue row, not a tab header");
            window.Close();
        });
    }

    [Fact]
    public void NoPriorClick_WithNothingWanted_FocusFallsBackToTheActiveTab()
    {
        WithThemeAndTokens(() =>
        {
            var vm = CreateVm();
            vm.Refresh();
            var (window, screen, _) = Show(vm);

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");
            var focused = Assert.IsType<Button>(Focused(window));
            Assert.Contains("active", focused.Classes);
            window.Close();
        });
    }

    [Fact]
    public void SwitchingTabs_KeepsFocusInsideTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            SeedQueue();
            var vm = CreateVm();
            vm.Refresh();
            var (window, screen, _) = Show(vm);
            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");

            foreach (var tab in new[] { WantedTab.Series, WantedTab.Releases, WantedTab.Queue })
            {
                vm.ActiveTab = tab;
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
            SeedQueue();
            var vm = CreateVm();
            vm.Refresh();
            var (window, _, sibling) = Show(vm);
            sibling.Focus();
            RunLayout(window);
            Assert.Same(sibling, Focused(window));

            vm.ActiveTab = WantedTab.Series;
            RunLayout(window);
            vm.Refresh();
            RunLayout(window);

            Assert.Same(sibling, Focused(window));
            window.Close();
        });
    }
}
