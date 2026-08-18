using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

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
}
