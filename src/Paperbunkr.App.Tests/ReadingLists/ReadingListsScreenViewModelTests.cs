using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises the Reading Lists screen (<see cref="ReadingListsScreenViewModel"/>, ported 2026-09-28 from the previous screen's tests) - originally the Phase 4c overhaul additions (docs/
/// superpowers/specs/2026-08-17-metadata-model-phase4c-reading-list-overhaul-design.md) - Type/
/// StoryEvent link/per-item Role+Notes. Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/>
/// to a temp SQLite file, same pattern as <see cref="ContinuityScreen.ContinuityScreenTestBase"/> -
/// The screen has no injected context-factory seam.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReadingListsScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly FakeFilePickerService _filePicker = new();

    public ReadingListsScreenViewModelTests()
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
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });

        vm.CreateNewCommand.Execute(null);

        Assert.StartsWith("Created ", vm.List.CreatedAtLabel);

        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Single();
        Assert.Equal(ReadingListType.User, list.Type);
        Assert.True(list.CreatedAt > DateTime.MinValue);
        Assert.True(list.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public void ToggleLinkStoryEvent_TogglesPanelState_AndClearsSearch()
    {
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);

        vm.List.ToggleLinkStoryEventCommand.Execute(null);
        Assert.True(vm.List.IsLinkingStoryEvent);

        vm.List.ToggleLinkStoryEventCommand.Execute(null);
        Assert.False(vm.List.IsLinkingStoryEvent);
        Assert.Equal(string.Empty, vm.List.StoryEventSearchQuery);
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

        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            listId = context.ReadingLists.Single().Id;
        }

        vm.List.StoryEventSearchQuery = "Crisis";
        var target = Assert.Single(vm.List.StoryEventSearchResults);

        vm.List.LinkStoryEventCommand.Execute(target);
        TestDispatcher.Drain();                                 // the reload is deferred one tick (the clicked result is in the list it rebuilds)

        Assert.Equal("Crisis Event", vm.List.LinkedStoryEventName);
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

        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
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
        vm.List.StoryEventSearchQuery = "Crisis";
        vm.List.LinkStoryEventCommand.Execute(Assert.Single(vm.List.StoryEventSearchResults));

        vm.List.UnlinkStoryEventCommand.Execute(null);

        Assert.Null(vm.List.LinkedStoryEventName);
        using var verifyContext = PaperbunkrDb.CreateContext();
        var verifyList = verifyContext.ReadingLists.Single(r => r.Id == listId);
        Assert.Null(verifyList.StoryEventId);
        Assert.Equal(ReadingListType.Event, verifyList.Type);
    }

    [Fact]
    public void ItemRow_ChangingRoleAndNotes_Persists()
    {
        int issueId = SeedIssue("Green Lantern", "13");
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            listId = context.ReadingLists.Single().Id;
        }
        vm.List.SearchQuery = "Green Lantern";
        vm.List.SearchCommand.Execute(null);
        vm.List.AddIssueCommand.Execute(vm.List.SearchResults[0]);
        var row = vm.List.Rows[0];

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
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId;
        DateTime originalUpdatedAt;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var list = context.ReadingLists.Single();
            listId = list.Id;
            originalUpdatedAt = list.UpdatedAt;
        }

        vm.List.SearchQuery = "Green Lantern";
        vm.List.SearchCommand.Execute(null);
        vm.List.AddIssueCommand.Execute(vm.List.SearchResults[0]);

        using var verifyContext = PaperbunkrDb.CreateContext();
        var updated = verifyContext.ReadingLists.Single(r => r.Id == listId);
        Assert.True(updated.UpdatedAt >= originalUpdatedAt);
    }

    // --- Delete whole list (docs/superpowers/specs/2026-08-22-delete-functionality-design.md); from the gallery tile's menu since the
    // redesign (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §2) - with no dialog service, no confirm is asked ---

    [Fact]
    public async Task DeleteTile_CascadeDeletesItems_ButNeverTheUnderlyingIssue()
    {
        int issueId = SeedIssue("Kilo Station", "1");
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        vm.List.SearchQuery = "Kilo Station";
        vm.List.SearchCommand.Execute(null);
        vm.List.AddIssueCommand.Execute(vm.List.SearchResults[0]);
        vm.Gallery.Refresh();

        await vm.Gallery.DeleteTileCommand.ExecuteAsync(vm.Gallery.Tiles.Single());
        TestDispatcher.Drain();

        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.ReadingLists);
        Assert.Empty(context.ReadingListItems);
        Assert.NotNull(context.Issues.Find(issueId)); // the comic itself must survive
        Assert.Empty(vm.Gallery.Tiles);
        Assert.True(vm.Gallery.HasNoLists);
    }

    [Fact]
    public void OpeningADeletedList_FallsBackToTheGallery()
    {
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int id = vm.List.ActiveListId!.Value;
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.ReadingLists.Remove(context.ReadingLists.Find(id)!);
            context.SaveChanges();
        }

        vm.LoadReadingList(id);

        Assert.True(vm.IsGalleryMode);
        Assert.Null(vm.List.ActiveListId);
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
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.List.ActiveListId!.Value;
        AddTag(listId, "Dark", IssueTagWeight.Core);

        vm.LoadReadingList(listId);

        var pill = Assert.Single(vm.List.Tags);
        Assert.Equal("Dark", pill.Value);
        Assert.Equal(IssueTagWeight.Core, pill.Weight);
        Assert.True(pill.CanReweight); // always true here - a Reading List is always one concrete list
    }

    [Fact]
    public void ClickingATagChip_OpensTheGalleryFiltered_ToListsCarryingIt()
    {
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int taggedId = vm.List.ActiveListId!.Value;
        AddTag(taggedId, "Dark");
        vm.CreateNewCommand.Execute(null); // second, untagged list
        vm.LoadReadingList(taggedId);

        vm.List.Tags.Single().SearchCommand.Execute(null);

        Assert.True(vm.IsGalleryMode);
        Assert.Equal("Dark", vm.Gallery.ActiveTag);
        var remaining = Assert.Single(vm.Gallery.Tiles);
        Assert.Equal(taggedId, remaining.Id);
    }

    [Fact]
    public void TheAllChip_RestoresEveryList()
    {
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int taggedId = vm.List.ActiveListId!.Value;
        AddTag(taggedId, "Dark");
        vm.CreateNewCommand.Execute(null);
        vm.LoadReadingList(taggedId);
        vm.List.Tags.Single().SearchCommand.Execute(null);

        vm.Gallery.SelectTagCommand.Execute(vm.Gallery.TagChips.First(c => c.Value is null));

        Assert.Null(vm.Gallery.ActiveTag);
        Assert.Equal(2, vm.Gallery.Tiles.Count);
    }

    [Fact]
    public void RightClickReweightOnTagChip_PersistsTheNewWeight()
    {
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.List.ActiveListId!.Value;
        AddTag(listId, "Dark", IssueTagWeight.Incidental);
        vm.LoadReadingList(listId);
        var pill = vm.List.Tags.Single();

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
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);

        vm.List.SearchQuery = "Kilo Station";
        vm.List.SearchCommand.Execute(null);

        Assert.Empty(vm.List.SearchResults);
    }

    [Fact]
    public void StartLinkThenAddIssue_RelinksTheTargetedRow_InsteadOfAppending()
    {
        int placeholderId = SeedPlaceholderIssue("Kilo Station", "1");
        int realIssueId = SeedIssue("Kilo Station", "1 (owned copy)");
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
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
        var row = vm.List.Rows[0];

        row.LinkCommand.Execute(null);
        Assert.True(vm.List.IsLinking);
        // Real bug found 2026-09-16: StartLink set IsLinking (showing the LinkingBannerText) but
        // never opened the search panel itself (gated on the separate IsAddIssuesOpen flag) - the
        // "Find & link" button did nothing observable on screen. The rest of this test drove
        // SearchCommand/AddIssueCommand directly, which still passed since it never checked whether
        // the panel a real user would need to click into was actually visible.
        Assert.True(vm.List.IsDrawerOpen);

        vm.List.SearchQuery = "1 (owned copy)";
        vm.List.SearchCommand.Execute(null);
        vm.List.AddIssueCommand.Execute(vm.List.SearchResults[0]);

        Assert.False(vm.List.IsLinking);
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
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            listId = context.ReadingLists.Single().Id;
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = placeholderId, SortOrder = 0 });
            context.SaveChanges();
        }
        vm.LoadReadingList(listId);
        vm.List.Rows[0].LinkCommand.Execute(null);

        vm.List.CancelLinkCommand.Execute(null);

        Assert.False(vm.List.IsLinking);
        using var verify = PaperbunkrDb.CreateContext();
        Assert.Equal(placeholderId, verify.ReadingListItems.Single(i => i.ReadingListId == listId).IssueId);
    }

    [Fact]
    public void OpenRow_ForAnOwnedIssue_InvokesGoReaderForIssueInReadingList()
    {
        int issueId = SeedIssue("Kilo Station", "1");
        int? openedIssueId = null;
        int? openedListId = null;
        var vm = new ReadingListsScreenViewModel(_filePicker, (issue, list) => { openedIssueId = issue; openedListId = list; });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.List.ActiveListId!.Value;
        vm.List.SearchQuery = "Kilo Station";
        vm.List.SearchCommand.Execute(null);
        vm.List.AddIssueCommand.Execute(vm.List.SearchResults[0]);

        vm.List.Rows[0].OpenCommand.Execute(null);

        Assert.Equal(issueId, openedIssueId);
        Assert.Equal(listId, openedListId);
    }

    [Fact]
    public void OpenRow_ForAMissingIssue_NoOps()
    {
        int placeholderId = SeedPlaceholderIssue("Kilo Station", "1");
        bool invoked = false;
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => invoked = true);
        vm.CreateNewCommand.Execute(null);
        int listId = vm.List.ActiveListId!.Value;
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = placeholderId, SortOrder = 0 });
            context.SaveChanges();
        }
        vm.LoadReadingList(listId);

        vm.List.Rows[0].OpenCommand.Execute(null);

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
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.List.ActiveListId!.Value;
        using var context = PaperbunkrDb.CreateContext();
        for (int i = 0; i < issueIds.Length; i++)
        {
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = issueIds[i], SortOrder = i });
        }
        context.SaveChanges();
        return listId;
    }

    // --- Entrance-animation v2 (docs/superpowers/specs/2026-09-12-entrance-animation-v2-design.md
    // §3) - LoadReadingList's triggerEntrance parameter distinguishes a genuine list switch/nav-in
    // from the many same-list mutation call sites that reuse this method to refresh after an edit. ---

    [Fact]
    public void LoadReadingList_TriggerEntranceTrue_SetsFlag()
    {
        int listId = SeedListWith(SeedOwnedIssue("R", "1"));
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });

        vm.LoadReadingList(listId, triggerEntrance: true);

        Assert.True(vm.List.PlayEntranceAnimation);
    }

    [Fact]
    public void LoadReadingList_DefaultTriggerEntranceFalse_LeavesFlagUnset()
    {
        int listId = SeedListWith(SeedOwnedIssue("R", "1"));
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });

        vm.LoadReadingList(listId);

        Assert.False(vm.List.PlayEntranceAnimation);
    }

    [Fact]
    public void OpeningATile_TriggersEntrance()
    {
        int listId = SeedListWith(SeedOwnedIssue("R", "1"));
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        var tile = vm.Gallery.Tiles.Single(l => l.Id == listId);

        vm.Gallery.OpenTileCommand.Execute(tile);

        Assert.True(vm.IsListMode);
        Assert.True(vm.List.PlayEntranceAnimation);
    }

    [Fact]
    public void CreateNew_TriggersEntrance()
    {
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });

        vm.CreateNewCommand.Execute(null);

        Assert.True(vm.List.PlayEntranceAnimation);
    }

    [Fact]
    public void EnsureListLoaded_TriggersEntrance()
    {
        int listId = SeedListWith(SeedOwnedIssue("R", "1"));
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);
        vm.List.PlayEntranceAnimation = false;

        vm.EnsureListLoaded();

        Assert.True(vm.List.PlayEntranceAnimation);
    }

    [Fact]
    public void AddIssue_DoesNotTriggerEntrance()
    {
        SeedOwnedIssue("Findable Series", "1");
        int listId = SeedListWith();
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);
        vm.List.SearchQuery = "Findable Series";
        vm.List.SearchCommand.Execute(null);
        vm.List.PlayEntranceAnimation = false;

        vm.List.AddIssueCommand.Execute(vm.List.SearchResults[0]);

        Assert.False(vm.List.PlayEntranceAnimation);
    }

    [Fact]
    public void RemoveSelectedMembers_DoesNotTriggerEntrance()
    {
        int listId = SeedListWith(SeedOwnedIssue("R", "1"), SeedOwnedIssue("R", "2"));
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);
        vm.List.ToggleMemberSelectionCommand.Execute(vm.List.Rows.First());
        vm.List.PlayEntranceAnimation = false;

        vm.List.RemoveSelectedMembersCommand.Execute(null);

        Assert.False(vm.List.PlayEntranceAnimation);
    }

    [Fact]
    public void Progress_CountsReadOwnedAndMissing()
    {
        int listId = SeedListWith(
            SeedOwnedIssue("A", "1", read: true),
            SeedOwnedIssue("A", "2", read: false),
            SeedPlaceholderIssue("A", "3"));

        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        Assert.Equal(3, vm.List.TotalCount);
        Assert.Equal(1, vm.List.ReadCount);
        Assert.Equal(2, vm.List.OwnedCount);
        Assert.Equal(1, vm.List.MissingCount);
        Assert.Equal(1.0 / 3.0, vm.List.ProgressFraction, 3);
    }

    [Fact]
    public void ContinueTarget_IsFirstOwnedUnread_SkippingReadAndMissing()
    {
        int readId = SeedOwnedIssue("B", "1", read: true);
        int missingId = SeedPlaceholderIssue("B", "2");
        int nextId = SeedOwnedIssue("B", "3", read: false);
        int listId = SeedListWith(readId, missingId, nextId);

        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        Assert.True(vm.List.HasContinueTarget);
        Assert.Equal("Continue — B #3", vm.List.ContinueLabel);   // the redesigned hero names the issue
        var rows = vm.List.Rows.ToList();
        Assert.True(rows.Single(r => r.Item.IssueId == nextId).IsNextUp);
        Assert.All(rows.Where(r => r.Item.IssueId != nextId), r => Assert.False(r.IsNextUp));
    }

    [Fact]
    public void ContinueTarget_AllRead_BecomesReReadFromStart()
    {
        int listId = SeedListWith(
            SeedOwnedIssue("C", "1", read: true),
            SeedOwnedIssue("C", "2", read: true));

        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        Assert.True(vm.List.HasContinueTarget);
        Assert.Equal("Re-read from start", vm.List.ContinueLabel);
    }

    [Fact]
    public void ContinueTarget_NoOwnedIssues_HasNoTarget()
    {
        int listId = SeedListWith(SeedPlaceholderIssue("D", "1"), SeedPlaceholderIssue("D", "2"));

        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        Assert.False(vm.List.HasContinueTarget);
    }

    [Fact]
    public void IsEmptyList_TrueForListWithNoItems()
    {
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);

        Assert.True(vm.List.IsEmptyList);
        Assert.Equal(0, vm.List.TotalCount);
    }

    [Fact]
    public void ToggleRead_MarksUnreadIssueRead_AndAdvancesContinueTarget()
    {
        int firstId = SeedOwnedIssue("E", "1", read: false);
        int secondId = SeedOwnedIssue("E", "2", read: false);
        int listId = SeedListWith(firstId, secondId);

        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);
        Assert.Equal("Start reading — E #1", vm.List.ContinueLabel);

        vm.List.Rows.Single(r => r.Item.IssueId == firstId).ToggleReadCommand.Execute(null);
        TestDispatcher.Drain();                                 // the reload is deferred one tick (the row's own menu item raised it)

        Assert.Equal(1, vm.List.ReadCount);
        Assert.Equal("Continue — E #2", vm.List.ContinueLabel);
        using var verify = PaperbunkrDb.CreateContext();
        Assert.Equal(19, verify.Issues.Single(i => i.Id == firstId).LastPageRead);
    }

    [Fact]
    public void RowPosition_Is1BasedAndContinuousAcrossGroups()
    {
        int a = SeedOwnedIssue("G", "1");
        int b = SeedOwnedIssue("G", "2");
        int c = SeedOwnedIssue("G", "3");
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.List.ActiveListId!.Value;
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = a, SortOrder = 0, GroupLabel = "Prologue" });
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = b, SortOrder = 1, GroupLabel = "Main" });
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = c, SortOrder = 2, GroupLabel = "Main" });
            context.SaveChanges();
        }

        vm.LoadReadingList(listId);

        var positions = vm.List.Rows.Select(r => r.Position).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, positions);
    }

    [Theory]
    [InlineData(0, 2, "Not started · 2 issues", "Start reading — H #1")]
    [InlineData(1, 2, "1 of 2 read", "Continue — H #2")]
    [InlineData(2, 2, "Finished", "Re-read from start")]
    public void ProgressLabelAndContinueLabel_MatchState(int readCount, int total, string progressLabel, string continueLabel)
    {
        var ids = Enumerable.Range(1, total).Select(n => SeedOwnedIssue("H", n.ToString(), read: n <= readCount)).ToArray();
        int listId = SeedListWith(ids);

        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        Assert.Equal(progressLabel, vm.List.ProgressLabel);
        Assert.Equal(continueLabel, vm.List.ContinueLabel);
    }

    // --- Bulk selection (docs/superpowers/specs/2026-08-28-bulk-selection-lists-continuities-events-design.md) ---

    [Fact]
    public void AddSelectedIssues_AddsEveryTicked_InOrder_AndClears()
    {
        int a = SeedOwnedIssue("Sel", "1");
        int b = SeedOwnedIssue("Sel", "2");
        int c = SeedOwnedIssue("Sel", "3");
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.List.ActiveListId!.Value;
        vm.LoadReadingList(listId);

        vm.List.SearchQuery = "Sel";
        Assert.Equal(3, vm.List.SearchResults.Count);
        vm.List.ToggleSearchSelectionCommand.Execute(vm.List.SearchResults[0]);
        vm.List.ToggleSearchSelectionCommand.Execute(vm.List.SearchResults[2]);
        Assert.True(vm.List.AnySearchSelected);

        vm.List.AddSelectedIssuesCommand.Execute(null);

        var ids = vm.List.Rows.Select(r => r.Item.IssueId).ToList();
        Assert.Equal(new[] { a, c }, ids);
        Assert.False(vm.List.AnySearchSelected);
    }

    [Fact]
    public void AddAllOfSeries_AddsWholeRun_SkipsDuplicates()
    {
        SeedOwnedIssue("Run", "1");
        SeedOwnedIssue("Run", "2");
        SeedOwnedIssue("Run", "3");
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.CreateNewCommand.Execute(null);
        int listId = vm.List.ActiveListId!.Value;
        vm.LoadReadingList(listId);

        vm.List.SearchQuery = "Run";
        vm.List.AddAllOfSeriesCommand.Execute(vm.List.SearchResults[0]);
        Assert.Equal(3, vm.List.Rows.Count());

        vm.List.AddAllOfSeriesCommand.Execute(vm.List.SearchResults[0]);
        Assert.Equal(3, vm.List.Rows.Count());
    }

    [Fact]
    public void RemoveSelectedMembers_RemovesTicked()
    {
        int listId = SeedListWith(SeedOwnedIssue("R", "1"), SeedOwnedIssue("R", "2"), SeedOwnedIssue("R", "3"));
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);

        var rows = vm.List.Rows.ToList();
        vm.List.ToggleMemberSelectionCommand.Execute(rows[0]);
        vm.List.ToggleMemberSelectionCommand.Execute(rows[1]);

        vm.List.RemoveSelectedMembersCommand.Execute(null);

        Assert.Single(vm.List.Rows);
        Assert.False(vm.List.AnyMembersSelected);
    }

    [Fact]
    public void MemberSelection_ClearsOnListSwitch()
    {
        int listId = SeedListWith(SeedOwnedIssue("S", "1"), SeedOwnedIssue("S", "2"));
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });
        vm.LoadReadingList(listId);
        vm.List.ToggleMemberSelectionCommand.Execute(vm.List.Rows.First());
        Assert.True(vm.List.AnyMembersSelected);

        vm.LoadReadingList(listId);

        Assert.False(vm.List.AnyMembersSelected);
    }

    [Fact]
    public void GalleryTile_CarriesCoverAndProgress()
    {
        int listId = SeedListWith(
            SeedOwnedIssue("F", "1", read: true),
            SeedOwnedIssue("F", "2", read: false));

        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });

        var tile = vm.Gallery.Tiles.Single(l => l.Id == listId);
        Assert.NotEmpty(tile.CoverKeys);
        Assert.Equal("1 / 2 read", tile.SubLine);
        Assert.Equal(0.5, tile.Progress, 3);
    }

    [Fact]
    public async Task ImportDroppedPathsAsync_ImportsComicsAndAttachesToOpenList()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"pb_rl_drop_{Guid.NewGuid():N}"));
        try
        {
            string cbz = CbzFixture.Create(Path.Combine(root.FullName, "Kilo Station 001 (2020).cbz"), pageCount: 1);
            var activity = new ActivityService(a => a(), _ => { });
            var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { }, activity: activity);
            vm.CreateNewCommand.Execute(null);
            int listId;
            using (var context = PaperbunkrDb.CreateContext())
            {
                listId = context.ReadingLists.Single().Id;
            }

            await vm.List.ImportDroppedPathsAsync(new[] { cbz });

            using var db = PaperbunkrDb.CreateContext();
            Assert.Single(db.Issues);
            Assert.Single(db.ReadingListItems.Where(i => i.ReadingListId == listId));
            Assert.Equal(1, vm.List.TotalCount);
            // Job-tracked, not a direct toast (docs/superpowers/specs/2026-09-06-feedback-
            // notification-system-design.md §6) - the import now settles as an ActivityJob.
            var job = Assert.Single(activity.RecentJobs);
            Assert.Equal(ActivityJobStatus.Succeeded, job.Status);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ImportDroppedPathsAsync_NoListOpen_IsNoOp()
    {
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });

        await vm.List.ImportDroppedPathsAsync(new[] { "Z:\\nonexistent\\Kilo Station 001.cbz" });

        Assert.False(vm.List.IsListOpen);
        using var db = PaperbunkrDb.CreateContext();
        Assert.Empty(db.Issues);
    }

    // --- SuggestBox string projections (docs/superpowers/specs/2026-09-10-suggestbox-migration-plan.md) ---

    [Fact]
    public void ArcSourceAndBulkRoleText_RoundTripThroughTheirObjectProperties()
    {
        var vm = new ReadingListsScreenViewModel(_filePicker, (_, _) => { });

        Assert.Equal(ReadingListsScreenViewModel.ArcSourceOptions.Select(o => o.DisplayName), vm.List.ArcSourceNames);
        var source = ReadingListsScreenViewModel.ArcSourceOptions.First(o => o.DisplayName != vm.List.SelectedArcSource.DisplayName);
        vm.List.SelectedArcSourceText = source.DisplayName;
        Assert.Equal(source.DisplayName, vm.List.SelectedArcSource.DisplayName);

        var kept = vm.List.SelectedArcSource;
        vm.List.SelectedArcSourceText = "no such source";
        Assert.Equal(kept, vm.List.SelectedArcSource);

        var role = ReadingListItemRowViewModel.RoleOptions.First();
        vm.List.BulkRoleText = role.Label;
        Assert.Equal(role.Role, vm.List.BulkRole!.Role);
        vm.List.BulkRoleText = "not a role";
        Assert.Null(vm.List.BulkRole);
        Assert.Equal(ReadingListItemRowViewModel.RoleOptions.Select(o => o.Label), vm.List.BulkRoleNames);
    }
}
