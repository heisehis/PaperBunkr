using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The <c>SaveChanges</c> gap-filler (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md §3):
/// a reading-list membership write that skipped <see cref="ReadingListManager"/> is announced by the context
/// itself, and a managed write is never announced twice. The backstop can only speak on
/// <see cref="LibraryEvents.Default"/>, so each test uses a uniquely named list and listens for that name only -
/// other test classes write reading-list items directly too.
/// </summary>
public class ReadingListBackstopTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly string _listName = "Backstop " + Guid.NewGuid().ToString("N");
    private readonly List<ReadingListChangedEvent> _heard = new();
    private readonly List<string> _bypasses = new();
    private readonly object _gate = new();

    public ReadingListBackstopTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rlbackstop_test_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
        LibraryEvents.Default.ReadingListChanged += OnChanged;
        LibraryEvents.Default.ManagerBypassed += OnBypassed;
    }

    public void Dispose()
    {
        LibraryEvents.Default.ReadingListChanged -= OnChanged;
        LibraryEvents.Default.ManagerBypassed -= OnBypassed;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private void OnChanged(ReadingListChangedEvent e)
    {
        if (e.ListName == _listName) lock (_gate) _heard.Add(e);
    }

    private void OnBypassed(string message)
    {
        if (message.Contains(_listName)) lock (_gate) _bypasses.Add(message);
    }

    private PaperbunkrDbContext NewContext() => new(_options);

    private (int ListId, int[] IssueIds) Seed(int issueCount, int itemsInList = 0)
    {
        using var context = NewContext();
        var series = new Series { Name = "Kilo Station", SortName = "Kilo Station" };
        context.Series.Add(series);
        var issues = Enumerable.Range(1, issueCount).Select(n => new Issue { Series = series, Number = n.ToString() }).ToList();
        context.Issues.AddRange(issues);
        var list = new ReadingList { Name = _listName, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.ReadingLists.Add(list);
        context.SaveChanges();
        for (int i = 0; i < itemsInList; i++)
        {
            // Seeding goes through the backstop too; the heard lists are cleared once seeding is done.
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = list.Id, IssueId = issues[i].Id, SortOrder = i });
        }

        context.SaveChanges();
        lock (_gate)
        {
            _heard.Clear();
            _bypasses.Clear();
        }

        return (list.Id, issues.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void Direct_add_is_announced_after_the_save_and_flagged_as_a_bypass()
    {
        var (listId, issueIds) = Seed(2);
        using var context = NewContext();

        context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = issueIds[0], SortOrder = 0 });
        context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = issueIds[1], SortOrder = 1 });
        Assert.Empty(_heard);
        context.SaveChanges();

        var change = Assert.Single(_heard);
        Assert.Equal(listId, change.ListId);
        Assert.Equal(ReadingListChangeKind.Added, change.Kind);
        Assert.Equal(issueIds, change.AddedIssueIds.ToArray());
        Assert.Single(_bypasses);
    }

    [Fact]
    public void Direct_remove_is_announced_as_removed()
    {
        var (listId, issueIds) = Seed(2, itemsInList: 2);
        using var context = NewContext();

        context.ReadingListItems.RemoveRange(context.ReadingListItems.Where(i => i.ReadingListId == listId && i.IssueId == issueIds[1]));
        context.SaveChanges();

        var change = Assert.Single(_heard);
        Assert.Equal(ReadingListChangeKind.Removed, change.Kind);
        Assert.Equal(new[] { issueIds[1] }, change.RemovedIssueIds.ToArray());
    }

    [Fact]
    public void Managed_add_is_not_announced_twice_or_flagged()
    {
        var (listId, issueIds) = Seed(2);
        var hub = new LibraryEvents();
        var managed = new List<ReadingListChangedEvent>();
        hub.ReadingListChanged += managed.Add;
        using var context = NewContext();

        ReadingListManager.AddIssues(context, listId, issueIds, hub);
        context.SaveChanges();

        Assert.Single(managed);
        Assert.Empty(_heard);
        Assert.Empty(_bypasses);
    }

    [Fact]
    public void Managed_removal_and_reorder_are_not_flagged()
    {
        var (listId, issueIds) = Seed(3, itemsInList: 3);
        var hub = new LibraryEvents();
        using var context = NewContext();
        int firstItem = context.ReadingListItems.Where(i => i.ReadingListId == listId).OrderBy(i => i.SortOrder).Select(i => i.Id).First();

        ReadingListManager.MoveItem(context, listId, firstItem, +1, hub);
        ReadingListManager.RemoveItems(context, listId, new[] { firstItem }, hub);
        context.SaveChanges();

        Assert.Empty(_heard);
        Assert.Empty(_bypasses);
    }

    [Fact]
    public void Created_with_items_via_the_manager_is_covered_even_before_it_has_an_id()
    {
        var (_, issueIds) = Seed(2);
        var hub = new LibraryEvents();
        using var context = NewContext();
        var list = new ReadingList { Name = _listName + " imported", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        list.Items.Add(new ReadingListItem { IssueId = issueIds[0], SortOrder = 0 });
        context.ReadingLists.Add(list);
        ReadingListManager.RecordCreatedWithItems(context, list, events: hub);

        context.SaveChanges();

        Assert.Empty(_heard);
        Assert.Empty(_bypasses);
    }

    [Fact]
    public void Record_with_kind_none_still_covers_the_lists_deleted_rows()
    {
        var (listId, issueIds) = Seed(2, itemsInList: 2);
        using var context = NewContext();
        var list = context.ReadingLists.Include(l => l.Items).Single(l => l.Id == listId);

        // An arc refresh that only tidied duplicate rows: it deletes items but has nothing to announce.
        context.ReadingListItems.Remove(list.Items.First());
        ReadingListManager.Record(context, list, ReadingListChangeKind.None, Array.Empty<int>(), Array.Empty<int>(), new LibraryEvents());
        context.SaveChanges();

        Assert.Empty(_heard);
        Assert.Empty(_bypasses);
    }

    [Fact]
    public void New_list_with_items_added_directly_is_announced_with_its_assigned_id()
    {
        var (_, issueIds) = Seed(2);
        using var context = NewContext();
        var list = new ReadingList { Name = _listName, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        list.Items.Add(new ReadingListItem { IssueId = issueIds[0], SortOrder = 0 });
        context.ReadingLists.Add(list);

        context.SaveChanges();

        var change = Assert.Single(_heard);
        Assert.NotEqual(0, change.ListId);
        Assert.Equal(list.Id, change.ListId);
        Assert.Equal(new[] { issueIds[0] }, change.AddedIssueIds.ToArray());
    }

    [Fact]
    public void Abandoned_context_announces_nothing()
    {
        var (listId, issueIds) = Seed(1);
        using (var context = NewContext())
        {
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = issueIds[0], SortOrder = 0 });
        }

        Assert.Empty(_heard);
        Assert.Empty(_bypasses);
    }

    [Fact]
    public void Relinking_an_item_to_another_issue_is_not_a_membership_change()
    {
        var (listId, issueIds) = Seed(2, itemsInList: 1);
        using var context = NewContext();

        var item = context.ReadingListItems.Single(i => i.ReadingListId == listId);
        item.IssueId = issueIds[1];
        context.SaveChanges();

        Assert.Empty(_heard);
        Assert.Empty(_bypasses);
    }
}
