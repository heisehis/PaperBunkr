using Avalonia.Controls;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Keyboard focus for Insights (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-detail-home-smart-design.md): the
/// Today/Trends/Recap bodies and their range chips are permanently attached and toggled by <c>IsVisible</c>.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class InsightsScreenFocusTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;

    public InsightsScreenFocusTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_insights_focus_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(0);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(true);
    }

    private static (Window Window, InsightsScreen Screen, InsightsScreenViewModel Vm, Button Sibling) Show()
    {
        var vm = new InsightsScreenViewModel(_ => { }, _ => { }, _ => { }, () => { }, new FakeDialogService(),
            nowUtc: () => new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc));
        vm.Refresh();
        var screen = new InsightsScreen { DataContext = vm };
        var sibling = new Button { Content = "Rail" };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(sibling);
        Grid.SetRow(screen, 1);
        grid.Children.Add(screen);
        var window = new Window { Content = grid, Width = 1200, Height = 900 };
        window.Show();
        RunLayout(window);
        return (window, screen, vm, sibling);
    }

    [Fact]
    public void FocusRings_AreNotClipped()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            var (window, screen, vm, _) = Show();
            var clipped = new List<string>();
            foreach (var select in new Action[] { () => vm.SelectTodayTabCommand.Execute(null), () => vm.SelectTrendsTabCommand.Execute(null) })
            {
                select();
                RunLayout(window);
                clipped.AddRange(ClippedFocusRings(window, screen));
            }

            window.Close();
            Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
        });
    }

    [Fact]
    public void NoPriorClick_FocusLandsOnTheActiveTabHeader()
    {
        WithThemeAndTokens(() =>
        {
            var (window, screen, _, _) = Show();

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");
            var focused = Assert.IsType<Button>(Focused(window));
            Assert.Contains("active", focused.Classes);
            window.Close();
        });
    }

    [Fact]
    public void SwitchingTabs_WhileAChipHasFocus_KeepsFocusInsideTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            var (window, screen, vm, _) = Show();
            vm.SelectTrendsTabCommand.Execute(null);
            RunLayout(window);
            var chip = screen.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("rangeChip") && b.IsEffectivelyVisible);
            chip.Focus();
            RunLayout(window);
            Assert.Same(chip, Focused(window));

            vm.SelectTodayTabCommand.Execute(null);
            RunLayout(window);

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");
            window.Close();
        });
    }

    [Fact]
    public void ATabSwitch_DoesNotStealFocusFromASiblingRegion()
    {
        WithThemeAndTokens(() =>
        {
            var (window, _, vm, sibling) = Show();
            sibling.Focus();
            RunLayout(window);

            vm.SelectTrendsTabCommand.Execute(null);
            RunLayout(window);

            Assert.Same(sibling, Focused(window));
            window.Close();
        });
    }
}
