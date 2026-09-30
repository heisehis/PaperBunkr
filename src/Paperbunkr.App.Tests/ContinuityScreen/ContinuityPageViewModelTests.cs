using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.ContinuityScreen;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests.ContinuityScreen;

/// <summary>
/// <see cref="ContinuityPageViewModel"/> (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md, "Continuity page"). The
/// first half carries over the old screen's continuity tests (members, add/remove through <see cref="ContinuityResolver"/>, notes,
/// order, bulk select, compare, reading list, delete); the rest covers the overview: runs, events in order, attention, Continue, the
/// Custom order and merging.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ContinuityPageViewModelTests : ContinuityScreenTestBase
{
    private static ContinuityPageViewModel Open(int continuityId, ContinuityScreenViewModel? screen = null)
    {
        screen ??= CreateScreen();
        screen.LoadContinuity(continuityId);
        return screen.ContinuityPage;
    }

    [Fact]
    public void Open_ListsTheMemberSeries_InTheirStoredOrder()
    {
        int a = SeedSeries("Aaa");
        int b = SeedSeries("Bbb");
        var page = Open(SeedContinuity("Earth-616", b, a));

        Assert.Equal(new[] { b, a }, page.Members.Select(m => m.SeriesId));
        Assert.Equal("Series · 2", page.SeriesHeader);
    }

    [Fact]
    public void AddAndRemoveSeries_WriteThroughContinuityResolver()
    {
        int avengers = SeedSeries("Avengers");
        int xmen = SeedSeries("X-Men");
        var page = Open(SeedContinuity("Earth-616", avengers));

        page.AddSeriesCommand.Execute(new SeriesSearchResult { SeriesId = xmen, Name = "X-Men" });
        Drain();
        Assert.Equal(2, page.Members.Count);
        using (var c = PaperbunkrDb.CreateContext())
        {
            Assert.Single(ContinuityResolver.GetOtherSeriesSharingContinuity(c, avengers));   // the path the Related tab uses sees it
        }

        page.RemoveSeriesCommand.Execute(page.Members.Single(m => m.SeriesId == xmen));
        Drain();
        Assert.Single(page.Members);
        using var check = PaperbunkrDb.CreateContext();
        Assert.Empty(ContinuityResolver.GetOtherSeriesSharingContinuity(check, avengers));
    }

    [Fact]
    public void OpenSeries_NavigatesToSeriesDetail()
    {
        int seriesId = SeedSeries("Avengers");
        int? navigatedTo = null;
        var page = Open(SeedContinuity("Earth-616", seriesId), CreateScreen(goToSeriesDetail: id => navigatedTo = id));

        page.OpenSeriesCommand.Execute(page.Members.Single());

        Assert.Equal(seriesId, navigatedTo);
    }

    [Fact]
    public void SetNote_PersistsAndSurvivesReload()
    {
        int continuityId = SeedContinuity("Marvel Prime", SeedSeries("Daredevil"));
        var page = Open(continuityId);

        var card = page.Members.Single();
        page.BeginEditNoteCommand.Execute(card);
        Assert.True(card.IsEditingNote);
        card.MembershipNote = "flagship title";
        page.SetNoteCommand.Execute(card);
        Assert.False(card.IsEditingNote);

        Assert.Equal("flagship title", Open(continuityId).Members.Single().MembershipNote);
    }

    [Fact]
    public void CustomOrder_MovingAPoster_ReordersAndPersists()
    {
        int a = SeedSeries("Aaa");
        int b = SeedSeries("Bbb");
        int c = SeedSeries("Ccc");
        int continuityId = SeedContinuity("Ordered", a, b, c);
        var page = Open(continuityId);
        page.UseCustomOrderCommand.Execute(null);

        page.MoveSeriesTo(page.Members[0], 2);                // drag the first poster to the end
        Drain();
        Assert.Equal(new[] { b, c, a }, page.Members.Select(m => m.SeriesId));

        page.MoveSeriesEarlierCommand.Execute(page.Members[2]);    // Ctrl+← on it
        Drain();
        Assert.Equal(new[] { b, a, c }, page.Members.Select(m => m.SeriesId));
        Assert.Equal(new[] { b, a, c }, page.CustomPosters.Select(p => p.Card!.SeriesId));

        Assert.Equal(new[] { b, a, c }, Open(continuityId).Members.Select(m => m.SeriesId));     // the order is stored
        var screen = CreateScreen();
        var again = Open(continuityId, screen);
        again.UseCustomOrderCommand.Execute(null);
        screen.LoadContinuity(SeedContinuity("Other"));
        screen.LoadContinuity(continuityId);
        Assert.True(again.IsCustomOrder);                     // Custom is remembered for the session
    }

    [Fact]
    public void AddSelectedSeries_AndRemoveSelectedSeries_ActOnTheTicked()
    {
        int continuityId = SeedContinuity("Ultimate");
        SeedSeries("Ultimate Spider-Man");
        SeedSeries("Ultimate X-Men");
        var page = Open(continuityId);

        page.SeriesSearchQuery = "Ultimate";
        Assert.Equal(2, page.SeriesSearchResults.Count);
        page.ToggleSeriesSelectionCommand.Execute(page.SeriesSearchResults[0]);
        page.ToggleSeriesSelectionCommand.Execute(page.SeriesSearchResults[1]);
        page.AddSelectedSeriesCommand.Execute(null);
        Assert.Equal(2, page.Members.Count);
        Assert.False(page.AnySeriesSelected);

        page.ToggleMemberSelectionCommand.Execute(page.Members[0]);
        page.RemoveSelectedSeriesCommand.Execute(null);
        Assert.Single(page.Members);
        Assert.False(page.AnyMembersSelected);
    }

    [Fact]
    public void Compare_ShowsWhatIsShared_AndWhatIsOnlyOnEachSide()
    {
        int shared = SeedSeries("Avengers");
        int hereOnly = SeedSeries("Daredevil");
        int thereOnly = SeedSeries("Ultimates");
        int e616 = SeedContinuity("Earth-616", shared, hereOnly);
        SeedContinuity("Ultimate", shared, thereOnly);
        var page = Open(e616);

        Assert.Equal("Ultimate", Assert.Single(page.OverlappingContinuities).Name);
        page.ToggleCompareCommand.Execute(null);                // opens on the first overlapping continuity

        Assert.True(page.HasComparison);
        Assert.Equal("Avengers", Assert.Single(page.SharedSeries).Name);
        Assert.Equal("Daredevil", Assert.Single(page.OnlyHereSeries).Name);
        Assert.Equal("Ultimates", Assert.Single(page.OnlyThereSeries).Name);
    }

    [Fact]
    public void Merge_TakesTwoClicks_ThenFoldsIntoTheOther_AndOpensIt()
    {
        int shared = SeedSeries("Avengers");
        int mine = SeedSeries("Daredevil");
        int source = SeedContinuity("Earth-616", shared, mine);
        int target = SeedContinuity("Marvel", shared);
        var screen = CreateScreen();
        var page = Open(source, screen);
        page.ToggleCompareCommand.Execute(null);

        page.MergeConfirm!.TriggerCommand.Execute(null);
        using (var c = PaperbunkrDb.CreateContext())
        {
            Assert.NotNull(c.Continuities.Find(source));       // the first click only arms it
        }

        page.MergeConfirm!.TriggerCommand.Execute(null);
        Drain();

        using var check = PaperbunkrDb.CreateContext();
        Assert.Null(check.Continuities.Find(source));
        Assert.Equal(target, screen.ActiveContinuityId);
        Assert.Equal(2, page.Members.Count);
    }

    [Fact]
    public void CreateReadingList_BuildsTheList_AndNavigates()
    {
        int seriesId = SeedSeries("Avengers", ("1", 1965));
        int? list = null;
        var page = Open(SeedContinuity("Earth-616", seriesId), CreateScreen(goToReadingList: id => list = id));

        page.CreateReadingListCommand.Execute("publication");

        using var context = PaperbunkrDb.CreateContext();
        var created = context.ReadingLists.Single();
        Assert.Equal("Earth-616 (continuity)", created.Name);
        Assert.Equal(created.Id, list);
        Assert.Equal(ContinuityOrderKind.PublicationOrder, created.ContinuityOrderKind);
        Assert.NotNull(created.ContinuityId);
    }

    /// <summary>Story order is the default (docs/superpowers/specs/2026-09-28-reading-lists-build-from-events-design.md §2): the list reads
    /// like the continuity map - with no events, that is the map's year blocks - and stays linked for Rebuild.</summary>
    [Fact]
    public void CreateReadingList_DefaultsToStoryOrder_LabelledLikeTheMap()
    {
        int seriesId = SeedSeries("Avengers", ("1", 1965), ("2", 1966));
        int? list = null;
        var page = Open(SeedContinuity("Earth-616", seriesId), CreateScreen(goToReadingList: id => list = id));

        page.CreateReadingListCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        var created = context.ReadingLists.Include(r => r.Items).Single();
        Assert.Equal("Earth-616 (story order)", created.Name);
        Assert.Equal(ContinuityOrderKind.StoryOrder, created.ContinuityOrderKind);
        Assert.Equal(created.Id, list);
        Assert.Equal(new[] { "1965", "1966" }, created.Items.OrderBy(i => i.SortOrder).Select(i => i.GroupLabel));
    }

    [Fact]
    public void DeleteContinuity_RemovesIt_AndFallsBackToTheNext()
    {
        int keep = SeedContinuity("Alpha");
        int drop = SeedContinuity("Zeta");
        var screen = CreateScreen();
        var page = Open(drop, screen);

        page.DeleteContinuityCommand.Execute(null);
        Drain();

        using var context = PaperbunkrDb.CreateContext();
        Assert.Null(context.Continuities.Find(drop));
        Assert.Equal(keep, screen.ActiveContinuityId);
        Assert.Equal("Alpha", page.Name);
    }

    // --- The overview ---

    [Fact]
    public void Runs_JoinContinuedSeries_OldestFirst_AndTheRestAreStandalone()
    {
        int older = SeedSeries("Incredible Hulk (1968)", ("1", 1968));
        int newer = SeedSeries("Hulk (2008)", ("1", 2008));
        int alone = SeedSeries("Thor", ("1", 1966));
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.MediaRelations.Add(new MediaRelation { SourceSeriesId = newer, TargetSeriesId = older, RelationType = RelationType.Continuation });
            context.SaveChanges();
        }

        var page = Open(SeedContinuity("Earth-616", newer, alone, older));

        var run = Assert.Single(page.Runs);
        Assert.Equal("Incredible Hulk · 1968–2008 · 2 series", run.Label);
        Assert.Equal(new int?[] { older, newer }, run.Posters.Select(p => p.Card?.SeriesId));
        Assert.True(run.Posters[1].ArrowBefore);
        Assert.Equal(alone, Assert.Single(page.StandalonePosters).Card!.SeriesId);
        Assert.Equal("1966", page.StandalonePosters[0].Years);
    }

    [Fact]
    public void Overview_Stats_EventsInOrder_AndContinue()
    {
        int hulk = SeedSeries("Hulk", ("92", 2006), ("106", 2007), ("300", 2010));
        List<int> issues;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issues = context.Issues.Where(i => i.SeriesId == hulk).OrderBy(i => i.Id).Select(i => i.Id).ToList();
        }

        int planet = SeedEvent("Planet Hulk", new DateTime(2006, 4, 1), new DateTime(2007, 5, 1), issues[0]);
        int war = SeedEvent("World War Hulk", new DateTime(2007, 6, 1), new DateTime(2008, 1, 1), issues[1]);
        using (var context = PaperbunkrDb.CreateContext())
        {
            EventRelationResolver.TryCreate(context, planet, war, RelationType.Prequel);
        }

        MarkRead(issues[0]);
        (int, int)? opened = null;
        var screen = CreateScreen();
        screen.GoToReaderInEvent = (issue, anchor) => opened = (issue, anchor);
        var page = Open(SeedContinuity("Earth-616", hulk), screen);

        Assert.Equal("2006–2010 · 1 series · 3 issues · 2 events · 33% read", page.StatsLine);
        Assert.Equal(new[] { "Planet Hulk", "World War Hulk" }, page.EventsInOrder.Select(e => e.Name));
        Assert.Equal("↓ prequel of", page.EventsInOrder[0].LinkBelow);
        Assert.Equal("1 issue · 1 read", page.EventsInOrder[0].CountLabel);
        Assert.Equal("Continue · Hulk #106", page.ContinueLabel);

        page.ContinueReadingCommand.Execute(null);
        Assert.Equal((issues[1], war), opened);

        page.OpenEventMapCommand.Execute(page.EventsInOrder[1]);
        Assert.True(screen.IsContinuitySelected);             // deferred: the row is on the page this swaps out
        Drain();
        Assert.Equal(war, screen.ActiveEventId);
        Assert.True(screen.IsMapView);
    }

    [Fact]
    public void Attention_OutsideSeries_CanBeAdded()
    {
        int hulk = SeedSeries("Hulk", ("1", 2007));
        int thor = SeedSeries("Thor", ("5", 2007));
        using (var context = PaperbunkrDb.CreateContext())
        {
            SeedEvent("Crossover", null, null, context.Issues.Single(i => i.SeriesId == hulk).Id, context.Issues.Single(i => i.SeriesId == thor).Id);
        }

        var page = Open(SeedContinuity("Earth-616", hulk));

        Assert.Equal("1 series from its events isn't in this continuity", page.OutsideAttention!.Text);
        page.AddOutsideSeriesCommand.Execute(page.OutsideSeries.Single());
        Drain();

        Assert.Equal(2, page.Members.Count);
        Assert.Null(page.OutsideAttention);
        Assert.False(page.HasAttention);
    }

    [Fact]
    public void Attention_Duplicates_OpenSuggestionsAndChecks()
    {
        int hulk = SeedSeries("Hulk", ("92", 2006), ("93", 2006));
        using (var context = PaperbunkrDb.CreateContext())
        {
            var ids = context.Issues.Select(i => i.Id).ToArray();
            int a = SeedEvent("Hulk: Planet Hulk", null, null, ids);
            SeedEvent("Planet Hulk", null, null, ids);
            context.StoryEvents.Find(a)!.Origin = StoryEventOrigin.Provider;
            context.SaveChanges();
        }

        var screen = CreateScreen();
        var page = Open(SeedContinuity("Earth-616", hulk), screen);

        Assert.Equal("1 possible duplicate event", page.DuplicatesAttention!.Text);
        page.OpenDuplicatesCommand.Execute(null);

        Assert.True(screen.IsSuggestionsOpen);
        Assert.Single(screen.Suggestions.PossibleDuplicates);
    }

    [Fact]
    public void Continue_IsHidden_WhenEverythingIsRead_AndFollowsPublicationOrderWithoutEvents()
    {
        int hulk = SeedSeries("Hulk", ("1", 2001), ("2", 2002));
        int continuityId = SeedContinuity("Earth-616", hulk);
        var page = Open(continuityId);
        Assert.Equal("Continue · Hulk #1", page.ContinueLabel);
        Assert.False(page.HasEvents);

        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (int id in context.Issues.Select(i => i.Id).ToList())
            {
                MarkRead(id);
            }
        }

        page = Open(continuityId);
        Assert.False(page.HasContinue);
    }
}
