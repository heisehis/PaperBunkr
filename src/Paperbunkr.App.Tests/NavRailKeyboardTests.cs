using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>The real main window: the arrow keys move around the nav rail, Right goes into the screen, and a Left that runs out of screen lands on the rail.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class NavRailKeyboardTests : IDisposable
{
    private readonly string? _original;
    private readonly string _db;
    private readonly CoverCacheTestRedirect _cover;

    public NavRailKeyboardTests()
    {
        _original = PaperbunkrDbContext.DatabasePathOverride;
        _db = Path.Combine(Path.GetTempPath(), $"pb_navrail_{Guid.NewGuid():N}.db");
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

    private static string Describe(object? element) => element is Control c ? $"{c.GetType().Name}[{string.Join(",", c.Classes)}]@{c.Bounds.X:0},{c.Bounds.Y:0}" : "null";

    [Fact]
    public void DownAndUp_StepThroughTheRail_AndRightLeavesIt_AndLeftComesBack()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            window.Show();
            RunLayout(window);

            var rail = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("rail") && b.IsEffectivelyVisible && b.IsEffectivelyEnabled).ToList();
            Assert.True(rail.Count >= 4, $"rail buttons: {rail.Count}");
            rail[0].Focus(NavigationMethod.Tab);
            RunLayout(window);
            Assert.Same(rail[0], window.FocusManager!.GetFocusedElement());

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            RunLayout(window);
            Assert.Same(rail[1], window.FocusManager!.GetFocusedElement());

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            RunLayout(window);
            Assert.Same(rail[2], window.FocusManager!.GetFocusedElement());

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            RunLayout(window);
            Assert.Same(rail[1], window.FocusManager!.GetFocusedElement());

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            RunLayout(window);
            var inside = window.FocusManager!.GetFocusedElement() as Visual;
            Assert.NotNull(inside);
            Assert.False(rail.Contains(inside as Button), "Right left the rail");

            // On a screen with no text fields (Insights), its first tab has nothing to its left, so a Left there goes to the rail.
            vm.GoInsightsCommand.Execute(null);
            RunLayout(window);
            var today = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("tab") && b.IsEffectivelyVisible && Equals(b.Content, "Today"));
            today.Focus(NavigationMethod.Directional);
            RunLayout(window);
            Assert.Same(today, window.FocusManager!.GetFocusedElement());

            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            RunLayout(window);

            Assert.True(rail.Contains(window.FocusManager!.GetFocusedElement() as Button), Describe(window.FocusManager!.GetFocusedElement()));
            window.Close();
        });
    }

    // Enter only: the headless harness does not click a plain Button on Space (it does not, with or without the app's input host), so Space cannot be checked here.
    [Fact]
    public void EnterOnARailButton_OpensItsScreen()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
            window.Show();
            RunLayout(window);
            Assert.True(vm.IsHome);

            var library = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("rail") && ReferenceEquals(b.Command, vm.GoLibraryCommand));
            library.Focus(NavigationMethod.Tab);
            RunLayout(window);

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            RunLayout(window);
            Assert.True(vm.IsLibrary);
            window.Close();
        });
    }
}
