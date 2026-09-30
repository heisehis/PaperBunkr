using Paperbunkr.App.Services;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.ContinuityScreen;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests.ContinuityScreen;

/// <summary>
/// <see cref="EventPageViewModel"/> (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md, "Event page"). The first half
/// carries over the old Story Events screen's event tests (members, search, roles, connected events, issue suggestions, the event chain,
/// bulk selection) with the same intent; row-button reloads now wait one dispatcher tick, so those tests drain it. The second half covers
/// what's new: filters, the Follows / Followed by strip, attention, Continue and the chips.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class EventPageViewModelTests : ContinuityScreenTestBase
{
    private static int SeedSignalIssue(string seriesName, string number, string format, int year) => SeedIssue(seriesName, number, format, year);

    [Fact]
    public void Search_MatchesBySeriesName_CaseInsensitive()
    {
        SeedIssue("Green Lantern", "13");
        var page = OpenEvent(SeedEvent("Blackest Night"));

        page.SearchQuery = "green";

        Assert.Contains("Green Lantern", Assert.Single(page.SearchResults).DisplayLabel);
    }

    [Fact]
    public void AddIssue_WithRole_AppearsInOrderedMemberList()
    {
        int issueId = SeedIssue("Green Lantern", "13");
        var page = OpenEvent(SeedEvent("Blackest Night"));
        page.SelectedRole = EventMembershipRole.Prologue;
        page.SearchQuery = "Green Lantern";

        page.AddIssueCommand.Execute(page.SearchResults.Single());
        Drain();

        var member = Assert.Single(page.Members);
        Assert.Equal(issueId, member.Member.IssueId);
        Assert.Equal(EventMembershipRole.Prologue, member.SelectedRole);
        Assert.False(page.HasNoMembers);
    }

    [Fact]
    public void MoveDown_SwapsOrder_AndRemoveMember_ClearsIt()
    {
        int a = SeedIssue("Green Lantern", "13");
        int b = SeedIssue("Green Lantern Corps", "13");
        var page = OpenEvent(SeedEvent("Blackest Night", null, null, a, b));

        page.Members[0].MoveDownCommand.Execute(null);
        Drain();
        Assert.Equal(new[] { b, a }, page.Members.Select(m => m.Member.IssueId));

        page.Members[0].RemoveCommand.Execute(null);
        Drain();
        Assert.Equal(a, Assert.Single(page.Members).Member.IssueId);
    }

    [Fact]
    public void ChangingRoleOnRow_Persists()
    {
        int eventId = SeedEvent("Blackest Night", null, null, SeedIssue("Green Lantern", "13"));
        var page = OpenEvent(eventId);

        page.Members.Single().SelectedRole = EventMembershipRole.Aftermath;
        Drain();

        page.Load(eventId);
        Assert.Equal(EventMembershipRole.Aftermath, Assert.Single(page.Members).SelectedRole);
    }

    [Fact]
    public void ConnectEvent_ShowsCrossoversInRelatedEvents_AndDirectionalLinksInTheStrip()
    {
        int a = SeedEvent("Event A");
        int b = SeedEvent("Event B");
        int c = SeedEvent("Event C");
        var screen = CreateScreen();
        var page = OpenEvent(a, screen);

        page.SelectedRelationType = RelationType.Crossover;
        page.ConnectEventCommand.Execute(new StoryEventSearchResult { StoryEventId = b, Name = "Event B" });
        page.SelectedRelationType = RelationType.Prequel;
        page.ConnectEventCommand.Execute(new StoryEventSearchResult { StoryEventId = c, Name = "Event C" });

        Assert.Equal("Event B", Assert.Single(page.ConnectedEvents).Name);          // the crossover
        Assert.Equal("Event C", Assert.Single(page.FollowedBy).Name);               // A is the prequel of C
        Assert.Empty(page.Follows);

        screen.LoadEvent(c);
        Assert.Equal("Event A", Assert.Single(page.Follows).Name);
        Assert.Empty(page.ConnectedEvents);
    }

    [Fact]
    public void ConnectEvent_ShowsInvertedLabelsFromEachSide()
    {
        int prequelId = SeedEvent("Secret Wars (1984)");
        int crossId = SeedEvent("Secret Wars (2015)");
        var screen = CreateScreen();
        var page = OpenEvent(prequelId, screen);
        page.SelectedRelationType = RelationType.Prequel;
        page.ConnectEventCommand.Execute(new StoryEventSearchResult { StoryEventId = crossId, Name = "Secret Wars (2015)" });

        Assert.Equal("Prequel", Assert.Single(page.AllConnections).RelationLabel);
        screen.LoadEvent(crossId);
        Assert.Equal("Sequel", Assert.Single(page.AllConnections).RelationLabel);
    }

    [Fact]
    public void RemoveConnectedEvent_ClearsBothSides_AndUnlinkNeighbour_RemovesAnOrderLink()
    {
        int a = SeedEvent("Event A");
        int b = SeedEvent("Event B");
        int c = SeedEvent("Event C");
        var screen = CreateScreen();
        var page = OpenEvent(a, screen);
        page.SelectedRelationType = RelationType.Crossover;
        page.ConnectEventCommand.Execute(new StoryEventSearchResult { StoryEventId = b, Name = "Event B" });
        page.SelectedRelationType = RelationType.Sequel;
        page.ConnectEventCommand.Execute(new StoryEventSearchResult { StoryEventId = c, Name = "Event C" });

        page.RemoveConnectedEventCommand.Execute(page.ConnectedEvents.Single());
        Drain();
        Assert.True(page.HasNoConnectedEvents);

        page.UnlinkNeighbourCommand.Execute(page.Follows.Single());
        Drain();
        Assert.False(page.HasNeighbours);

        screen.LoadEvent(b);
        Assert.Empty(page.AllConnections);
    }

    [Fact]
    public void OpenConnectedEvent_AndOpenFamilyEvent_OpenThatEvent()
    {
        int a = SeedEvent("Event A");
        int b = SeedEvent("Event B");
        int d = SeedEvent("Event D");
        var screen = CreateScreen();
        var page = OpenEvent(a, screen);
        page.SelectedRelationType = RelationType.Crossover;
        page.ConnectEventCommand.Execute(new StoryEventSearchResult { StoryEventId = b, Name = "Event B" });
        screen.LoadEvent(b);
        page.ConnectEventCommand.Execute(new StoryEventSearchResult { StoryEventId = d, Name = "Event D" });
        screen.LoadEvent(a);

        Assert.True(page.HasEventChain);                  // A + B (direct) + D (transitive)
        var dNode = page.EventFamily.Single(n => n.Name == "Event D");
        Assert.Equal(2, dNode.Depth);
        page.OpenFamilyEventCommand.Execute(dNode);
        Assert.Equal("Event D", page.Name);
        Assert.Equal(d, screen.ActiveEventId);

        page.OpenConnectedEventCommand.Execute(page.ConnectedEvents.Single());
        Assert.Equal("Event B", page.Name);
    }

    [Fact]
    public void SearchEvents_ExcludesThisEvent_MatchesByNameCaseInsensitive()
    {
        var page = OpenEvent(SeedEvent("Rise of the Third Army"));
        SeedEvent("Rise of the Third Sun");

        page.ConnectEventQuery = "rise of the third";

        Assert.Equal("Rise of the Third Sun", Assert.Single(page.EventSearchResults).Name);
    }

    [Fact]
    public void IssueSuggestion_AddMovesItIntoTheList_WithTheEditedRole()
    {
        int issueId = SeedSignalIssue("Avengers", "1", "Annual", 2015);
        var page = OpenEvent(SeedEvent("Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1)));
        var suggestion = Assert.Single(page.SuggestedIssues);
        suggestion.SelectedRole = EventMembershipRole.TieIn;

        suggestion.AddCommand.Execute(null);
        Drain();

        Assert.Empty(page.SuggestedIssues);
        var member = Assert.Single(page.Members);
        Assert.Equal(issueId, member.Member.IssueId);
        Assert.Equal(EventMembershipRole.TieIn, member.SelectedRole);
    }

    [Fact]
    public void IssueSuggestion_Dismiss_Persists_AndCanBeRestored()
    {
        SeedSignalIssue("Avengers", "1", "Annual", 2015);
        int eventId = SeedEvent("Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1));
        var page = OpenEvent(eventId);

        page.SuggestedIssues.Single().DismissCommand.Execute(null);
        Drain();
        Assert.True(page.HasNoSuggestions);
        Assert.Empty(page.Members);

        page.Load(eventId);
        Assert.Empty(page.SuggestedIssues);
        page.RestoreDismissedCommand.Execute(page.DismissedSuggestions.Single());
        Drain();

        Assert.Single(page.SuggestedIssues);
        Assert.True(page.HasNoDismissed);
    }

    [Fact]
    public void SuggestedRole_PreFilledFromCatalog_WhenSupplied()
    {
        SeedSignalIssue("Avengers", "1", "Prologue", 2015);
        var page = OpenEvent(SeedEvent("Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1)));

        Assert.Equal(EventMembershipRole.Prologue, Assert.Single(page.SuggestedIssues).SelectedRole);
    }

    [Fact]
    public void ConnectionSuggestion_SurfacesLikelyPair_AndConnectsIt()
    {
        int a = SeedEvent("Secret Wars", new DateTime(2015, 5, 1));
        SeedEvent("Secret Empire", new DateTime(2017, 4, 1));
        var page = OpenEvent(a);

        var suggestion = Assert.Single(page.ConnectionSuggestions);
        Assert.Equal("Secret Empire", suggestion.Name);
        page.SelectedRelationType = RelationType.Related;
        page.ConnectSuggestedEventCommand.Execute(suggestion);
        Drain();

        Assert.Single(page.AllConnections);
        Assert.Equal("Secret Empire", Assert.Single(page.FollowedBy).Name);     // a typed Sequel: it shows in the strip, not Related events
        Assert.True(page.HasNoConnectionSuggestions);
    }

    [Fact]
    public void TypedSuggestion_AcceptCreatesExactlyThatRelation()
    {
        int old = SeedEvent("Secret Wars (1984)", null, null, SeedIssue("Secret Wars", "1", year: 1984));
        int later = SeedEvent("Secret Wars (2015)", null, null, SeedIssue("Secret Wars (2015)", "1", year: 2015));
        var page = OpenEvent(later);

        var card = page.ConnectionSuggestions.First(c => c.IsTyped);
        Assert.Equal("Looks like the sequel of Secret Wars (1984)", card.Headline);
        page.ConnectSuggestedEventCommand.Execute(card);

        using var check = PaperbunkrDb.CreateContext();
        var relation = Assert.Single(check.EventRelations);
        Assert.Equal((later, old, RelationType.Sequel), (relation.SourceEventId, relation.TargetEventId, relation.RelationType));
    }

    [Fact]
    public async Task TheSweepsInferredContinuation_ShowsInTheStrip_AndUnlinkingDismissesThePair()
    {
        int hulk = SeedSeries("Hulk", ("105", 2007), ("106", 2007));
        int planet, war;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var issues = context.Issues.Where(i => i.SeriesId == hulk).OrderBy(i => i.Id).ToList();
            issues[0].Month = 5;
            issues[1].Month = 6;
            context.SaveChanges();
            planet = SeedEvent("Planet Hulk", null, null, issues[0].Id);
            war = SeedEvent("World War Hulk", null, null, issues[1].Id);
            foreach (var e in context.StoryEvents)
            {
                e.Origin = StoryEventOrigin.Provider;
            }

            context.SaveChanges();
        }

        await EventConnectorSweep.RunAsync(() => PaperbunkrDb.CreateContext(), null, 40, CancellationToken.None);
        var page = OpenEvent(war);

        var before = Assert.Single(page.Follows);
        Assert.Equal("Planet Hulk", before.Name);
        Assert.Equal("inferred", page.AllConnections.Single().SourceLabel);

        page.UnlinkNeighbourCommand.Execute(before);
        Drain();

        using var check = PaperbunkrDb.CreateContext();
        Assert.Empty(check.EventRelations);
        var dismissal = Assert.Single(check.EventRelationDismissals);
        Assert.Equal((Math.Min(planet, war), Math.Max(planet, war)), (dismissal.LowerEventId, dismissal.HigherEventId));
    }

    [Fact]
    public void SearchQuery_LiveSearches_AndToggleAddIssues_ClearsOnClose()
    {
        SeedSignalIssue("X-Men", "1", "Regular", 1996);
        var page = OpenEvent(SeedEvent("Onslaught"));
        Assert.False(page.IsAddingIssues);

        page.ToggleAddIssuesCommand.Execute(null);
        page.SearchQuery = "X-Men";
        Assert.True(page.IsAddingIssues);
        Assert.Single(page.SearchResults);

        page.ToggleAddIssuesCommand.Execute(null);
        Assert.False(page.IsAddingIssues);
        Assert.Empty(page.SearchResults);
        Assert.Equal(string.Empty, page.SearchQuery);
    }

    [Fact]
    public void AddSelectedMembers_AddsEveryTicked_WithBulkRole()
    {
        int eventId = SeedEvent("Onslaught");
        SeedSignalIssue("X-Men", "1", "Regular", 1996);
        SeedSignalIssue("X-Men", "2", "Regular", 1996);
        var page = OpenEvent(eventId);
        page.SearchQuery = "X-Men";
        page.BulkRole = EventMembershipRoleOption.All.First(o => o.Role == EventMembershipRole.TieIn);
        page.ToggleSearchSelectionCommand.Execute(page.SearchResults[0]);
        page.ToggleSearchSelectionCommand.Execute(page.SearchResults[1]);

        page.AddSelectedMembersCommand.Execute(null);

        Assert.Equal(2, page.Members.Count);
        using var context = PaperbunkrDb.CreateContext();
        Assert.All(context.EventMemberships.Where(m => m.StoryEventId == eventId), m => Assert.Equal(EventMembershipRole.TieIn, m.Role));
        Assert.False(page.AnySearchSelected);
    }

    [Fact]
    public void RemoveSelectedMembers_RemovesTicked_AndPositionsAre1Based()
    {
        var page = OpenEvent(SeedEvent("Fear Itself", null, null, SeedSignalIssue("Thor", "1", "Regular", 2011), SeedSignalIssue("Thor", "2", "Regular", 2011)));
        Assert.Equal(new[] { 1, 2 }, page.Members.Select(m => m.Position));

        page.ToggleMemberSelectionCommand.Execute(page.Members[0]);
        page.RemoveSelectedMembersCommand.Execute(null);

        Assert.Single(page.Members);
        Assert.False(page.AnyMembersSelected);
    }

    [Fact]
    public void RoleAndRelationTextProjections_RoundTripThroughTheirObjectProperties()
    {
        var page = OpenEvent(SeedEvent("Any"));

        var role = EventPageViewModel.RoleOptions.First(o => o.Role != page.SelectedRoleOption.Role);
        page.SelectedRoleOptionText = role.Label;
        Assert.Equal(role.Role, page.SelectedRole);
        page.SelectedRoleOptionText = "not a role";
        Assert.Equal(role.Role, page.SelectedRole);

        page.BulkRoleText = role.Label;
        Assert.Equal(role.Role, page.BulkRole!.Role);

        var relation = EventPageViewModel.RelationTypeOptions.First(o => o.Type != page.SelectedRelationTypeOption.Type);
        page.SelectedRelationTypeOptionText = relation.Label;
        Assert.Equal(relation.Type, page.SelectedRelationType);

        Assert.Equal(EventPageViewModel.RoleOptions.Select(o => o.Label), EventPageViewModel.RoleNames);
        Assert.Equal(EventPageViewModel.RelationTypeOptions.Select(o => o.Label), EventPageViewModel.RelationTypeNames);
    }

    // --- New with the redesign ---

    [Fact]
    public void Filters_HideRowsButKeepReadingOrder_WithCountsOnTheChips()
    {
        int a = SeedIssue("X-Men", "1");
        int b = SeedIssue("X-Factor", "1");
        int c = SeedIssue("Excalibur", "1");
        int eventId = SeedEvent("Inferno", null, null, a, b, c);
        using (var context = PaperbunkrDb.CreateContext())
        {
            var members = context.EventMemberships.OrderBy(m => m.Position).ToList();
            members[1].Role = EventMembershipRole.TieIn;
            members[2].Role = EventMembershipRole.Optional;
            context.SaveChanges();
        }

        MarkRead(a);
        var page = OpenEvent(eventId);

        Assert.Equal("All 3", page.AllFilterLabel);
        Assert.Equal("Core 1", page.CoreFilterLabel);
        Assert.Equal("Unread 2", page.UnreadFilterLabel);

        page.SetFilterCommand.Execute(EventMemberFilter.HideOptional);
        Assert.Equal(new[] { a, b }, page.VisibleMembers.Select(m => m.Member.IssueId));

        page.SetFilterCommand.Execute(EventMemberFilter.Unread);
        Assert.Equal(new[] { b, c }, page.VisibleMembers.Select(m => m.Member.IssueId));
        Assert.True(page.VisibleMembers[0].Position < page.VisibleMembers[1].Position);
        Assert.Equal(3, page.Members.Count);
    }

    [Fact]
    public void Attention_RoleSuggestions_OpenTheNeedsReviewFilter()
    {
        int eventId = SeedEvent("Big Event", null, null, SeedIssue("Spider-Man", "1", format: "Prologue"), SeedIssue("Spider-Man", "2"));
        var page = OpenEvent(eventId);
        page.DetectRolesCommand.Execute(null);
        Drain();

        var item = page.Attention.Single(a => a.Kind == AttentionKind.RoleSuggestions);
        Assert.Equal("1 role suggestion to review", item.Text);

        page.OpenAttentionCommand.Execute(item);

        Assert.True(page.IsNeedsReviewFilter);
        Assert.Single(page.VisibleMembers);
    }

    [Fact]
    public void Attention_IssueSuggestions_ExpandTheirPanel()
    {
        SeedSignalIssue("Avengers", "1", "Annual", 2015);
        var page = OpenEvent(SeedEvent("Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1)));
        string? scrolledTo = null;
        page.ScrollToPanelRequested += panel => scrolledTo = panel;

        page.OpenAttentionCommand.Execute(page.Attention.Single(a => a.Kind == AttentionKind.IssueSuggestions));

        Assert.True(page.IssueSuggestionsExpanded);
        Assert.Equal("issues", scrolledTo);
    }

    [Fact]
    public void Overview_StatsChipsAndContinue_FollowTheEvent()
    {
        int one = SeedIssue("World War Hulk", "1", year: 2007);
        int two = SeedIssue("Incredible Hulk", "106", year: 2007);
        int eventId = SeedEvent("World War Hulk", new DateTime(2007, 6, 1), new DateTime(2008, 1, 1), one, two);
        int continuityId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            continuityId = SeedContinuity("Earth-616", context.Issues.Find(one)!.SeriesId);
            context.StoryEvents.Find(eventId)!.ComicVineArcId = "123";
            context.SaveChanges();
        }

        MarkRead(one);
        (int, int)? opened = null;
        var screen = CreateScreen();
        screen.GoToReaderInEvent = (issue, anchor) => opened = (issue, anchor);
        var page = OpenEvent(eventId, screen);

        Assert.Equal("2007–2008 · 2 issues (2 core · 0 tie-ins) · 2 series · 50% read", page.StatsLine);
        Assert.Equal("ComicVine", page.SourceLabel);
        Assert.Equal("in Earth-616", page.ContinuityChipLabel);
        Assert.Equal("Continue · Incredible Hulk #106", page.ContinueLabel);

        page.ContinueReadingCommand.Execute(null);
        Assert.Equal((two, eventId), opened);

        page.OpenContinuityCommand.Execute(page.FirstContinuity);
        Drain();
        Assert.Equal(continuityId, screen.ActiveContinuityId);
    }

    [Fact]
    public void Continue_IsHidden_OnceEverythingIsRead()
    {
        int one = SeedIssue("Hulk", "1");
        MarkRead(one);

        var page = OpenEvent(SeedEvent("Done", null, null, one));

        Assert.False(page.HasContinue);
    }

    [Fact]
    public void DeleteEvent_RemovesIt()
    {
        int eventId = SeedEvent("Inferno");
        var page = OpenEvent(eventId);

        page.DeleteEventCommand.Execute(null);
        Drain();

        using var context = PaperbunkrDb.CreateContext();
        Assert.Null(context.StoryEvents.Find(eventId));
    }
}
