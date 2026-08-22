using FlaUI.Core.AutomationElements;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.UiTests;

/// <summary>
/// On-screen verification of the Home screen (docs/superpowers/specs/2026-08-18-home-screen-
/// design.md) - confirms the app now launches to Home by default, the rail nav entry exists and is
/// clickable, all 5 modules render (as their real content or their empty state on a fresh library),
/// and a card click navigates away from Home for one row-module (Recently Added) and one
/// single-card module (Spotlight). Query/pick logic itself is covered by
/// <c>HomeFeedResolverTests</c> (Paperbunkr.Data.Tests) and command-wiring by
/// <c>HomeScreenViewModelTests</c> (Paperbunkr.App.Tests) - including Continue Reading's click
/// wiring, which isn't re-verified here: producing a real in-progress reading position needs an
/// actual comic archive to open and page through, which no UI-automation fixture in this codebase
/// has (same standing limitation noted throughout the Reader specs). Seeds data directly into
/// <see cref="AppFixture.DbPath"/> rather than through the "+ Add a physical book" flow - that flow
/// routes through Issue Properties before landing back on Library, adding real complexity for no
/// extra coverage over a direct seed.
/// </summary>
public class HomeScreenTests : IDisposable
{
    private readonly AppFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private void Seed(Action<PaperbunkrDbContext> configure)
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_fixture.DbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        configure(context);
        context.SaveChanges();
    }

    [Fact]
    public void App_LaunchesToHome_ByDefault()
    {
        var window = _fixture.Window;

        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeRailButton")));
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeContinueReadingHeader")));
        Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySearchBox")));
    }

    [Fact]
    public void HomeRailButton_NavigatesBackToHome_AfterLeavingIt()
    {
        var window = _fixture.Window;

        window.FindFirstDescendant(cf => cf.ByAutomationId("LibraryRailButton"))!.AsButton().Invoke();
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("LibrarySearchBox")));

        window.FindFirstDescendant(cf => cf.ByAutomationId("HomeRailButton"))!.AsButton().Invoke();

        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeContinueReadingHeader")));
    }

    [Fact]
    public void AllFiveModules_RenderTheirEmptyStates_OnAFreshLibrary()
    {
        var window = _fixture.Window;

        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeContinueReadingHeader")));
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeRecentlyAddedHeader")));
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeBecauseYouReadEmptyState")));
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeSpotlightHeader")));
        Assert.NotNull(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeReadingListSpotlightHeader")));

        // Empty states, not real cards, since the fresh test database has no data.
        Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeSpotlightCard")));
        Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeReadingListSpotlightCard")));
    }

    [Fact]
    public void RecentlyAddedCard_Click_NavigatesAwayFromHome()
    {
        Seed(context =>
        {
            var series = new Series { Name = "Freshly Added Series" };
            context.Series.Add(series);
            context.SaveChanges();
            context.Issues.Add(new Issue { SeriesId = series.Id, AddedTime = DateTime.UtcNow });
        });

        var window = _fixture.Window;
        // Force a reload - the window already loaded Home once before the seed above landed.
        window.FindFirstDescendant(cf => cf.ByAutomationId("HomeRailButton"))!.AsButton().Invoke();

        var card = window.FindFirstDescendant(cf => cf.ByAutomationId("HomeRecentlyAddedList"))!
            .FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button))!;
        card.AsButton().Invoke();

        Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeContinueReadingHeader")));
    }

    [Fact]
    public void SpotlightCard_Click_NavigatesAwayFromHome()
    {
        Seed(context =>
        {
            var series = new Series { Name = "Untouched Series" };
            context.Series.Add(series);
            context.SaveChanges();
            context.Issues.Add(new Issue { SeriesId = series.Id });
        });

        var window = _fixture.Window;
        window.FindFirstDescendant(cf => cf.ByAutomationId("HomeRailButton"))!.AsButton().Invoke();

        var card = window.FindFirstDescendant(cf => cf.ByAutomationId("HomeSpotlightCard"))!;
        card.AsButton().Invoke();

        Assert.Null(window.FindFirstDescendant(cf => cf.ByAutomationId("HomeContinueReadingHeader")));
    }
}
