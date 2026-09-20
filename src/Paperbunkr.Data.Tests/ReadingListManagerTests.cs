using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <see cref="ReadingListManager"/> (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §5.4).
/// The properties that matter: a change is announced only after the caller's <c>SaveChanges</c>
/// succeeds (never before, never for an abandoned or failed save); one operation is one announcement;
/// and nothing is announced when nothing changed. Every test passes its own <see cref="LibraryEvents"/>
/// so tests can't hear each other.
/// </summary>
public class ReadingListManagerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly LibraryEvents _events = new();
    private readonly List<ReadingListChangedEvent> _heard = new();

    public ReadingListManagerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rlmanager_test_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
        _events.ReadingListChanged += _heard.Add;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext NewContext() => new(_options);

    private (int ListId, int[] IssueIds) Seed(int issueCount, string listName = "Signal War", int itemsAlreadyInList = 0)
    {
        using var context = NewContext();
        var series = new Series { Name = "Kilo Station", SortName = "Kilo Station" };
        context.Series.Add(series);
        var issues = Enumerable.Range(1, issueCount).Select(n => new Issue { Series = series, Number = n.ToString() }).ToList();
        context.Issues.AddRange(issues);
        var list = new ReadingList { Name = listName, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow.AddDays(-1) };
        context.ReadingLists.Add(list);
        context.SaveChanges();

        for (int i = 0; i < itemsAlreadyInList; i++)
        {
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = list.Id, IssueId = issues[i].Id, SortOrder = i });
        }

        context.SaveChanges();
        return (list.Id, issues.Select(i => i.Id).ToArray());
    }

    // ---- AddIssues ----

    [Fact]
    public void AddIssues_announces_only_after_the_save_and_only_once()
    {
        var (listId, issueIds) = Seed(3);
        using var context = NewContext();

        var result = ReadingListManager.AddIssues(context, listId, issueIds, _events);

        Assert.Equal(3, result.Added);
        Assert.Empty(_heard);           // staged, not announced - the caller hasn't saved yet
        context.SaveChanges();

        var change = Assert.Single(_heard);
        Assert.Equal(listId, change.ListId);
        Assert.Equal("Signal War", change.ListName);
        Assert.Equal(ReadingListChangeKind.Added, change.Kind);
        Assert.Equal(issueIds, change.AddedIssueIds);
        Assert.Empty(change.RemovedIssueIds);
    }

    [Fact]
    public void A_later_save_on_the_same_context_does_not_announce_again()
    {
        var (listId, issueIds) = Seed(2);
        using var context = NewContext();
        ReadingListManager.AddIssues(context, listId, issueIds, _events);
        context.SaveChanges();

        context.Series.First().Name = "Renamed";
        context.SaveChanges();

        Assert.Single(_heard);
    }

    [Fact]
    public void An_abandoned_unit_of_work_announces_nothing()
    {
        var (listId, issueIds) = Seed(2);

        using (var context = NewContext())
        {
            ReadingListManager.AddIssues(context, listId, issueIds, _events);
            // disposed without SaveChanges
        }

        Assert.Empty(_heard);
        using var verify = NewContext();
        Assert.Empty(verify.ReadingListItems);
    }

    [Fact]
    public void A_failed_save_announces_nothing()
    {
        var (listId, issueIds) = Seed(1);
        using var context = NewContext();
        ReadingListManager.AddIssues(context, listId, issueIds, _events);

        // Break the unit of work: an item pointing at an issue that doesn't exist violates the FK.
        context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = 999_999, SortOrder = 9 });

        Assert.ThrowsAny<DbUpdateException>(() => context.SaveChanges());
        Assert.Empty(_heard);
    }

    [Fact]
    public void AddIssues_skips_issues_already_in_the_list_and_appends_after_the_last_order()
    {
        var (listId, issueIds) = Seed(4, itemsAlreadyInList: 2);
        using var context = NewContext();

        var result = ReadingListManager.AddIssues(context, listId, issueIds, _events);
        context.SaveChanges();

        Assert.Equal(2, result.Added);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(new[] { issueIds[2], issueIds[3] }, result.AddedIssueIds);

        using var verify = NewContext();
        var ordered = verify.ReadingListItems.Where(i => i.ReadingListId == listId).OrderBy(i => i.SortOrder).ToList();
        Assert.Equal(new[] { 0, 1, 2, 3 }, ordered.Select(i => i.SortOrder));
        Assert.Equal(issueIds, ordered.Select(i => i.IssueId));
        Assert.Equal(new[] { issueIds[2], issueIds[3] }, Assert.Single(_heard).AddedIssueIds);
    }

    [Fact]
    public void AddIssues_announces_nothing_when_every_issue_is_already_in_the_list()
    {
        var (listId, issueIds) = Seed(2, itemsAlreadyInList: 2);
        using var context = NewContext();

        var result = ReadingListManager.AddIssues(context, listId, issueIds, _events);
        context.SaveChanges();

        Assert.Equal(0, result.Added);
        Assert.Equal(2, result.Skipped);
        Assert.Empty(_heard);
    }

    [Fact]
    public void AddIssues_bumps_UpdatedAt_only_when_something_was_added()
    {
        var (listId, issueIds) = Seed(2, itemsAlreadyInList: 2);
        DateTime before;
        using (var read = NewContext())
        {
            before = read.ReadingLists.Find(listId)!.UpdatedAt;
        }

        using (var noOp = NewContext())
        {
            ReadingListManager.AddIssues(noOp, listId, issueIds, _events);
            noOp.SaveChanges();
        }

        using (var check = NewContext())
        {
            Assert.Equal(before, check.ReadingLists.Find(listId)!.UpdatedAt);
        }

        var (freshListId, freshIssueIds) = Seed(1);
        using (var real = NewContext())
        {
            DateTime freshBefore = real.ReadingLists.Find(freshListId)!.UpdatedAt;
            ReadingListManager.AddIssues(real, freshListId, freshIssueIds, _events);
            real.SaveChanges();
            Assert.True(real.ReadingLists.Find(freshListId)!.UpdatedAt > freshBefore);
        }
    }

    [Fact]
    public void A_large_import_style_add_is_one_announcement()
    {
        var (listId, issueIds) = Seed(300);
        using var context = NewContext();

        ReadingListManager.AddIssues(context, listId, issueIds, _events);
        context.SaveChanges();

        var change = Assert.Single(_heard);
        Assert.Equal(300, change.AddedIssueIds.Count);
    }

    // ---- RemoveItems ----

    [Fact]
    public void RemoveItems_removes_and_announces_the_removed_issue_ids()
    {
        var (listId, issueIds) = Seed(3, itemsAlreadyInList: 3);
        int[] itemIds;
        using (var read = NewContext())
        {
            itemIds = read.ReadingListItems.Where(i => i.IssueId == issueIds[0] || i.IssueId == issueIds[2]).Select(i => i.Id).ToArray();
        }

        using var context = NewContext();
        int removed = ReadingListManager.RemoveItems(context, listId, itemIds, _events);
        Assert.Equal(2, removed);
        Assert.Empty(_heard);
        context.SaveChanges();

        var change = Assert.Single(_heard);
        Assert.Equal(ReadingListChangeKind.Removed, change.Kind);
        Assert.Empty(change.AddedIssueIds);
        Assert.Equal(new[] { issueIds[0], issueIds[2] }.OrderBy(x => x), change.RemovedIssueIds.OrderBy(x => x));

        using var verify = NewContext();
        Assert.Equal(issueIds[1], Assert.Single(verify.ReadingListItems).IssueId);
    }

    [Fact]
    public void RemoveItems_ignores_ids_that_belong_to_a_different_list()
    {
        var (listA, issuesA) = Seed(1, "A", itemsAlreadyInList: 1);
        var (listB, _) = Seed(1, "B", itemsAlreadyInList: 1);
        int itemInA;
        using (var read = NewContext())
        {
            itemInA = read.ReadingListItems.Single(i => i.ReadingListId == listA).Id;
        }

        using var context = NewContext();
        int removed = ReadingListManager.RemoveItems(context, listB, new[] { itemInA }, _events);
        context.SaveChanges();

        Assert.Equal(0, removed);
        Assert.Empty(_heard);
        using var verify = NewContext();
        Assert.Equal(issuesA[0], verify.ReadingListItems.Single(i => i.ReadingListId == listA).IssueId);
    }

    // ---- MoveItem ----

    [Fact]
    public void MoveItem_swaps_with_the_neighbour_and_announces_a_reorder()
    {
        var (listId, issueIds) = Seed(3, itemsAlreadyInList: 3);
        int middleItem;
        using (var read = NewContext())
        {
            middleItem = read.ReadingListItems.Single(i => i.IssueId == issueIds[1]).Id;
        }

        using var context = NewContext();
        Assert.True(ReadingListManager.MoveItem(context, listId, middleItem, offset: -1, _events));
        context.SaveChanges();

        var change = Assert.Single(_heard);
        Assert.Equal(ReadingListChangeKind.Reordered, change.Kind);
        Assert.Empty(change.AddedIssueIds);
        Assert.Empty(change.RemovedIssueIds);

        using var verify = NewContext();
        Assert.Equal(
            new[] { issueIds[1], issueIds[0], issueIds[2] },
            verify.ReadingListItems.Where(i => i.ReadingListId == listId).OrderBy(i => i.SortOrder).Select(i => i.IssueId));
    }

    [Theory]
    [InlineData(0, -1)]   // already first, move up
    [InlineData(2, 1)]    // already last, move down
    public void MoveItem_at_the_edge_does_nothing_and_announces_nothing(int position, int offset)
    {
        var (listId, issueIds) = Seed(3, itemsAlreadyInList: 3);
        int itemId;
        using (var read = NewContext())
        {
            itemId = read.ReadingListItems.Single(i => i.IssueId == issueIds[position]).Id;
        }

        using var context = NewContext();
        Assert.False(ReadingListManager.MoveItem(context, listId, itemId, offset, _events));
        context.SaveChanges();

        Assert.Empty(_heard);
    }

    // ---- RemoveIssueFromAllLists ----

    [Fact]
    public void RemoveIssueFromAllLists_announces_once_per_affected_list()
    {
        var (listA, issuesA) = Seed(1, "Alpha", itemsAlreadyInList: 1);
        int sharedIssue = issuesA[0];
        int listB;
        using (var setup = NewContext())
        {
            var b = new ReadingList { Name = "Beta", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var untouched = new ReadingList { Name = "Untouched", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            setup.ReadingLists.AddRange(b, untouched);
            setup.SaveChanges();
            setup.ReadingListItems.Add(new ReadingListItem { ReadingListId = b.Id, IssueId = sharedIssue, SortOrder = 0 });
            setup.SaveChanges();
            listB = b.Id;
        }

        using var context = NewContext();
        ReadingListManager.RemoveIssueFromAllLists(context, sharedIssue, _events);
        Assert.Empty(_heard);
        context.SaveChanges();

        Assert.Equal(2, _heard.Count);
        Assert.All(_heard, c =>
        {
            Assert.Equal(ReadingListChangeKind.Removed, c.Kind);
            Assert.Equal(new[] { sharedIssue }, c.RemovedIssueIds);
        });
        Assert.Equal(new[] { listA, listB }.OrderBy(x => x), _heard.Select(c => c.ListId).OrderBy(x => x));
        Assert.Contains(_heard, c => c.ListName == "Alpha");
        Assert.Contains(_heard, c => c.ListName == "Beta");

        using var verify = NewContext();
        Assert.Empty(verify.ReadingListItems);
    }

    [Fact]
    public void RemoveIssueFromAllLists_for_an_issue_in_no_list_announces_nothing()
    {
        var (_, issueIds) = Seed(1);
        using var context = NewContext();

        ReadingListManager.RemoveIssueFromAllLists(context, issueIds[0], _events);
        context.SaveChanges();

        Assert.Empty(_heard);
    }

    // ---- RecordCreatedWithItems / Record ----

    [Fact]
    public void RecordCreatedWithItems_reads_the_list_id_and_items_when_the_save_lands()
    {
        var (_, issueIds) = Seed(3);
        using var context = NewContext();
        var list = new ReadingList { Name = "Imported Arc", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        for (int i = 0; i < 3; i++)
        {
            list.Items.Add(new ReadingListItem { IssueId = issueIds[i], SortOrder = i });
        }

        context.ReadingLists.Add(list);
        ReadingListManager.RecordCreatedWithItems(context, list, events: _events);
        Assert.Empty(_heard);
        Assert.Equal(0, list.Id);   // not persisted yet - the Id must be read at flush time

        context.SaveChanges();

        var change = Assert.Single(_heard);
        Assert.NotEqual(0, change.ListId);
        Assert.Equal(list.Id, change.ListId);
        Assert.Equal("Imported Arc", change.ListName);
        Assert.Equal(ReadingListChangeKind.Imported, change.Kind);
        Assert.Equal(issueIds, change.AddedIssueIds);
    }

    [Fact]
    public void RecordCreatedWithItems_announces_nothing_for_an_empty_list()
    {
        using var context = NewContext();
        var list = new ReadingList { Name = "Blank", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.ReadingLists.Add(list);
        ReadingListManager.RecordCreatedWithItems(context, list, events: _events);

        context.SaveChanges();

        Assert.Empty(_heard);
    }

    [Fact]
    public void RecordCreatedWithItems_can_announce_a_kind_other_than_Imported()
    {
        var (_, issueIds) = Seed(1);
        using var context = NewContext();
        var list = new ReadingList { Name = "From an event", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        list.Items.Add(new ReadingListItem { IssueId = issueIds[0], SortOrder = 0 });
        context.ReadingLists.Add(list);
        ReadingListManager.RecordCreatedWithItems(context, list, ReadingListChangeKind.Added, _events);

        context.SaveChanges();

        Assert.Equal(ReadingListChangeKind.Added, Assert.Single(_heard).Kind);
    }

    [Fact]
    public void Record_announces_a_compound_change_with_flags()
    {
        var (listId, issueIds) = Seed(2);
        using var context = NewContext();
        var list = context.ReadingLists.Find(listId)!;

        ReadingListManager.Record(
            context,
            list,
            ReadingListChangeKind.Added | ReadingListChangeKind.Removed | ReadingListChangeKind.Reordered,
            new[] { issueIds[0] },
            new[] { issueIds[1] },
            _events);
        context.SaveChanges();

        var change = Assert.Single(_heard);
        Assert.True(change.Kind.HasFlag(ReadingListChangeKind.Added));
        Assert.True(change.Kind.HasFlag(ReadingListChangeKind.Removed));
        Assert.True(change.Kind.HasFlag(ReadingListChangeKind.Reordered));
        Assert.False(change.Kind.HasFlag(ReadingListChangeKind.Imported));
    }

    [Fact]
    public void Record_with_no_kind_announces_nothing()
    {
        var (listId, _) = Seed(1);
        using var context = NewContext();

        ReadingListManager.Record(context, context.ReadingLists.Find(listId)!, ReadingListChangeKind.None, Array.Empty<int>(), Array.Empty<int>(), _events);
        context.SaveChanges();

        Assert.Empty(_heard);
    }

    // ---- The hub itself ----

    [Fact]
    public void A_throwing_subscriber_reaches_neither_the_producer_nor_the_other_subscribers()
    {
        var (listId, issueIds) = Seed(1);
        var hub = new LibraryEvents();
        var heard = new List<ReadingListChangedEvent>();
        hub.ReadingListChanged += _ => throw new InvalidOperationException("bad subscriber");
        hub.ReadingListChanged += heard.Add;

        using var context = NewContext();
        ReadingListManager.AddIssues(context, listId, issueIds, hub);
        context.SaveChanges();   // must not throw

        Assert.Single(heard);
    }
}
