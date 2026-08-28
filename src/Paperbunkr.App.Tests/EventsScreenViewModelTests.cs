using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="EventsScreenViewModel"/> (docs/superpowers/specs/2026-08-17-metadata-model-
/// phase4b-story-events-design.md). Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/>
/// to a temp SQLite file, same pattern as <see cref="SmartScreenViewModelTests"/> -
/// <see cref="EventsScreenViewModel"/> has no injected context-factory seam, matching
/// <see cref="ReadingScreenViewModel"/>'s own established shape. Joins
/// <see cref="AvaloniaTestCollection"/> - not for Avalonia/Skia itself (this VM touches neither),
/// but because that collection is this suite's established mechanism for serializing every test
/// class that mutates the shared static <see cref="PaperbunkrDbContext.DatabasePathOverride"/>;
/// without it, this class raced against others doing the same and flaked on full-suite runs.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class EventsScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public EventsScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_events_vm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
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

    private static int SeedIssue(string seriesName, string number)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();

        var issue = new Issue { SeriesId = series.Id, Number = number };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static int SeedEvent(string name, DateTime? start = null, DateTime? end = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var storyEvent = new StoryEvent { Name = name, StartDate = start, EndDate = end, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        return storyEvent.Id;
    }

    private static int SeedSignalIssue(string seriesName, string number, string format, int year)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = number, Format = format, Year = year };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void NoEvents_HasNoEventsTrue()
    {
        var vm = new EventsScreenViewModel();

        Assert.True(vm.HasNoEvents);
    }

    [Fact]
    public void CreateNew_AddsEventToSidebar_LoadsIt()
    {
        var vm = new EventsScreenViewModel();

        vm.CreateNewCommand.Execute(null);

        Assert.False(vm.HasNoEvents);
        Assert.Equal("New Story Event", vm.EventName);
        Assert.Single(vm.Events);
        Assert.True(vm.Events[0].IsActive);
    }

    [Fact]
    public void Search_MatchesBySeriesName_CaseInsensitive()
    {
        SeedIssue("Green Lantern", "13");
        var vm = new EventsScreenViewModel();
        vm.CreateNewCommand.Execute(null);

        vm.SearchQuery = "green";
        vm.SearchCommand.Execute(null);

        var result = Assert.Single(vm.SearchResults);
        Assert.Contains("Green Lantern", result.DisplayLabel);
    }

    [Fact]
    public void AddIssue_WithRole_AppearsInOrderedMemberList()
    {
        int issueId = SeedIssue("Green Lantern", "13");
        var vm = new EventsScreenViewModel();
        vm.CreateNewCommand.Execute(null);
        vm.SelectedRole = EventMembershipRole.Prologue;
        vm.SearchQuery = "Green Lantern";
        vm.SearchCommand.Execute(null);
        var result = Assert.Single(vm.SearchResults);

        vm.AddIssueCommand.Execute(result);

        var member = Assert.Single(vm.Members);
        Assert.Equal(issueId, member.Member.IssueId);
        Assert.Equal(EventMembershipRole.Prologue, member.SelectedRole);
        Assert.Equal("1", vm.TotalMembers);
        Assert.False(vm.HasNoMembers);
    }

    [Fact]
    public void MoveDown_SwapsOrder()
    {
        int issueA = SeedIssue("Green Lantern", "13");
        int issueB = SeedIssue("Green Lantern Corps", "13");
        var vm = new EventsScreenViewModel();
        vm.CreateNewCommand.Execute(null);
        foreach (var (name, number) in new[] { ("Green Lantern", "13"), ("Green Lantern Corps", "13") })
        {
            vm.SearchQuery = name;
            vm.SearchCommand.Execute(null);
            vm.AddIssueCommand.Execute(vm.SearchResults[0]);
        }
        var first = vm.Members[0];

        first.MoveDownCommand.Execute(null);

        Assert.Equal(issueB, vm.Members[0].Member.IssueId);
        Assert.Equal(issueA, vm.Members[1].Member.IssueId);
    }

    [Fact]
    public void RemoveMember_ClearsFromList()
    {
        SeedIssue("Green Lantern", "13");
        var vm = new EventsScreenViewModel();
        vm.CreateNewCommand.Execute(null);
        vm.SearchQuery = "Green Lantern";
        vm.SearchCommand.Execute(null);
        vm.AddIssueCommand.Execute(vm.SearchResults[0]);
        var member = Assert.Single(vm.Members);

        member.RemoveCommand.Execute(null);

        Assert.Empty(vm.Members);
        Assert.True(vm.HasNoMembers);
    }

    [Fact]
    public void ChangingRoleOnRow_Persists()
    {
        SeedIssue("Green Lantern", "13");
        var vm = new EventsScreenViewModel();
        vm.CreateNewCommand.Execute(null);
        vm.SearchQuery = "Green Lantern";
        vm.SearchCommand.Execute(null);
        vm.AddIssueCommand.Execute(vm.SearchResults[0]);
        var member = Assert.Single(vm.Members);
        int eventId = vm.Events[0].Id;

        member.SelectedRole = EventMembershipRole.Aftermath;

        vm.LoadEvent(eventId);
        var reloaded = Assert.Single(vm.Members);
        Assert.Equal(EventMembershipRole.Aftermath, reloaded.SelectedRole);
    }

    // --- Delete a whole event (docs/superpowers/specs/2026-08-22-delete-functionality-design.md) ---

    [Fact]
    public void DeleteConfirm_Trigger_RequiresTwoClicksBeforeDeleting()
    {
        var vm = new EventsScreenViewModel();
        vm.CreateNewCommand.Execute(null);
        var summary = vm.Events.Single();

        summary.DeleteConfirm.TriggerCommand.Execute(null);
        Assert.Single(vm.Events);

        summary.DeleteConfirm.TriggerCommand.Execute(null);
        Assert.Empty(vm.Events);
    }

    [Fact]
    public void Delete_OfTheLastRemainingEvent_ClearsTheScreen()
    {
        var vm = new EventsScreenViewModel();
        vm.CreateNewCommand.Execute(null);
        var summary = vm.Events.Single();

        summary.DeleteConfirm.TriggerCommand.Execute(null);
        summary.DeleteConfirm.TriggerCommand.Execute(null);

        Assert.True(vm.HasNoEvents);
        Assert.Equal(string.Empty, vm.EventName);
    }

    // --- Connected Events (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-relations-design.md) ---

    [Fact]
    public void ConnectEvent_MakesEachAppearInTheOthersSection_WithInvertedLabels()
    {
        int prequelId = SeedEvent("Secret Wars (1984)");
        int sequelId = SeedEvent("Secret Wars (2015)");
        var vm = new EventsScreenViewModel();
        vm.LoadEvent(prequelId);
        vm.SelectedEventRelationType = Paperbunkr.Data.Entities.RelationType.Prequel;
        vm.ConnectEventCommand.Execute(new Paperbunkr.App.Models.StoryEventSearchResult { StoryEventId = sequelId, Name = "Secret Wars (2015)" });

        // Viewed from the source (the 1984 event) - the other event's card shows the stored type.
        var fromPrequel = Assert.Single(vm.ConnectedEvents);
        Assert.Equal(sequelId, fromPrequel.OtherEventId);
        Assert.Equal("Prequel", fromPrequel.RelationLabel);

        vm.LoadEvent(sequelId);
        var fromSequel = Assert.Single(vm.ConnectedEvents);
        Assert.Equal(prequelId, fromSequel.OtherEventId);
        Assert.Equal("Sequel", fromSequel.RelationLabel);
    }

    [Fact]
    public void RemoveConnectedEvent_ClearsBothSides()
    {
        int aId = SeedEvent("Event A");
        int bId = SeedEvent("Event B");
        var vm = new EventsScreenViewModel();
        vm.LoadEvent(aId);
        vm.SelectedEventRelationType = Paperbunkr.Data.Entities.RelationType.Crossover;
        vm.ConnectEventCommand.Execute(new Paperbunkr.App.Models.StoryEventSearchResult { StoryEventId = bId, Name = "Event B" });
        var card = Assert.Single(vm.ConnectedEvents);

        vm.RemoveConnectedEventCommand.Execute(card);

        Assert.Empty(vm.ConnectedEvents);
        Assert.True(vm.HasNoConnectedEvents);

        vm.LoadEvent(bId);
        Assert.Empty(vm.ConnectedEvents);
    }

    [Fact]
    public void OpenConnectedEvent_LoadsItAsActiveEvent()
    {
        int aId = SeedEvent("Event A");
        int bId = SeedEvent("Event B");
        var vm = new EventsScreenViewModel();
        vm.LoadEvent(aId);
        vm.SelectedEventRelationType = Paperbunkr.Data.Entities.RelationType.Crossover;
        vm.ConnectEventCommand.Execute(new Paperbunkr.App.Models.StoryEventSearchResult { StoryEventId = bId, Name = "Event B" });
        var card = Assert.Single(vm.ConnectedEvents);

        vm.OpenConnectedEventCommand.Execute(card);

        Assert.Equal("Event B", vm.EventName);
    }

    [Fact]
    public void SearchEvents_ExcludesActiveEvent_MatchesByNameCaseInsensitive()
    {
        int activeId = SeedEvent("Rise of the Third Army");
        SeedEvent("Rise of the Third Sun");
        var vm = new EventsScreenViewModel();
        vm.LoadEvent(activeId);

        vm.ConnectEventQuery = "rise of the third";

        var result = Assert.Single(vm.EventSearchResults);
        Assert.Equal("Rise of the Third Sun", result.Name);
    }

    // --- Suggested Issues (docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md) ---

    [Fact]
    public void AddSuggestion_MovesItIntoTheMemberList_WithEditedRole()
    {
        int issueId = SeedSignalIssue("Avengers", "1", "Annual", 2015);
        int eventId = SeedEvent("Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1));
        var vm = new EventsScreenViewModel();
        vm.LoadEvent(eventId);
        var suggestion = Assert.Single(vm.SuggestedIssues);
        suggestion.SelectedRole = EventMembershipRole.TieIn;

        suggestion.AddCommand.Execute(null);

        Assert.Empty(vm.SuggestedIssues);
        var member = Assert.Single(vm.Members);
        Assert.Equal(issueId, member.Member.IssueId);
        Assert.Equal(EventMembershipRole.TieIn, member.SelectedRole);
    }

    [Fact]
    public void DismissSuggestion_RemovesFromVisibleList_WithoutCreatingMembership()
    {
        SeedSignalIssue("Avengers", "1", "Annual", 2015);
        int eventId = SeedEvent("Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1));
        var vm = new EventsScreenViewModel();
        vm.LoadEvent(eventId);
        var suggestion = Assert.Single(vm.SuggestedIssues);

        suggestion.DismissCommand.Execute(null);

        Assert.Empty(vm.SuggestedIssues);
        Assert.True(vm.HasNoSuggestions);
        Assert.Empty(vm.Members);
        Assert.Single(vm.DismissedSuggestions);
    }

    [Fact]
    public void DismissSuggestion_Persists_AcrossReload_AndCanBeRestored()
    {
        SeedSignalIssue("Avengers", "1", "Annual", 2015);
        int eventId = SeedEvent("Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1));
        var vm = new EventsScreenViewModel();
        vm.LoadEvent(eventId);
        Assert.Single(vm.SuggestedIssues).DismissCommand.Execute(null);

        vm.LoadEvent(eventId);
        Assert.Empty(vm.SuggestedIssues);
        var dismissed = Assert.Single(vm.DismissedSuggestions);

        vm.RestoreDismissedCommand.Execute(dismissed);

        Assert.Single(vm.SuggestedIssues);
        Assert.True(vm.HasNoDismissed);
    }

    [Fact]
    public void SuggestedRole_PreFilledFromCatalog_WhenSupplied()
    {
        SeedSignalIssue("Avengers", "1", "Prologue", 2015);
        int eventId = SeedEvent("Secret Wars", new DateTime(2015, 1, 1), new DateTime(2016, 1, 1));
        var vm = new EventsScreenViewModel();
        vm.LoadEvent(eventId);

        var suggestion = Assert.Single(vm.SuggestedIssues);
        Assert.Equal(EventMembershipRole.Prologue, suggestion.SelectedRole);
    }

    // --- Continuities mode (docs/superpowers/specs/2026-08-27-metadata-model-phase4f-continuity-browse-design.md) ---

    private static (int seriesId, int continuityId) SeedSeriesInContinuity(string seriesName, string continuityName)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();
        var continuity = ContinuityResolver.GetOrCreate(context, continuityName);
        ContinuityResolver.AddSeriesToContinuity(context, series.Id, continuity.Id);
        return (series.Id, continuity.Id);
    }

    [Fact]
    public void SwitchToContinuitiesMode_PopulatesSidebar_WithCorrectSeriesCount()
    {
        SeedSeriesInContinuity("Avengers", "Earth-616");
        SeedSeriesInContinuity("X-Men", "Earth-616");
        var vm = new EventsScreenViewModel();

        vm.SetModeCommand.Execute(EventsScreenMode.Continuities);

        Assert.True(vm.IsContinuitiesMode);
        var summary = Assert.Single(vm.Continuities);
        Assert.Equal("Earth-616", summary.Name);
        Assert.Equal(2, summary.SeriesCount);
    }

    [Fact]
    public void SelectContinuity_PopulatesMemberSeriesGrid()
    {
        var (_, continuityId) = SeedSeriesInContinuity("Avengers", "Earth-616");
        SeedSeriesInContinuity("X-Men", "Earth-616");
        var vm = new EventsScreenViewModel();
        vm.SetModeCommand.Execute(EventsScreenMode.Continuities);

        vm.SelectContinuityCommand.Execute(new ContinuitySummary(continuityId, "Earth-616", null, 2));

        Assert.Equal(2, vm.ContinuityMembers.Count);
    }

    [Fact]
    public void AddAndRemoveSeries_WriteThroughContinuityResolver_UpdatingBothGridAndSharedContinuity()
    {
        var (firstSeriesId, continuityId) = SeedSeriesInContinuity("Avengers", "Earth-616");
        using (var c = PaperbunkrDb.CreateContext())
        {
            c.Series.Add(new Series { Name = "X-Men" });
            c.SaveChanges();
        }
        var vm = new EventsScreenViewModel();
        vm.SetModeCommand.Execute(EventsScreenMode.Continuities);
        vm.SelectContinuityCommand.Execute(new ContinuitySummary(continuityId, "Earth-616", null, 1));

        int xmenId;
        using (var c = PaperbunkrDb.CreateContext())
        {
            xmenId = c.Series.Single(s => s.Name == "X-Men").Id;
        }
        vm.AddSeriesToActiveContinuityCommand.Execute(new SeriesSearchResult { SeriesId = xmenId, Name = "X-Men" });

        Assert.Equal(2, vm.ContinuityMembers.Count);
        using (var c = PaperbunkrDb.CreateContext())
        {
            // Same path the Related-tab UI uses - the shared-continuity query now sees both.
            Assert.Single(ContinuityResolver.GetOtherSeriesSharingContinuity(c, firstSeriesId));
        }

        var xmenCard = vm.ContinuityMembers.Single(m => m.SeriesId == xmenId);
        vm.RemoveSeriesFromActiveContinuityCommand.Execute(xmenCard);

        Assert.Single(vm.ContinuityMembers);
        using (var c = PaperbunkrDb.CreateContext())
        {
            Assert.Empty(ContinuityResolver.GetOtherSeriesSharingContinuity(c, firstSeriesId));
        }
    }

    [Fact]
    public void CreateNewContinuityFromSidebar_DedupesCaseInsensitively()
    {
        var vm = new EventsScreenViewModel();
        vm.SetModeCommand.Execute(EventsScreenMode.Continuities);

        vm.NewContinuityName = "Earth-616";
        vm.CreateNewContinuityCommand.Execute(null);
        vm.NewContinuityName = "earth-616";
        vm.CreateNewContinuityCommand.Execute(null);

        Assert.Single(vm.Continuities);
    }

    [Fact]
    public void OpenContinuitySeries_NavigatesToSeriesDetail()
    {
        var (seriesId, continuityId) = SeedSeriesInContinuity("Avengers", "Earth-616");
        int? navigatedTo = null;
        var vm = new EventsScreenViewModel(goToSeriesDetail: id => navigatedTo = id, goToReader: null);
        vm.SetModeCommand.Execute(EventsScreenMode.Continuities);
        vm.SelectContinuityCommand.Execute(new ContinuitySummary(continuityId, "Earth-616", null, 1));

        vm.OpenContinuitySeriesCommand.Execute(vm.ContinuityMembers.Single());

        Assert.Equal(seriesId, navigatedTo);
    }

    // --- Timeline mode (docs/superpowers/specs/2026-08-27-metadata-model-phase4g-age-progression-design.md) ---

    private static int SeedTimelineSeries(string name, params (string number, int year)[] issues)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        foreach (var (number, year) in issues)
        {
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = number, Year = year });
        }
        context.SaveChanges();
        return series.Id;
    }

    [Fact]
    public void SelectTimelineSeries_PopulatesEraBucketedSections_OnlyNonEmptyAges_YearOrdered()
    {
        int seriesId = SeedTimelineSeries("The Flash", ("1", 1965), ("2", 1962), ("350", 1990));
        var vm = new EventsScreenViewModel();
        vm.SetModeCommand.Execute(EventsScreenMode.Timeline);

        vm.SelectTimelineSeriesCommand.Execute(new SeriesSearchResult { SeriesId = seriesId, Name = "The Flash" });

        Assert.True(vm.HasTimelineSeed);
        Assert.Equal(2, vm.TimelineSections.Count); // Silver + Modern, not Golden/Bronze/Platinum
        Assert.Equal("Silver Age", vm.TimelineSections[0].Label);
        Assert.Equal("Modern Age", vm.TimelineSections[1].Label);
        // Year-ordered within the Silver section.
        Assert.Equal("1962", vm.TimelineSections[0].Issues[0].YearLabel);
        Assert.Equal("1965", vm.TimelineSections[0].Issues[1].YearLabel);
    }

    [Fact]
    public void TimelineIssue_InDisputedWindow_HasReducedConfidenceIndicator()
    {
        int seriesId = SeedTimelineSeries("Crisis Era", ("1", 1982));
        var vm = new EventsScreenViewModel();
        vm.SetModeCommand.Execute(EventsScreenMode.Timeline);

        vm.SelectTimelineSeriesCommand.Execute(new SeriesSearchResult { SeriesId = seriesId, Name = "Crisis Era" });

        var card = vm.TimelineSections.Single().Issues.Single();
        Assert.True(card.IsReducedConfidence);
        Assert.Contains("Bronze Age", card.ConfidenceReason);
    }

    [Fact]
    public void ClickingTimelineIssue_OpensTheReader()
    {
        int seriesId = SeedTimelineSeries("The Flash", ("1", 1990));
        int? openedIssueId = null;
        var vm = new EventsScreenViewModel(goToSeriesDetail: null, goToReader: id => openedIssueId = id);
        vm.SetModeCommand.Execute(EventsScreenMode.Timeline);
        vm.SelectTimelineSeriesCommand.Execute(new SeriesSearchResult { SeriesId = seriesId, Name = "The Flash" });
        var card = vm.TimelineSections.Single().Issues.Single();

        vm.OpenTimelineIssueCommand.Execute(card);

        Assert.Equal(card.IssueId, openedIssueId);
    }

    [Fact]
    public void TimelineLibraryScope_LaysOutEverySeries_NoSeedNeeded()
    {
        SeedTimelineSeries("Series A", ("1", 1965));
        SeedTimelineSeries("Series B", ("1", 1990));
        var vm = new EventsScreenViewModel();
        vm.SetModeCommand.Execute(EventsScreenMode.Timeline);

        vm.SetTimelineScopeCommand.Execute(TimelineScope.Library);

        Assert.True(vm.HasTimelineSeed);
        Assert.Equal(2, vm.TimelineSections.Count); // Silver + Modern across both series
        Assert.Contains("whole library", vm.TimelineTitle);
    }

    [Fact]
    public void TimelineContinuityScope_LaysOutTheContinuitysSeries()
    {
        var (seriesId, continuityId) = SeedSeriesInContinuity("Avengers", "Earth-616");
        using (var c = PaperbunkrDb.CreateContext())
        {
            c.Issues.Add(new Issue { SeriesId = seriesId, Number = "1", Year = 1965 });
            c.SaveChanges();
        }
        var vm = new EventsScreenViewModel();
        vm.SetModeCommand.Execute(EventsScreenMode.Timeline);

        vm.LoadContinuityTimeline(continuityId);

        Assert.Equal("Silver Age", Assert.Single(vm.TimelineSections).Label);
        Assert.Contains("Earth-616", vm.TimelineTitle);
    }

    [Fact]
    public void TimelineCharacterAware_PullsInUnrelatedCharacterSharer()
    {
        int flashId = SeedTimelineSeries("The Flash", ("1", 1965));
        int oneShotId = SeedTimelineSeries("DC One-Shot", ("1", 1990));
        using (var c = PaperbunkrDb.CreateContext())
        {
            c.Issues.Single(i => i.SeriesId == flashId).Characters = "Barry Allen";
            c.Issues.Single(i => i.SeriesId == oneShotId).Characters = "Barry Allen";
            c.SaveChanges();
            Paperbunkr.Data.Metadata.CharacterResolver.RebuildAll(c);
        }
        var vm = new EventsScreenViewModel();
        vm.SetModeCommand.Execute(EventsScreenMode.Timeline);
        vm.SelectTimelineSeriesCommand.Execute(new SeriesSearchResult { SeriesId = flashId, Name = "The Flash" });
        Assert.Single(vm.TimelineSections); // just Silver (Flash 1965)

        vm.TimelineCharacterAware = true;

        Assert.Equal(2, vm.TimelineSections.Count); // Silver + Modern (one-shot 1990) now included
    }

    [Fact]
    public void TimelineInferredAges_AcceptWritesLabel_AndRemovesTheRow()
    {
        int seriesId = SeedTimelineSeries("The Flash", ("1", 1965));
        var vm = new EventsScreenViewModel();
        vm.SetModeCommand.Execute(EventsScreenMode.Timeline);
        vm.SelectTimelineSeriesCommand.Execute(new SeriesSearchResult { SeriesId = seriesId, Name = "The Flash" });

        var row = Assert.Single(vm.InferredAges);
        Assert.Equal("Silver Age", row.AgeLabel);

        row.AcceptCommand.Execute(null);

        Assert.Empty(vm.InferredAges);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal("Silver (1956-69)", context.Issues.Single().BookAge);
    }

    // --- Event graph / connection suggestions / dismissed (Phase 4d/4e deferred items) ---

    [Fact]
    public void EventChain_ShowsTransitiveGraph_ClickJumpsToAnyNode()
    {
        int a = SeedEvent("Event A");
        int b = SeedEvent("Event B");
        int d = SeedEvent("Event D");
        var vm = new EventsScreenViewModel();
        vm.LoadEvent(a);
        vm.SelectedEventRelationType = Paperbunkr.Data.Entities.RelationType.Sequel;
        vm.ConnectEventCommand.Execute(new Paperbunkr.App.Models.StoryEventSearchResult { StoryEventId = b, Name = "Event B" });
        vm.LoadEvent(b);
        vm.SelectedEventRelationType = Paperbunkr.Data.Entities.RelationType.Sequel;
        vm.ConnectEventCommand.Execute(new Paperbunkr.App.Models.StoryEventSearchResult { StoryEventId = d, Name = "Event D" });

        vm.LoadEvent(a);

        Assert.True(vm.HasEventChain); // A + B (direct) + D (transitive)
        Assert.Equal(3, vm.EventFamily.Count);
        var dNode = vm.EventFamily.Single(n => n.Name == "Event D");
        Assert.Equal(2, dNode.Depth);

        vm.OpenFamilyEventCommand.Execute(dNode);
        Assert.Equal("Event D", vm.EventName);
    }

    [Fact]
    public void ConnectionSuggestion_SurfacesLikelyPair_AndConnectsIt()
    {
        int a = SeedEvent("Secret Wars", new DateTime(2015, 5, 1), null);
        SeedEvent("Secret Empire", new DateTime(2017, 4, 1), null);
        var vm = new EventsScreenViewModel();
        vm.LoadEvent(a);

        var suggestion = Assert.Single(vm.EventConnectionSuggestions);
        Assert.Equal("Secret Empire", suggestion.Name);

        vm.SelectedEventRelationType = Paperbunkr.Data.Entities.RelationType.Related;
        vm.ConnectSuggestedEventCommand.Execute(suggestion);

        Assert.Single(vm.ConnectedEvents);
        Assert.True(vm.HasNoConnectionSuggestions);
    }

    // --- Cross-continuity comparison + reading list (Phase 4f deferred items) ---

    [Fact]
    public void CompareContinuities_ShowsOverlapAndSharedSeries()
    {
        var (sharedSeriesId, e616Id) = SeedSeriesInContinuity("Avengers", "Earth-616");
        using (var c = PaperbunkrDb.CreateContext())
        {
            var ult = Paperbunkr.Data.Metadata.ContinuityResolver.GetOrCreate(c, "Ultimate");
            Paperbunkr.Data.Metadata.ContinuityResolver.AddSeriesToContinuity(c, sharedSeriesId, ult.Id);
        }
        var vm = new EventsScreenViewModel();
        vm.SetModeCommand.Execute(EventsScreenMode.Continuities);
        vm.SelectContinuityCommand.Execute(new ContinuitySummary(e616Id, "Earth-616", null, 1));

        var overlap = Assert.Single(vm.OverlappingContinuities);
        Assert.Equal("Ultimate", overlap.Name);

        vm.CompareWithContinuityCommand.Execute(overlap);

        Assert.True(vm.HasComparison);
        Assert.Equal("Avengers", Assert.Single(vm.SharedContinuitySeries).Name);
    }

    [Fact]
    public void CreateReadingListFromContinuity_BuildsList_AndNavigates()
    {
        var (seriesId, continuityId) = SeedSeriesInContinuity("Avengers", "Earth-616");
        using (var c = PaperbunkrDb.CreateContext())
        {
            c.Issues.Add(new Issue { SeriesId = seriesId, Number = "1", Year = 1965 });
            c.SaveChanges();
        }
        int? navigatedToList = null;
        var vm = new EventsScreenViewModel(goToReadingList: id => navigatedToList = id);
        vm.SetModeCommand.Execute(EventsScreenMode.Continuities);
        vm.SelectContinuityCommand.Execute(new ContinuitySummary(continuityId, "Earth-616", null, 1));

        vm.CreateReadingListFromContinuityCommand.Execute(null);

        Assert.NotNull(navigatedToList);
        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Single();
        Assert.Equal("Earth-616 (continuity)", list.Name);
        Assert.Equal(navigatedToList, list.Id);
    }
}
