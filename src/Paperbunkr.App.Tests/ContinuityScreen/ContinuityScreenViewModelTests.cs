using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.ContinuityScreen;

/// <summary>
/// The Continuity screen's shell and sidebar (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md): navigation between
/// continuities, events and Suggestions &amp; checks, the remembered sidebar tab, row counts and deletes, the Overview | Map | Timeline
/// switch, and the Event Map carried over from the old screen's map tests (docs/superpowers/specs/2026-09-25-event-map-design.md §4).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ContinuityScreenViewModelTests : ContinuityScreenTestBase
{
    private static (int EventId, int SeriesId, int ContinuityId) SeedCrisis()
    {
        int seriesId = SeedSeries("Crisis", ("1", 1985), ("2", 1985));
        int[] issues;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issues = context.Issues.Where(i => i.SeriesId == seriesId).OrderBy(i => i.Id).Select(i => i.Id).ToArray();
        }

        return (SeedEvent("Crisis", null, null, issues), seriesId, SeedContinuity("Earth-1", seriesId));
    }

    [Fact]
    public void NothingYet_ShowsTheEmptyPrompt()
    {
        var vm = CreateScreen();
        vm.EnsureEventLoaded();

        Assert.True(vm.HasNothing);
        Assert.True(vm.ShowEmptyPrompt);
        Assert.Equal(ContinuityScreenPage.None, vm.Page);
    }

    [Fact]
    public void Sidebar_CountsIssues_AndShowsTheEventsStartYear()
    {
        var (eventId, _, continuityId) = SeedCrisis();
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.StoryEvents.Find(eventId)!.StartDate = new DateTime(1985, 4, 1);
            context.SaveChanges();
        }

        var vm = CreateScreen();

        var continuity = Assert.Single(vm.Sidebar.Continuities);
        Assert.Equal((continuityId, 1, 2), (continuity.Id, continuity.SeriesCount, continuity.IssueCount));
        Assert.NotNull(continuity.DeleteConfirm);
        Assert.Equal("1985 · 2", Assert.Single(vm.Sidebar.Events).MetaLabel);
    }

    [Fact]
    public void SidebarTab_OpensOnContinuitiesFirst_IsRemembered_AndFollowsWhatOpens()
    {
        var (eventId, _, continuityId) = SeedCrisis();
        var vm = CreateScreen();
        Assert.True(vm.Sidebar.IsContinuitiesTab);

        vm.EnsureEventLoaded();
        Assert.Equal(continuityId, vm.ActiveContinuityId);        // first visit: the first item of the remembered tab

        vm.Sidebar.ShowEventsTabCommand.Execute(null);
        Assert.True(CreateScreen().Sidebar.IsEventsTab);          // remembered across screens (AppSettings.ContinuitySidebarTab)

        vm.LoadContinuity(continuityId);
        Assert.True(vm.Sidebar.IsContinuitiesTab);                // opening a continuity flips it back
        vm.LoadEvent(eventId);
        Assert.True(vm.Sidebar.IsEventsTab);
    }

    [Fact]
    public void SelectEvent_ThenSelectContinuity_SwapsWhatIsOpen_AndMarksTheRowActive()
    {
        var (eventId, _, continuityId) = SeedCrisis();
        var vm = CreateScreen();

        vm.SelectEventCommand.Execute(vm.Sidebar.Events.Single());
        Assert.True(vm.IsEventSelected);
        Assert.Equal(eventId, vm.ActiveEventId);
        Assert.Null(vm.ActiveContinuityId);
        Assert.True(vm.Sidebar.Events.Single().IsActive);

        vm.SelectContinuityCommand.Execute(vm.Sidebar.Continuities.Single());
        Assert.True(vm.IsContinuitySelected);
        Assert.Equal(continuityId, vm.ActiveContinuityId);
        Assert.Null(vm.ActiveEventId);
        Assert.True(vm.Sidebar.Continuities.Single().IsActive);
    }

    [Fact]
    public void DeleteConfirm_OnASidebarRow_TakesTwoClicks_AndClearsTheScreenWhenItWasTheLast()
    {
        int eventId = SeedEvent("Inferno");
        var vm = CreateScreen();
        vm.LoadEvent(eventId);
        var row = vm.Sidebar.Events.Single();

        row.DeleteConfirm.TriggerCommand.Execute(null);
        Assert.Single(vm.Sidebar.Events);

        row.DeleteConfirm.TriggerCommand.Execute(null);
        Drain();
        Assert.Empty(vm.Sidebar.Events);
        Assert.True(vm.ShowEmptyPrompt);
        Assert.False(vm.IsEventSelected);
    }

    [Fact]
    public void Suggestions_OpenInTheMainArea_AndClearTheSidebarSelection()
    {
        var (eventId, _, _) = SeedCrisis();
        var vm = CreateScreen();
        vm.LoadEvent(eventId);

        vm.OpenSuggestionsCommand.Execute(null);

        Assert.True(vm.IsSuggestionsOpen);
        Assert.True(vm.Sidebar.IsSuggestionsActive);
        Assert.Null(vm.ActiveEventId);
        Assert.All(vm.Sidebar.Events, e => Assert.False(e.IsActive));
    }

    [Fact]
    public void Timeline_ForAnEvent_ThenReselecting_GoesBackToOverview()
    {
        int issueId = SeedIssue("X-Men", "1", "Regular", 1995);
        int eventId = SeedEvent("Age of Apocalypse", null, null, issueId);
        var vm = CreateScreen();
        vm.SelectEventCommand.Execute(vm.Sidebar.Events.Single());

        vm.SetDetailViewCommand.Execute(EventsDetailView.Timeline);
        Assert.True(vm.IsTimelineView);
        Assert.NotEmpty(vm.Timeline.Sections);

        vm.SelectEventCommand.Execute(vm.Sidebar.Events.Single());
        Assert.True(vm.IsOverviewView);
        Assert.Equal(eventId, vm.ActiveEventId);
    }

    // --- Event Map (ported from the old screen's EventsScreenMapTests and ContinuityMapViewModelTests) ---

    [Fact]
    public async Task Map_LoadsLazily_OnlyWhenChosen()
    {
        var (eventId, _, _) = SeedCrisis();
        var vm = CreateScreen();
        vm.LoadEvent(eventId);
        Assert.False(vm.IsMapCreated);
        Assert.Null(vm.ActiveMap);

        vm.SetDetailViewCommand.Execute(EventsDetailView.Map);
        await vm.MapLoadTask;

        Assert.True(vm.ShowEventMap);
        Assert.False(vm.ShowDetailScroller);
        Assert.Same(vm.Map, vm.ActiveMap);
        Assert.Equal(2, vm.Map.Cards.Count);
    }

    [Fact]
    public void Map_NeedsSomethingOpen()
    {
        SeedCrisis();
        var vm = CreateScreen();

        vm.SetDetailViewCommand.Execute(EventsDetailView.Map);

        Assert.Equal(EventsDetailView.Primary, vm.DetailView);
        Assert.False(vm.ShowEventMap);
        Assert.False(vm.IsMapCreated);
    }

    [Fact]
    public async Task SwitchingEvents_WhileMapShows_KeepsMapAndReloadsIt()
    {
        var (eventId, seriesId, _) = SeedCrisis();
        int other;
        using (var context = PaperbunkrDb.CreateContext())
        {
            other = SeedEvent("Other", null, null, context.Issues.First(i => i.SeriesId == seriesId).Id);
        }

        var vm = CreateScreen();
        vm.LoadEvent(eventId);
        vm.SetDetailViewCommand.Execute(EventsDetailView.Map);
        await vm.MapLoadTask;

        vm.SelectEventCommand.Execute(vm.Sidebar.Events.Single(e => e.Id == other));
        await vm.MapLoadTask;

        Assert.True(vm.IsMapView);
        Assert.Equal(other, vm.Map.StoryEventId);
        Assert.Single(vm.Map.Cards);
    }

    [Fact]
    public async Task Map_WorksForContinuities()
    {
        var (_, _, continuityId) = SeedCrisis();
        var vm = CreateScreen();
        vm.LoadContinuity(continuityId);

        vm.SetDetailViewCommand.Execute(EventsDetailView.Map);
        await vm.MapLoadTask;

        Assert.True(vm.Map.IsContinuityScope);
        Assert.Equal(new[] { "Crisis" }, vm.Map.Layout.Blocks.Select(b => b.Label));
    }

    /// <summary>Ported from ContinuityMapViewModelTests.Screen_MapToggle_WorksForContinuities: loose issues before the first event, then
    /// the events in order.</summary>
    [Fact]
    public async Task Map_ForAContinuity_PlacesLooseIssuesBetweenEvents()
    {
        int hulk = SeedSeries("Hulk", ("105", 2007), ("106", 2007), ("1", 1990));
        int[] issues;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var rows = context.Issues.Where(i => i.SeriesId == hulk).OrderBy(i => i.Id).ToList();
            rows[0].Month = 5;
            rows[1].Month = 6;
            rows[2].Month = 1;
            context.SaveChanges();
            issues = rows.Select(i => i.Id).ToArray();
        }

        SeedEvent("Planet Hulk", null, null, issues[0]);
        SeedEvent("World War Hulk", null, null, issues[1]);
        var vm = CreateScreen();
        vm.LoadContinuity(SeedContinuity("Earth-616", hulk));

        vm.SetDetailViewCommand.Execute(EventsDetailView.Map);
        await vm.MapLoadTask;

        Assert.Equal(new[] { "Between events · 1990", "Planet Hulk", "World War Hulk" }, vm.Map.Layout.Blocks.Select(b => b.Label));
    }

    [Fact]
    public async Task OpenReaderFromTheMap_PassesTheEvent_AndReturningReselectsTheReadersIssue()
    {
        var (eventId, _, _) = SeedCrisis();
        (int IssueId, int EventId)? opened = null;
        var vm = CreateScreen();
        vm.GoToReaderInEvent = (issueId, storyEventId) => opened = (issueId, storyEventId);
        vm.LoadEvent(eventId);
        vm.SetDetailViewCommand.Execute(EventsDetailView.Map);
        await vm.MapLoadTask;
        vm.Map.Select(0);

        vm.Map.OpenReaderCommand.Execute(null);
        Assert.Equal((vm.Map.Cards[0].IssueId, eventId), opened);

        int readerIssue = vm.Map.Cards[1].IssueId;
        vm.ReaderIssueId = () => readerIssue;
        vm.EnsureEventLoaded();                          // what GoEvents does on the way back
        await vm.MapLoadTask;

        Assert.Equal(1, vm.Map.SelectedIndex);
    }

    [Fact]
    public async Task MapEmptyState_GoesBackToOverviewWithAddIssuesOpen()
    {
        int eventId = SeedEvent("Empty");
        var vm = CreateScreen();
        vm.LoadEvent(eventId);
        vm.SetDetailViewCommand.Execute(EventsDetailView.Map);
        await vm.MapLoadTask;

        vm.Map.AddIssuesCommand.Execute(null);

        Assert.True(vm.IsOverviewView);
        Assert.True(vm.EventPage.IsAddingIssues);
    }
}
