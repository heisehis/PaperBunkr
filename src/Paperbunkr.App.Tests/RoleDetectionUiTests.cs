using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The role-detection UI (docs/superpowers/specs/2026-09-25-reading-list-role-detection-design.md, section 6): the "Detect roles"
/// action on an event and on a reading list, the suggestion chip's Accept / Dismiss, clearing a detected role, and the guarantee that a
/// role picked by hand is marked as the user's.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class RoleDetectionUiTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public RoleDetectionUiTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_roles_ui_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private sealed class NoFilePicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private static int SeedIssue(string series, string number, string? format = null, string? title = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var seriesRow = context.Series.FirstOrDefault(s => s.Name == series) ?? context.Series.Add(new Series { Name = series }).Entity;
        context.SaveChanges();
        var issue = new Issue { SeriesId = seriesRow.Id, Number = number, Format = format, Title = title };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static int SeedEventWithMembers(params int[] issueIds)
    {
        using var context = PaperbunkrDb.CreateContext();
        var storyEvent = new StoryEvent { Name = "Big Event", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        int position = 0;
        foreach (int issueId in issueIds)
        {
            context.EventMemberships.Add(new EventMembership { StoryEventId = storyEvent.Id, IssueId = issueId, Position = position++, Role = EventMembershipRole.Core });
        }

        context.SaveChanges();
        return storyEvent.Id;
    }

    private static EventsScreenViewModel OpenEvent(int eventId)
    {
        var vm = new EventsScreenViewModel();
        vm.SelectEventCommand.Execute(new StoryEventSummary { Id = eventId, Name = "Big Event", DeleteConfirm = new TwoStepConfirm(() => { }) });
        return vm;
    }

    // -- events --

    [Fact]
    public void DetectRolesOnAnEvent_OffersASuggestion_ForAMemberWhoseDefaultCoreCannotBeToldFromAChoice()
    {
        int eventId = SeedEventWithMembers(SeedIssue("Spider-Man", "1", format: "Prologue"), SeedIssue("Spider-Man", "2"));
        var vm = OpenEvent(eventId);

        vm.DetectRolesCommand.Execute(null);
        TestDispatcher.Drain();

        var row = vm.Members[0];
        Assert.True(row.HasRoleSuggestion);
        Assert.Contains("Prologue", row.SuggestionText);
        Assert.Equal(EventMembershipRole.Core, row.SelectedRole);              // nothing was changed behind the user's back
        Assert.False(vm.Members[1].HasRoleSuggestion);
    }

    [Fact]
    public void AcceptingASuggestion_MakesItTheUsersOwnRole_AndSavesIt()
    {
        int eventId = SeedEventWithMembers(SeedIssue("Spider-Man", "1", format: "Prologue"));
        var vm = OpenEvent(eventId);
        vm.DetectRolesCommand.Execute(null);
        TestDispatcher.Drain();

        vm.Members[0].AcceptSuggestionCommand.Execute(null);

        Assert.Equal(EventMembershipRole.Prologue, vm.Members[0].SelectedRole);
        Assert.False(vm.Members[0].HasRoleSuggestion);
        Assert.False(vm.Members[0].IsAutoRole);
        using var context = PaperbunkrDb.CreateContext();
        var saved = context.EventMemberships.Single();
        Assert.Equal(EventMembershipRole.Prologue, saved.Role);
        Assert.Equal(RoleAssignmentSource.User, saved.RoleSource);
        Assert.Null(saved.SuggestedRole);
    }

    [Fact]
    public void DismissingASuggestion_KeepsTheRole_AndIsRememberedSoDetectionDoesNotAskAgain()
    {
        int eventId = SeedEventWithMembers(SeedIssue("Spider-Man", "1", format: "Prologue"));
        var vm = OpenEvent(eventId);
        vm.DetectRolesCommand.Execute(null);
        TestDispatcher.Drain();

        vm.Members[0].DismissSuggestionCommand.Execute(null);
        vm.DetectRolesCommand.Execute(null);
        TestDispatcher.Drain();

        Assert.False(vm.Members[0].HasRoleSuggestion);
        using var context = PaperbunkrDb.CreateContext();
        var saved = context.EventMemberships.Single();
        Assert.Equal(EventMembershipRole.Core, saved.Role);
        Assert.True(saved.RoleSuggestionDismissed);
        Assert.Null(saved.SuggestedRole);
    }

    [Fact]
    public void ARoleChosenByHandInTheDropdown_IsMarkedAsTheUsers()
    {
        int eventId = SeedEventWithMembers(SeedIssue("Spider-Man", "1"));
        using (var context = PaperbunkrDb.CreateContext())
        {
            var member = context.EventMemberships.Single();
            member.Role = EventMembershipRole.Prologue;
            member.RoleSource = RoleAssignmentSource.Auto;
            member.RoleReason = "Format: Prologue";
            context.SaveChanges();
        }

        var vm = OpenEvent(eventId);
        Assert.True(vm.Members[0].IsAutoRole);
        Assert.EndsWith("· auto", vm.Members[0].RoleChipLabel);
        Assert.Contains("Format: Prologue", vm.Members[0].RoleReasonText);

        vm.Members[0].SetRoleCommand.Execute(EventMembershipRole.TieIn);

        Assert.False(vm.Members[0].IsAutoRole);
        Assert.DoesNotContain("auto", vm.Members[0].RoleChipLabel);
        using var verify = PaperbunkrDb.CreateContext();
        var saved = verify.EventMemberships.Single();
        Assert.Equal(EventMembershipRole.TieIn, saved.Role);
        Assert.Equal(RoleAssignmentSource.User, saved.RoleSource);
        Assert.Null(saved.RoleReason);
    }

    [Fact]
    public void ClearingADetectedEventRole_ReturnsItToCore_AndDetectionDoesNotPutItBack()
    {
        int eventId = SeedEventWithMembers(SeedIssue("Spider-Man", "1", format: "Prologue"));
        using (var context = PaperbunkrDb.CreateContext())
        {
            var member = context.EventMemberships.Single();
            member.Role = EventMembershipRole.Prologue;
            member.RoleSource = RoleAssignmentSource.Auto;
            context.SaveChanges();
        }

        var vm = OpenEvent(eventId);
        vm.Members[0].ClearAutoRoleCommand.Execute(null);
        vm.DetectRolesCommand.Execute(null);
        TestDispatcher.Drain();

        Assert.Equal(EventMembershipRole.Core, vm.Members[0].SelectedRole);
        Assert.False(vm.Members[0].HasRoleSuggestion);
        using var verify = PaperbunkrDb.CreateContext();
        Assert.Equal(EventMembershipRole.Core, verify.EventMemberships.Single().Role);
    }

    /// <summary>Both edited screens still load their compiled XAML with the new role chips, tooltip and menu items (a build alone does not prove it).</summary>
    [Fact]
    public void TheEditedScreens_StillConstruct_WithRoleRowsOnScreen()
    {
        TestAppBuilder.EnsureInitialized();
        int eventId = SeedEventWithMembers(SeedIssue("Spider-Man", "1", format: "Prologue"));
        var events = OpenEvent(eventId);
        events.DetectRolesCommand.Execute(null);
        TestDispatcher.Drain();
        Assert.NotNull(new Views.EventsScreen { DataContext = events }.Content);

        int listId = SeedList(SeedIssue("Thor", "1", title: "Aftermath"));
        var reading = new ReadingScreenViewModel(new NoFilePicker(), (_, _) => { });
        reading.LoadReadingList(listId);
        reading.DetectRolesCommand.Execute(null);
        TestDispatcher.Drain();
        Assert.NotNull(new Views.ReadingScreen { DataContext = reading }.Content);
    }

    // -- reading lists --

    private static int SeedList(params int[] issueIds)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = new ReadingList { Name = "L", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        int order = 0;
        foreach (int issueId in issueIds)
        {
            list.Items.Add(new ReadingListItem { IssueId = issueId, SortOrder = order++ });
        }

        context.ReadingLists.Add(list);
        context.SaveChanges();
        return list.Id;
    }

    private static IEnumerable<ReadingListItemRowViewModel> Rows(ReadingScreenViewModel vm) => vm.Groups.SelectMany(g => g.Rows);

    [Fact]
    public void DetectRolesOnAList_FillsAnEmptyItemAsAutomatic_AndShowsWhy()
    {
        int listId = SeedList(SeedIssue("Spider-Man", "1", title: "Aftermath"), SeedIssue("Spider-Man", "2"));
        var vm = new ReadingScreenViewModel(new NoFilePicker(), (_, _) => { });
        vm.LoadReadingList(listId);

        vm.DetectRolesCommand.Execute(null);
        TestDispatcher.Drain();

        var row = Rows(vm).First();
        Assert.True(row.IsAutoRole);
        Assert.True(row.HasRole);
        Assert.Equal("Aftermath · auto", row.RoleChipLabel);
        Assert.Contains("aftermath", row.RoleReasonText, StringComparison.OrdinalIgnoreCase);
        Assert.False(Rows(vm).Last().HasRole);
        Assert.Equal("1 role detected, 0 need review.", vm.StatusMessage);
    }

    [Fact]
    public void ClearingADetectedListRole_LeavesTheItemWithoutARole_AndItStaysCleared()
    {
        int listId = SeedList(SeedIssue("Spider-Man", "1", title: "Aftermath"));
        var vm = new ReadingScreenViewModel(new NoFilePicker(), (_, _) => { });
        vm.LoadReadingList(listId);
        vm.DetectRolesCommand.Execute(null);
        TestDispatcher.Drain();

        Rows(vm).First().ClearAutoRoleCommand.Execute(null);
        vm.DetectRolesCommand.Execute(null);
        TestDispatcher.Drain();

        Assert.False(Rows(vm).First().HasRole);
        using var context = PaperbunkrDb.CreateContext();
        var saved = context.ReadingListItems.Single();
        Assert.Null(saved.Role);
        Assert.True(saved.RoleSuggestionDismissed);
    }

    [Fact]
    public void ARoleTheUserSetOnAListItem_IsOnlyEverSuggestedAgainst()
    {
        int listId = SeedList(SeedIssue("Spider-Man", "1", title: "Aftermath"));
        var vm = new ReadingScreenViewModel(new NoFilePicker(), (_, _) => { });
        vm.LoadReadingList(listId);
        Rows(vm).First().SetRoleForTest(EventMembershipRole.Core);

        vm.DetectRolesCommand.Execute(null);
        TestDispatcher.Drain();

        var row = Rows(vm).First();
        Assert.Equal(EventMembershipRole.Core, row.SelectedRole);
        Assert.False(row.IsAutoRole);
        Assert.True(row.HasRoleSuggestion);
        Assert.Contains("Aftermath", row.SuggestionText);
    }
}

internal static class RoleTestExtensions
{
    /// <summary>Picks a role the way the row's editor does: through the selected option, which is what marks it as the user's own.</summary>
    public static void SetRoleForTest(this ReadingListItemRowViewModel row, EventMembershipRole role) =>
        row.SelectedRoleOption = ReadingListItemRowViewModel.RoleOptions.First(o => o.Role == role);
}
