using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.ContinuityScreen;

/// <summary>
/// <see cref="SuggestionsChecksViewModel"/> (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md, "Suggestions &amp;
/// checks"), carrying over the old screen's story-event suggestion and Story Event resolver tests (docs/superpowers/specs/
/// 2026-09-17-storyevent-continuity-autopopulate-design.md, 2026-09-27-story-event-resolver-design.md §4). No credentials and no network:
/// verification is a no-op, the sweep has no provider sources and a synchronous runner. Every row removal now waits one dispatcher tick.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SuggestionsChecksViewModelTests : ContinuityScreenTestBase
{
    private static void SeedArcIssues(string arcName, string publisher, params (string SeriesName, string Number)[] issues)
    {
        using var context = PaperbunkrDb.CreateContext();
        foreach (var (seriesName, number) in issues)
        {
            var series = new Series { Name = seriesName };
            context.Series.Add(series);
            context.SaveChanges();
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = number, StoryArc = arcName, Publisher = publisher });
        }

        context.SaveChanges();
    }

    private static (int ProviderEvent, int UserEvent) SeedPossibleDuplicate()
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = "Hulk" };
        series.Issues.Add(new Issue { Number = "92" });
        series.Issues.Add(new Issue { Number = "93" });
        context.Series.Add(series);
        context.SaveChanges();

        StoryEvent Make(string name, StoryEventOrigin origin)
        {
            var e = new StoryEvent { Name = name, Origin = origin, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            int position = 1;
            foreach (var issue in series.Issues)
            {
                e.Members.Add(new EventMembership { IssueId = issue.Id, Position = position++ });
            }

            context.StoryEvents.Add(e);
            return e;
        }

        var provider = Make("Hulk: Planet Hulk", StoryEventOrigin.Provider);
        var mine = Make("Planet Hulk", StoryEventOrigin.User);       // a user event: never merged silently, so it waits for review
        context.SaveChanges();
        return (provider.Id, mine.Id);
    }

    [Fact]
    public void StoryEventCandidates_ComeFromLocalGrouping_AndCountOnTheSidebarBadge()
    {
        SeedArcIssues("Civil War", "Marvel", ("Avengers", "1"), ("Iron Man", "1"));
        var vm = CreateScreen();

        vm.RefreshStoryEventCandidates();

        var candidate = Assert.Single(vm.Suggestions.NewStoryEventCandidates);
        Assert.Equal("Civil War", candidate.ArcName);
        Assert.Equal(2, candidate.MemberCount);
        Assert.Equal(1, vm.Sidebar.SuggestionsCount);
        Assert.True(vm.Sidebar.HasSuggestions);
    }

    [Fact]
    public async Task AcceptingACandidate_CreatesTheEvent_RemovesTheRowAfterTheTick_AndOpensIt()
    {
        SeedArcIssues("Civil War", "Marvel", ("Avengers", "1"), ("Iron Man", "1"));
        var vm = CreateScreen();
        vm.RefreshStoryEventCandidates();

        await vm.Suggestions.NewStoryEventCandidates.Single().AcceptCommand.ExecuteAsync(null);
        Drain();

        Assert.Empty(vm.Suggestions.NewStoryEventCandidates);
        using var context = PaperbunkrDb.CreateContext();
        var storyEvent = Assert.Single(context.StoryEvents);
        Assert.Equal("Civil War", storyEvent.Name);
        Assert.Equal(2, context.EventMemberships.Count(m => m.StoryEventId == storyEvent.Id));
        Assert.Equal(storyEvent.Id, vm.ActiveEventId);
    }

    [Fact]
    public void DismissingACandidate_IsDeferred_AndRemembered()
    {
        SeedArcIssues("Civil War", "Marvel", ("Avengers", "1"), ("Iron Man", "1"));
        var vm = CreateScreen();
        vm.RefreshStoryEventCandidates();

        vm.Suggestions.NewStoryEventCandidates.Single().DismissCommand.Execute(null);
        Assert.Single(vm.Suggestions.NewStoryEventCandidates);
        Drain();
        Assert.Empty(vm.Suggestions.NewStoryEventCandidates);
        Assert.Equal(0, vm.Sidebar.SuggestionsCount);

        vm.RefreshStoryEventCandidates();
        Assert.Empty(vm.Suggestions.NewStoryEventCandidates);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.StoryEvents);
    }

    [Fact]
    public void PossibleDuplicates_ShowOnlyWhenThereArePairs()
    {
        var vm = CreateScreen();
        vm.RefreshPossibleDuplicates();
        Assert.False(vm.Suggestions.HasPossibleDuplicates);
        Assert.True(vm.Suggestions.IsEmpty);

        SeedPossibleDuplicate();
        vm.RefreshPossibleDuplicates();

        var row = Assert.Single(vm.Suggestions.PossibleDuplicates);
        Assert.True(row.IsPair);
        Assert.Equal("Names match, 2 of 2 issues shared", row.Reason);
        Assert.Equal("Keeps “Planet Hulk”", row.KeptLabel);
    }

    [Fact]
    public void Merge_ActsOnlyAfterTheDispatcherRuns_AndSwitchesAwayFromTheMergedEvent()
    {
        var (provider, mine) = SeedPossibleDuplicate();
        var vm = CreateScreen();
        vm.LoadEvent(provider);                      // the event about to be merged away is open
        vm.RefreshPossibleDuplicates();

        vm.Suggestions.PossibleDuplicates.Single().MergeCommand.Execute(null);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(2, context.StoryEvents.Count());          // deferred: the row belongs to the list the merge rebuilds
        }

        Drain();

        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(mine, context.StoryEvents.Single().Id);   // the user's event survives
        }

        Assert.Equal(mine, vm.ActiveEventId);
        Assert.Empty(vm.Suggestions.PossibleDuplicates);
    }

    [Fact]
    public void NotTheSame_IsDeferred_AndRemembered()
    {
        SeedPossibleDuplicate();
        var vm = CreateScreen();
        vm.RefreshPossibleDuplicates();

        vm.Suggestions.PossibleDuplicates.Single().NotTheSameCommand.Execute(null);
        Assert.Single(vm.Suggestions.PossibleDuplicates);
        Drain();

        Assert.Empty(vm.Suggestions.PossibleDuplicates);
        vm.RefreshPossibleDuplicates();
        Assert.Empty(vm.Suggestions.PossibleDuplicates);
    }

    [Fact]
    public async Task CheckStoryEvents_ReportsOneActivitySummary_CoveringBothSteps()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "Hulk" };
            series.Issues.Add(new Issue { Number = "92" });
            context.Series.Add(series);
            context.SaveChanges();
            foreach (var name in new[] { "Planet Hulk", "Hulk: Planet Hulk" })
            {
                var e = new StoryEvent { Name = name, Origin = StoryEventOrigin.Provider, ComicVineArcId = "4512", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                e.Members.Add(new EventMembership { IssueId = series.Issues[0].Id, Position = 1 });
                context.StoryEvents.Add(e);
            }

            context.SaveChanges();
        }

        var activity = QuietActivity();
        var vm = CreateScreen(activity: activity);

        await vm.Suggestions.CheckStoryEventsCommand.ExecuteAsync(null);

        var job = Assert.Single(activity.RecentJobs);
        Assert.Contains("1 duplicate merged", job.ResultSummary);
        Assert.Contains("connection", job.ResultSummary);
        Assert.Equal(job.ResultSummary, vm.Suggestions.StoryEventCheckResult);
        using var check = PaperbunkrDb.CreateContext();
        Assert.Equal(1, check.StoryEvents.Count());
    }

    [Fact]
    public void OpeningAtAnEventsDuplicate_FocusesThatPair()
    {
        var (provider, _) = SeedPossibleDuplicate();
        var vm = CreateScreen();
        var page = OpenEvent(provider, vm);

        var item = page.Attention.Single(a => a.Kind == Paperbunkr.App.Services.ContinuityScreen.AttentionKind.Duplicate);
        Assert.Equal("Possible duplicate: Planet Hulk", item.Text);
        page.OpenAttentionCommand.Execute(item);

        Assert.True(vm.IsSuggestionsOpen);
        Assert.Equal(provider, vm.Suggestions.FocusedDuplicateEventId);
    }
}
