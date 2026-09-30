using Avalonia.Controls;
using Paperbunkr.App.Models;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.ContinuityScreen;

/// <summary>
/// Keyboard focus reclaim for the Continuity screen (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md): the
/// Overview/Map/Timeline bodies stay attached side by side and only toggle <c>IsVisible</c>, so switching one hides whatever held focus.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ContinuityScreenFocusTests : ContinuityScreenTestBase
{
    private static (Window Window, Views.ContinuityScreen Screen) Show(ViewModels.ContinuityScreenViewModel vm, Control? sibling = null)
    {
        var screen = new Views.ContinuityScreen { DataContext = vm };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        if (sibling is not null)
        {
            grid.Children.Add(sibling);
        }

        Grid.SetRow(screen, 1);
        grid.Children.Add(screen);
        var window = new Window { Content = grid, Width = 1100, Height = 800 };
        window.Show();
        RunLayout(window);
        return (window, screen);
    }

    private static ViewModels.ContinuityScreenViewModel LoadedScreen()
    {
        var vm = CreateScreen();
        vm.LoadEvent(SeedEvent("Inferno", null, null, SeedIssue("X-Men", "1", year: 1988), SeedIssue("X-Factor", "1", year: 1988)));
        return vm;
    }

    [Fact]
    public void FocusRings_AreNotClipped()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            var (window, screen) = Show(LoadedScreen());
            RunLayout(window);
            var clipped = ClippedFocusRings(window, screen);
            window.Close();
            Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
        });
    }

    [Fact]
    public void NoPriorClick_FocusLandsInsideTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            var (window, screen) = Show(LoadedScreen());

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");
            window.Close();
        });
    }

    [Fact]
    public void SwitchingTheDetailView_KeepsFocusInsideTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            var vm = LoadedScreen();
            var (window, screen) = Show(vm);
            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");

            foreach (var view in new[] { EventsDetailView.Map, EventsDetailView.Timeline, EventsDetailView.Primary })
            {
                vm.DetailView = view;
                RunLayout(window);
                Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)} after switching to {view}");
            }

            window.Close();
        });
    }

    [Fact]
    public void ADetailViewSwitch_DoesNotStealFocusFromASiblingRegion()
    {
        WithThemeAndTokens(() =>
        {
            var vm = LoadedScreen();
            var sibling = new Button { Content = "Rail" };
            var (window, screen) = Show(vm, sibling);
            sibling.Focus();
            RunLayout(window);
            Assert.Same(sibling, Focused(window));

            vm.DetailView = EventsDetailView.Map;
            RunLayout(window);
            vm.DetailView = EventsDetailView.Timeline;
            RunLayout(window);

            Assert.Same(sibling, Focused(window));
            Assert.False(FocusIsInside(window, screen));
            window.Close();
        });
    }
}
