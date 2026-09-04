using Microsoft.EntityFrameworkCore;
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

    private static int SeedPlaceholderIssue(string seriesName, string number)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.FirstOrDefault(s => s.Name == seriesName) ?? new Series { Name = seriesName };
        if (series.Id == 0)
        {
            context.Series.Add(series);
            context.SaveChanges();
        }

        var issue = new Issue { SeriesId = series.Id, Number = number, IsPlaceholder = true, FileIsMissing = true };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void CreateNew_DefaultsToTypeUser_SetsTimestamps()
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });

        vm.CreateNewCommand.Execute(null);

        Assert.StartsWith("Created ", vm.CreatedAtLabel);

        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Single();
        Assert.Equal(ReadingListType.User, list.Type);
        Assert.True(list.CreatedAt > DateTime.MinValue);
        Assert.True(list.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public void ToggleLinkStoryEvent_TogglesPanelState_AndClearsSearch()
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
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

        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
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

        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var list = context.ReadingLists.Single();
            list.Type = ReadingListType.Event;
            context.SaveChanges();
            listId = list.Id;
        }

        vm.LoadReadingList(listId);
        vm.StoryEventSearchQuery = "Crisis";
        vm.LinkStoryEventCommand.Execute(Assert.Single(vm.StoryEventSearchResults));

        vm.UnlinkStoryEventCommand.Execute(null);

        Assert.Null(vm.LinkedStoryEventName);
        using var verifyContext = PaperbunkrDb.CreateContext();
        var verifyList = verifyContext.ReadingLists.Single(r => r.Id == listId);
        Assert.Null(verifyList.StoryEventId);
        Assert.Equal(ReadingListType.Event, verifyList.Type);
    }

    [Fact]
    public void ItemRow_ChangingRoleAndNotes_Persists()
    {
        int issueId = SeedIssue("Green Lantern", "13");
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
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
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
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

    // --- Delete whole list (docs/superpowers/specs/2026-08-22-delete-functionality-design.md) ---

    [Fact]
    public void DeleteConfirm_Trigger_RequiresTwoClicksBeforeDeleting()
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        var summary = vm.Lists.Single();

        summary.DeleteConfirm.TriggerCommand.Execute(null);
        Assert.True(summary.DeleteConfirm.IsArmed);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(1, context.ReadingLists.Count());
        }

        summary.DeleteConfirm.TriggerCommand.Execute(null);
        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.Equal(0, verifyContext.ReadingLists.Count());
    }

    [Fact]
    public void Delete_CascadeDeletesItems_ButNeverTheUnderlyingIssue()
    {
        int issueId = SeedIssue("Kilo Station", "1");
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        vm.SearchQuery = "Kilo Station";
        vm.SearchCommand.Execute(null);
        vm.AddIssueCommand.Execute(vm.SearchResults[0]);

        var summary = vm.Lists.Single();
        summary.DeleteConfirm.TriggerCommand.Execute(null);
        summary.DeleteConfirm.TriggerCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.ReadingListItems);
        Assert.NotNull(context.Issues.Find(issueId)); // the comic itself must survive
    }

    [Fact]
    public void Delete_OfTheActiveList_FallsBackToAnotherList()
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null); // "New Reading List" #1, becomes active
        int firstId = vm.Lists.Single(l => l.IsActive).Id;
        vm.CreateNewCommand.Execute(null); // #2, becomes active instead
        var firstSummary = vm.Lists.Single(l => l.Id == firstId);

        var activeSummary = vm.Lists.Single(l => l.IsActive);
        activeSummary.DeleteConfirm.TriggerCommand.Execute(null);
        activeSummary.DeleteConfirm.TriggerCommand.Execute(null);

        Assert.Equal(firstId, vm.Lists.Single().Id); // only the un-deleted one remains
        Assert.False(string.IsNullOrEmpty(vm.ListName)); // fell back to displaying it, not a blank screen
    }

    [Fact]
    public void Delete_OfTheLastRemainingList_ClearsTheScreen()
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        var summary = vm.Lists.Single();

        summary.DeleteConfirm.TriggerCommand.Execute(null);
        summary.DeleteConfirm.TriggerCommand.Execute(null);

        Assert.True(vm.HasNoReadingLists);
        Assert.Equal(string.Empty, vm.ListName);
    }

    // --- Reading List tags (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md) ---

    private static void AddTag(int listId, string value, IssueTagWeight weight = IssueTagWeight.Unset)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Include(r => r.Tags).Single(r => r.Id == listId);
        list.Tags.Add(new ReadingListTag { ReadingListId = listId, Value = value, Weight = weight });
        context.SaveChanges();
    }

    [Fact]
    public void LoadReadingList_PopulatesTagsChipRow_HighestFirst()
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.Lists.Single().Id;
        AddTag(listId, "Dark", IssueTagWeight.Core);

        vm.LoadReadingList(listId);

        var pill = Assert.Single(vm.Tags);
        Assert.Equal("Dark", pill.Value);
        Assert.Equal(IssueTagWeight.Core, pill.Weight);
        Assert.True(pill.CanReweight); // always true here - a Reading List is always one concrete list
    }

    [Fact]
    public void ClickingATagChip_FiltersTheSidebar_ToListsCarryingIt()
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int taggedId = vm.Lists.Single().Id;
        AddTag(taggedId, "Dark");
        vm.CreateNewCommand.Execute(null); // second, untagged list
        vm.LoadReadingList(taggedId);

        vm.Tags.Single().SearchCommand.Execute(null);

        Assert.True(vm.HasActiveTagFilter);
        Assert.Equal("Dark", vm.ActiveTagFilter);
        var remaining = Assert.Single(vm.Lists);
        Assert.Equal(taggedId, remaining.Id);
    }

    [Fact]
    public void ClearTagFilter_RestoresEveryList()
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int taggedId = vm.Lists.Single().Id;
        AddTag(taggedId, "Dark");
        vm.CreateNewCommand.Execute(null);
        vm.LoadReadingList(taggedId);
        vm.Tags.Single().SearchCommand.Execute(null);

        vm.ClearTagFilterCommand.Execute(null);

        Assert.False(vm.HasActiveTagFilter);
        Assert.Equal(2, vm.Lists.Count);
    }

    [Fact]
    public void RightClickReweightOnTagChip_PersistsTheNewWeight()
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.Lists.Single().Id;
        AddTag(listId, "Dark", IssueTagWeight.Incidental);
        vm.LoadReadingList(listId);
        var pill = vm.Tags.Single();

        pill.SetWeightCommand.Execute(IssueTagWeight.Core);

        Assert.Equal(IssueTagWeight.Core, pill.Weight);
        using var verify = PaperbunkrDb.CreateContext();
        var tag = verify.ReadingListTags.Single(t => t.ReadingListId == listId);
        Assert.Equal(IssueTagWeight.Core, tag.Weight);
    }

    // --- Manual relink + click-to-read (docs/superpowers/specs/2026-08-23-cbl-manager-manual-
    // editing-and-list-aware-reading-design.md §1/§2) ---

    [Fact]
    public void Search_ExcludesPlaceholderIssues()
    {
        SeedPlaceholderIssue("Kilo Station", "1");
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);

        vm.SearchQuery = "Kilo Station";
        vm.SearchCommand.Execute(null);

        Assert.Empty(vm.SearchResults);
    }

    [Fact]
    public void StartLinkThenAddIssue_RelinksTheTargetedRow_InsteadOfAppending()
    {
        int placeholderId = SeedPlaceholderIssue("Kilo Station", "1");
        int realIssueId = SeedIssue("Kilo Station", "1 (owned copy)");
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var list = context.ReadingLists.Single();
            listId = list.Id;
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = placeholderId, SortOrder = 0, Notes = "keep me" });
            context.SaveChanges();
        }
        vm.LoadReadingList(listId);
        var row = vm.Groups[0].Rows[0];

        row.LinkCommand.Execute(null);
        Assert.True(vm.IsLinking);

        vm.SearchQuery = "1 (owned copy)";
        vm.SearchCommand.Execute(null);
        vm.AddIssueCommand.Execute(vm.SearchResults[0]);

        Assert.False(vm.IsLinking);
        using var verify = PaperbunkrDb.CreateContext();
        var items = verify.ReadingListItems.Where(i => i.ReadingListId == listId).ToList();
        var relinked = Assert.Single(items); // relinked in place, not appended as a second row
        Assert.Equal(realIssueId, relinked.IssueId);
        Assert.Equal("keep me", relinked.Notes);
        Assert.Null(verify.Issues.FirstOrDefault(i => i.Id == placeholderId)); // orphaned placeholder cleaned up
    }

    [Fact]
    public void CancelLink_LeavesTheListUnmodified()
    {
        int placeholderId = SeedPlaceholderIssue("Kilo Station", "1");
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            listId = context.ReadingLists.Single().Id;
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = placeholderId, SortOrder = 0 });
            context.SaveChanges();
        }
        vm.LoadReadingList(listId);
        vm.Groups[0].Rows[0].LinkCommand.Execute(null);

        vm.CancelLinkCommand.Execute(null);

        Assert.False(vm.IsLinking);
        using var verify = PaperbunkrDb.CreateContext();
        Assert.Equal(placeholderId, verify.ReadingListItems.Single(i => i.ReadingListId == listId).IssueId);
    }

    [Fact]
    public void OpenRow_ForAnOwnedIssue_InvokesGoReaderForIssueInReadingList()
    {
        int issueId = SeedIssue("Kilo Station", "1");
        int? openedIssueId = null;
        int? openedListId = null;
        var vm = new ReadingScreenViewModel(_filePicker, (issue, list) => { openedIssueId = issue; openedListId = list; });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.Lists.Single().Id;
        vm.SearchQuery = "Kilo Station";
        vm.SearchCommand.Execute(null);
        vm.AddIssueCommand.Execute(vm.SearchResults[0]);

        vm.Groups[0].Rows[0].OpenCommand.Execute(null);

        Assert.Equal(issueId, openedIssueId);
        Assert.Equal(listId, openedListId);
    }

    [Fact]
    public void OpenRow_ForAMissingIssue_NoOps()
    {
        int placeholderId = SeedPlaceholderIssue("Kilo Station", "1");
        bool invoked = false;
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => invoked = true);
        vm.CreateNewCommand.Execute(null);
        int listId = vm.Lists.Single().Id;
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = placeholderId, SortOrder = 0 });
            context.SaveChanges();
        }
        vm.LoadReadingList(listId);

        vm.Groups[0].Rows[0].OpenCommand.Execute(null);

        Assert.False(invoked);
    }

    // --- Redesign: progress + Continue + mark-read (docs/superpowers/specs/
    //     2026-08-28-reading-lists-screen-redesign-design.md) ---

    /// <summary>An owned issue with a page count, optionally already read to the end.</summary>
    private static int SeedOwnedIssue(string seriesName, string number, bool read = false)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.FirstOrDefault(s => s.Name == seriesName) ?? new Series { Name = seriesName };
        if (series.Id == 0)
        {
            context.Series.Add(series);
            context.SaveChanges();
        }

        var issue = new Issue
        {
            SeriesId = series.Id,
            Number = number,
            FilePath = $@"C:\lib\{seriesName}-{number}.cbz",
            FileIsMissing = false,
            PageCount = 20,
            LastPageRead = read ? 19 : 0,
        };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private int SeedListWith(params int[] issueIds)
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.Lists.Single().Id;
        using var context = PaperbunkrDb.CreateContext();
        for (int i = 0; i < issueIds.Length; i++)
        {
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = issueIds[i], SortOrder = i });
        }
        context.SaveChanges();
        return listId;
    }

    [Fact]
    public void Progress_CountsReadOwnedAndMissing()
    {
        int listId = SeedListWith(
            SeedOwnedIssue("A", "1", read: true),
            SeedOwnedIssue("A", "2", read: false),
            SeedPlaceholderIssue("A", "3"));

        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        Assert.Equal(3, vm.TotalCount);
        Assert.Equal(1, vm.ReadCount);
        Assert.Equal(2, vm.OwnedCount);
        Assert.Equal(1, vm.MissingCount);
        Assert.Equal(1.0 / 3.0, vm.ProgressFraction, 3);
    }

    [Fact]
    public void ContinueTarget_IsFirstOwnedUnread_SkippingReadAndMissing()
    {
        int readId = SeedOwnedIssue("B", "1", read: true);
        int missingId = SeedPlaceholderIssue("B", "2");
        int nextId = SeedOwnedIssue("B", "3", read: false);
        int listId = SeedListWith(readId, missingId, nextId);

        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        Assert.True(vm.HasContinueTarget);
        Assert.Equal("Continue — 3", vm.ContinueLabel);
        var rows = vm.Groups.SelectMany(g => g.Rows).ToList();
        Assert.True(rows.Single(r => r.Item.IssueId == nextId).IsNextUp);
        Assert.All(rows.Where(r => r.Item.IssueId != nextId), r => Assert.False(r.IsNextUp));
    }

    [Fact]
    public void ContinueTarget_AllRead_BecomesReReadFromStart()
    {
        int listId = SeedListWith(
            SeedOwnedIssue("C", "1", read: true),
            SeedOwnedIssue("C", "2", read: true));

        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        Assert.True(vm.HasContinueTarget);
        Assert.Equal("Re-read from start", vm.ContinueLabel);
    }

    [Fact]
    public void ContinueTarget_NoOwnedIssues_HasNoTarget()
    {
        int listId = SeedListWith(SeedPlaceholderIssue("D", "1"), SeedPlaceholderIssue("D", "2"));

        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        Assert.False(vm.HasContinueTarget);
    }

    [Fact]
    public void IsEmptyList_TrueForListWithNoItems()
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);

        Assert.True(vm.IsEmptyList);
        Assert.Equal(0, vm.TotalCount);
    }

    [Fact]
    public void ToggleRead_MarksUnreadIssueRead_AndAdvancesContinueTarget()
    {
        int firstId = SeedOwnedIssue("E", "1", read: false);
        int secondId = SeedOwnedIssue("E", "2", read: false);
        int listId = SeedListWith(firstId, secondId);

        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);
        Assert.Equal("Start reading", vm.ContinueLabel);

        vm.Groups.SelectMany(g => g.Rows).Single(r => r.Item.IssueId == firstId).ToggleReadCommand.Execute(null);

        Assert.Equal(1, vm.ReadCount);
        Assert.Equal("Continue — 2", vm.ContinueLabel);
        using var verify = PaperbunkrDb.CreateContext();
        Assert.Equal(19, verify.Issues.Single(i => i.Id == firstId).LastPageRead);
    }

    [Fact]
    public void RowPosition_Is1BasedAndContinuousAcrossGroups()
    {
        int a = SeedOwnedIssue("G", "1");
        int b = SeedOwnedIssue("G", "2");
        int c = SeedOwnedIssue("G", "3");
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.Lists.Single().Id;
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = a, SortOrder = 0, GroupLabel = "Prologue" });
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = b, SortOrder = 1, GroupLabel = "Main" });
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = c, SortOrder = 2, GroupLabel = "Main" });
            context.SaveChanges();
        }

        vm.LoadReadingList(listId);

        var positions = vm.Groups.SelectMany(g => g.Rows).Select(r => r.Position).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, positions);
    }

    [Theory]
    [InlineData(0, 2, "Not started · 2 issues", "Start reading")]
    [InlineData(1, 2, "1 of 2 read", "Continue — 2")]
    [InlineData(2, 2, "Finished", "Re-read from start")]
    public void ProgressLabelAndContinueLabel_MatchState(int readCount, int total, string progressLabel, string continueLabel)
    {
        var ids = Enumerable.Range(1, total).Select(n => SeedOwnedIssue("H", n.ToString(), read: n <= readCount)).ToArray();
        int listId = SeedListWith(ids);

        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        Assert.Equal(progressLabel, vm.ProgressLabel);
        Assert.Equal(continueLabel, vm.ContinueLabel);
    }

    // --- Bulk selection (docs/superpowers/specs/2026-08-28-bulk-selection-lists-continuities-events-design.md) ---

    [Fact]
    public void AddSelectedIssues_AddsEveryTicked_InOrder_AndClears()
    {
        int a = SeedOwnedIssue("Sel", "1");
        int b = SeedOwnedIssue("Sel", "2");
        int c = SeedOwnedIssue("Sel", "3");
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.Lists.Single().Id;
        vm.LoadReadingList(listId);

        vm.SearchQuery = "Sel";
        Assert.Equal(3, vm.SearchResults.Count);
        vm.ToggleSearchSelectionCommand.Execute(vm.SearchResults[0]);
        vm.ToggleSearchSelectionCommand.Execute(vm.SearchResults[2]);
        Assert.True(vm.AnySearchSelected);

        vm.AddSelectedIssuesCommand.Execute(null);

        var ids = vm.Groups.SelectMany(g => g.Rows).Select(r => r.Item.IssueId).ToList();
        Assert.Equal(new[] { a, c }, ids);
        Assert.False(vm.AnySearchSelected);
    }

    [Fact]
    public void AddAllOfSeries_AddsWholeRun_SkipsDuplicates()
    {
        SeedOwnedIssue("Run", "1");
        SeedOwnedIssue("Run", "2");
        SeedOwnedIssue("Run", "3");
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.Lists.Single().Id;
        vm.LoadReadingList(listId);

        vm.SearchQuery = "Run";
        vm.AddAllOfSeriesCommand.Execute(vm.SearchResults[0]);
        Assert.Equal(3, vm.Groups.SelectMany(g => g.Rows).Count());

        vm.AddAllOfSeriesCommand.Execute(vm.SearchResults[0]);
        Assert.Equal(3, vm.Groups.SelectMany(g => g.Rows).Count());
    }

    [Fact]
    public void RemoveSelectedMembers_RemovesTicked()
    {
        int listId = SeedListWith(SeedOwnedIssue("R", "1"), SeedOwnedIssue("R", "2"), SeedOwnedIssue("R", "3"));
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        var rows = vm.Groups.SelectMany(g => g.Rows).ToList();
        vm.ToggleMemberSelectionCommand.Execute(rows[0]);
        vm.ToggleMemberSelectionCommand.Execute(rows[1]);

        vm.RemoveSelectedMembersCommand.Execute(null);

        Assert.Single(vm.Groups.SelectMany(g => g.Rows));
        Assert.False(vm.AnyMembersSelected);
    }

    [Fact]
    public void MemberSelection_ClearsOnListSwitch()
    {
        int listId = SeedListWith(SeedOwnedIssue("S", "1"), SeedOwnedIssue("S", "2"));
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);
        vm.ToggleMemberSelectionCommand.Execute(vm.Groups.SelectMany(g => g.Rows).First());
        Assert.True(vm.AnyMembersSelected);

        vm.LoadReadingList(listId);

        Assert.False(vm.AnyMembersSelected);
    }

    [Fact]
    public void SidebarSummary_CarriesCoverAndProgress()
    {
        int listId = SeedListWith(
            SeedOwnedIssue("F", "1", read: true),
            SeedOwnedIssue("F", "2", read: false));

        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        var summary = vm.Lists.Single(l => l.Id == listId);
        Assert.NotNull(summary.CoverIssueId);
        Assert.Equal(1, summary.ReadCount);
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(0.5, summary.ProgressFraction, 3);
    }

    [Fact]
    public async Task ImportDroppedPathsAsync_ImportsComicsAndAttachesToOpenList()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"pb_rl_drop_{Guid.NewGuid():N}"));
        try
        {
            string cbz = CbzFixture.Create(Path.Combine(root.FullName, "Kilo Station 001 (2020).cbz"), pageCount: 1);
            (string Title, string Message)? toast = null;
            var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { }, showToast: (t, m) => toast = (t, m));
            vm.CreateNewCommand.Execute(null);
            int listId;
            using (var context = PaperbunkrDb.CreateContext())
            {
                listId = context.ReadingLists.Single().Id;
            }

            await vm.ImportDroppedPathsAsync(new[] { cbz });

            using var db = PaperbunkrDb.CreateContext();
            Assert.Single(db.Issues);
            Assert.Single(db.ReadingListItems.Where(i => i.ReadingListId == listId));
            Assert.Equal(1, vm.TotalCount);
            Assert.NotNull(toast);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ImportDroppedPathsAsync_NoListOpen_IsNoOp()
    {
        var vm = new ReadingScreenViewModel(_filePicker, (_, _) => { });

        await vm.ImportDroppedPathsAsync(new[] { "Z:\\nonexistent\\Kilo Station 001.cbz" });

        Assert.False(vm.IsListOpen);
        using var db = PaperbunkrDb.CreateContext();
        Assert.Empty(db.Issues);
    }
}
