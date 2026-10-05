using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Daemon.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>The Wanted screen inside the real main window (the shell's own key handlers and the real input service), reached the way a keyboard user reaches it: from the nav rail.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class WantedInMainWindowTests : IDisposable
{
    private static readonly DateTime Today = DateTime.Today;
    private readonly string? _original;
    private readonly string _db;
    private readonly CoverCacheTestRedirect _cover;

    public WantedInMainWindowTests()
    {
        _original = PaperbunkrDbContext.DatabasePathOverride;
        _db = Path.Combine(Path.GetTempPath(), $"pb_wantedmain_{Guid.NewGuid():N}.db");
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

    private static void Seed(int volumes)
    {
        using var context = PaperbunkrDb.CreateContext();
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
        element is Control c ? $"{c.GetType().Name}[{string.Join(",", c.Classes.Where(k => !k.StartsWith(':')))}]{(c is Button { Content: string t } ? " \"" + t + "\"" : "")}@{(TopLevel.GetTopLevel(c) is { } root ? c.TranslatePoint(default, root)?.Y : null):0}" : "(none)";

    [Theory]
    [InlineData("queue")]
    [InlineData("series")]
    public void FromTheRail_RightEntersWanted_AndDownWalksTheRowsWithoutLosingFocusOrLeavingForTheRail(string tab)
    {
        // The Wanted lists are virtualizing ItemsControls: Avalonia's own arrow handling on an ItemsControl dropped focus between the screen's tunnel and bubble phases, which only shows in the
        // real window (a bare screen in a test window moved fine), so this walks the real thing.
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            Seed(12);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 800 };
            window.Show();
            vm.GoWantedCommand.Execute(null);
            vm.Wanted.Refresh();
            if (tab == "series")
            {
                vm.Wanted.GoSeriesCommand.Execute(null);
            }

            RunLayout(window);

            var railWanted = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("rail") && ReferenceEquals(b.Command, vm.GoWantedCommand));
            railWanted.Focus(NavigationMethod.Directional);
            RunLayout(window);

            var trail = new List<string>();
            Press(window, Key.Right);
            trail.Add("Right: " + Describe(window.FocusManager!.GetFocusedElement()));
            for (int i = 0; i < 14; i++)
            {
                Press(window, Key.Down);
                trail.Add("Down: " + Describe(window.FocusManager!.GetFocusedElement()));
            }

            string text = string.Join("\n", trail);
            var screen = window.GetVisualDescendants().OfType<WantedScreen>().First();
            Assert.True(trail.Skip(1).All(t => !t.Contains("[rail")), "Down never leaves the screen for the rail:\n" + text);
            Assert.True(FocusIsInside(window, screen), "focus stayed in the screen:\n" + text);
            Assert.True(trail.Skip(1).Select(t => t[(t.IndexOf(": ") + 2)..]).Distinct().Count() >= 10, "every press moved on:\n" + text);
            window.Close();
        });
    }
}
