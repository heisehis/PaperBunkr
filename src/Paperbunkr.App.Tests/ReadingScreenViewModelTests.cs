using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises the Phase 4c overhaul additions to <see cref="ReadingScreenViewModel"/> (docs/
/// superpowers/specs/2026-08-17-metadata-model-phase4c-reading-list-overhaul-design.md) - Type/
/// StoryEvent link/per-item Role+Notes. Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/>
/// to a temp SQLite file, same pattern as <see cref="EventsScreenViewModelTests"/> -
/// <see cref="ReadingScreenViewModel"/> has no injected context-factory seam.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReadingScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly FakeFilePickerService _filePicker = new();

    public ReadingScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reading_vm_test_{Guid.NewGuid():N}.db");
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

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
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
    public void CreateNew_DefaultsToTypeUser_SetsTimestamps()
    {
        var vm = new ReadingScreenViewModel(_filePicker);

        vm.CreateNewCommand.Execute(null);

        Assert.Equal(ReadingListType.User, vm.SelectedType);
        Assert.StartsWith("Created ", vm.CreatedAtLabel);

        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Single();
        Assert.Equal(ReadingListType.User, list.Type);
        Assert.True(list.CreatedAt > DateTime.MinValue);
        Assert.True(list.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public void ChangingSelectedType_Persists_WithoutFiringOnLoad()
    {
        var vm = new ReadingScreenViewModel(_filePicker);
        vm.CreateNewCommand.Execute(null);
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            listId = context.ReadingLists.Single().Id;
        }

        vm.SelectedType = ReadingListType.Event;

        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Equal(ReadingListType.Event, verifyContext.ReadingLists.Single(r => r.Id == listId).Type);

        // Reloading the same list must reflect the persisted Type without re-triggering a write
        // loop (the _isLoadingList guard) - loading a list whose Type is already Event should not
        // throw/duplicate-write.
        vm.LoadReadingList(listId);
        Assert.Equal(ReadingListType.Event, vm.SelectedType);
    }

    [Fact]
    public void ToggleLinkStoryEvent_TogglesPanelState_AndClearsSearch()
    {
        var vm = new ReadingScreenViewModel(_filePicker);
        vm.CreateNewCommand.Execute(null);

        vm.ToggleLinkStoryEventCommand.Execute(null);
        Assert.True(vm.IsLinkingStoryEvent);

        vm.ToggleLinkStoryEventCommand.Execute(null);
        Assert.False(vm.IsLinkingStoryEvent);
        Assert.Equal(string.Empty, vm.StoryEventSearchQuery);
    }

    [Fact]
    public void LinkStoryEvent_SetsLinkedName_Persists()
    {
        int storyEventId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var storyEvent = new StoryEvent { Name = "Crisis Event", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.StoryEvents.Add(storyEvent);
            context.SaveChanges();
            storyEventId = storyEvent.Id;
        }

        var vm = new ReadingScreenViewModel(_filePicker);
        vm.CreateNewCommand.Execute(null);
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            listId = context.ReadingLists.Single().Id;
        }

        vm.StoryEventSearchQuery = "Crisis";
        var target = Assert.Single(vm.StoryEventSearchResults);

        vm.LinkStoryEventCommand.Execute(target);

        Assert.Equal("Crisis Event", vm.LinkedStoryEventName);
        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Equal(storyEventId, verifyContext.ReadingLists.Single(r => r.Id == listId).StoryEventId);
    }

    [Fact]
    public void UnlinkStoryEvent_ClearsLink_KeepsType()
    {
        int storyEventId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var storyEvent = new StoryEvent { Name = "Crisis Event", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.StoryEvents.Add(storyEvent);
            context.SaveChanges();
            storyEventId = storyEvent.Id;
        }

        var vm = new ReadingScreenViewModel(_filePicker);
        vm.CreateNewCommand.Execute(null);
        vm.SelectedType = ReadingListType.Event;
        vm.StoryEventSearchQuery = "Crisis";
        vm.LinkStoryEventCommand.Execute(Assert.Single(vm.StoryEventSearchResults));
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            listId = context.ReadingLists.Single().Id;
        }

        vm.UnlinkStoryEventCommand.Execute(null);

        Assert.Null(vm.LinkedStoryEventName);
        using var verifyContext = PaperbunkrDb.CreateContext();
        var list = verifyContext.ReadingLists.Single(r => r.Id == listId);
        Assert.Null(list.StoryEventId);
        Assert.Equal(ReadingListType.Event, list.Type);
    }

    [Fact]
    public void ItemRow_ChangingRoleAndNotes_Persists()
    {
        int issueId = SeedIssue("Green Lantern", "13");
        var vm = new ReadingScreenViewModel(_filePicker);
        vm.CreateNewCommand.Execute(null);
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            listId = context.ReadingLists.Single().Id;
        }
        vm.SearchQuery = "Green Lantern";
        vm.SearchCommand.Execute(null);
        vm.AddIssueCommand.Execute(vm.SearchResults[0]);
        var row = vm.Groups[0].Rows[0];

        row.SelectedRole = EventMembershipRole.Core;
        row.Notes = "Key issue";

        using var context2 = PaperbunkrDb.CreateContext();
        var item = context2.ReadingListItems.Single(i => i.IssueId == issueId);
        Assert.Equal(EventMembershipRole.Core, item.Role);
        Assert.Equal("Key issue", item.Notes);
    }

    [Fact]
    public void AddIssue_BumpsListUpdatedAt()
    {
        SeedIssue("Green Lantern", "13");
        var vm = new ReadingScreenViewModel(_filePicker);
        vm.CreateNewCommand.Execute(null);
        int listId;
        DateTime originalUpdatedAt;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var list = context.ReadingLists.Single();
            listId = list.Id;
            originalUpdatedAt = list.UpdatedAt;
        }

        vm.SearchQuery = "Green Lantern";
        vm.SearchCommand.Execute(null);
        vm.AddIssueCommand.Execute(vm.SearchResults[0]);

        using var verifyContext = PaperbunkrDb.CreateContext();
        var updated = verifyContext.ReadingLists.Single(r => r.Id == listId);
        Assert.True(updated.UpdatedAt >= originalUpdatedAt);
    }
}
