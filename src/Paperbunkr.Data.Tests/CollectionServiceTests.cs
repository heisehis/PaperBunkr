using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="CollectionService"/> (docs/superpowers/specs/2026-08-27-collections-
/// design.md) - the CRUD/membership surface had no dedicated test file before the smart-collections
/// work (docs/superpowers/specs/2026-08-30-smart-collections-design.md), which also adds the
/// rule-slot set/clear methods covered here.
/// </summary>
public class CollectionServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public CollectionServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_service_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private int AddSeries(string name = "Series")
    {
        var series = new Series { Name = name };
        _context.Series.Add(series);
        _context.SaveChanges();
        return series.Id;
    }

    private int AddSmartList(SmartListTargetKind kind)
    {
        var list = new SmartList { Name = "list", TargetKind = kind, RootGroup = new SmartListConditionGroup() };
        _context.SmartLists.Add(list);
        _context.SaveChanges();
        return list.Id;
    }

    // --- Existing CRUD/membership surface ---

    [Fact]
    public void Create_AppendsAtEndOfSortOrder()
    {
        var a = CollectionService.Create(_context, "A");
        var b = CollectionService.Create(_context, "B");
        Assert.Equal(0, a.SortOrder);
        Assert.Equal(1, b.SortOrder);
    }

    [Fact]
    public void Rename_UpdatesName()
    {
        var collection = CollectionService.Create(_context, "Old");
        CollectionService.Rename(_context, collection.Id, "New");
        Assert.Equal("New", _context.Collections.Find(collection.Id)!.Name);
    }

    [Fact]
    public void Delete_CascadesCollectionItems()
    {
        var collection = CollectionService.Create(_context, "C");
        int seriesId = AddSeries();
        CollectionService.AddItems(_context, collection.Id, seriesIds: [seriesId]);

        CollectionService.Delete(_context, collection.Id);

        Assert.Null(_context.Collections.Find(collection.Id));
        Assert.Empty(_context.CollectionItems.Where(ci => ci.CollectionId == collection.Id));
    }

    [Fact]
    public void Reorder_AssignsSortOrderByPosition()
    {
        var a = CollectionService.Create(_context, "A");
        var b = CollectionService.Create(_context, "B");
        var c = CollectionService.Create(_context, "C");

        CollectionService.Reorder(_context, [c.Id, a.Id]);

        Assert.Equal(0, _context.Collections.Find(c.Id)!.SortOrder);
        Assert.Equal(1, _context.Collections.Find(a.Id)!.SortOrder);
        Assert.Equal(2, _context.Collections.Find(b.Id)!.SortOrder); // unlisted, appended after
    }

    [Fact]
    public void AddItems_IsIdempotent_SkipsAlreadyPresentTargets()
    {
        var collection = CollectionService.Create(_context, "C");
        int seriesId = AddSeries();

        CollectionService.AddItems(_context, collection.Id, seriesIds: [seriesId]);
        CollectionService.AddItems(_context, collection.Id, seriesIds: [seriesId]);

        Assert.Single(_context.CollectionItems.Where(ci => ci.CollectionId == collection.Id));
    }

    [Fact]
    public void AddItems_SkipsStaleIdThatDoesNotExist()
    {
        var collection = CollectionService.Create(_context, "C");
        CollectionService.AddItems(_context, collection.Id, seriesIds: [999999]);
        Assert.Empty(_context.CollectionItems.Where(ci => ci.CollectionId == collection.Id));
    }

    [Fact]
    public void RemoveTargets_RemovesMatchingItems()
    {
        var collection = CollectionService.Create(_context, "C");
        int seriesId = AddSeries();
        CollectionService.AddItems(_context, collection.Id, seriesIds: [seriesId]);

        CollectionService.RemoveTargets(_context, collection.Id, seriesIds: [seriesId]);

        Assert.Empty(_context.CollectionItems.Where(ci => ci.CollectionId == collection.Id));
    }

    [Fact]
    public void ReorderItems_AssignsSortOrderByPosition()
    {
        var collection = CollectionService.Create(_context, "C");
        int seriesA = AddSeries("A");
        int seriesB = AddSeries("B");
        CollectionService.AddItems(_context, collection.Id, seriesIds: [seriesA, seriesB]);

        var items = _context.CollectionItems.Where(ci => ci.CollectionId == collection.Id).OrderBy(ci => ci.SortOrder).ToList();
        CollectionService.ReorderItems(_context, collection.Id, [items[1].Id, items[0].Id]);

        Assert.Equal(0, _context.CollectionItems.Find(items[1].Id)!.SortOrder);
        Assert.Equal(1, _context.CollectionItems.Find(items[0].Id)!.SortOrder);
    }

    // --- Rule slots (docs/superpowers/specs/2026-08-30-smart-collections-design.md) ---

    [Fact]
    public void SetIssueSmartList_SetsTheSlot()
    {
        var collection = CollectionService.Create(_context, "C");
        int listId = AddSmartList(SmartListTargetKind.Issue);

        CollectionService.SetIssueSmartList(_context, collection.Id, listId);

        Assert.Equal(listId, _context.Collections.Find(collection.Id)!.IssueSmartListId);
    }

    [Fact]
    public void SetSeriesSmartList_SetsTheSlot()
    {
        var collection = CollectionService.Create(_context, "C");
        int listId = AddSmartList(SmartListTargetKind.Series);

        CollectionService.SetSeriesSmartList(_context, collection.Id, listId);

        Assert.Equal(listId, _context.Collections.Find(collection.Id)!.SeriesSmartListId);
    }

    [Fact]
    public void SetNovelSmartList_SetsTheSlot()
    {
        var collection = CollectionService.Create(_context, "C");
        int listId = AddSmartList(SmartListTargetKind.Novel);

        CollectionService.SetNovelSmartList(_context, collection.Id, listId);

        Assert.Equal(listId, _context.Collections.Find(collection.Id)!.NovelSmartListId);
    }

    [Fact]
    public void SetIssueSmartList_KindMismatch_IsALoggedNoOp()
    {
        var collection = CollectionService.Create(_context, "C");
        int seriesListId = AddSmartList(SmartListTargetKind.Series);

        CollectionService.SetIssueSmartList(_context, collection.Id, seriesListId);

        Assert.Null(_context.Collections.Find(collection.Id)!.IssueSmartListId);
    }

    [Fact]
    public void ClearSeriesSmartList_NullsTheSlot()
    {
        var collection = CollectionService.Create(_context, "C");
        int listId = AddSmartList(SmartListTargetKind.Series);
        CollectionService.SetSeriesSmartList(_context, collection.Id, listId);

        CollectionService.ClearSeriesSmartList(_context, collection.Id);

        Assert.Null(_context.Collections.Find(collection.Id)!.SeriesSmartListId);
    }

    [Fact]
    public void DeletingUnderlyingSmartList_SetsNullOnTheCollection()
    {
        var collection = CollectionService.Create(_context, "C");
        int listId = AddSmartList(SmartListTargetKind.Issue);
        CollectionService.SetIssueSmartList(_context, collection.Id, listId);

        _context.SmartLists.Remove(_context.SmartLists.Find(listId)!);
        _context.SaveChanges();

        Assert.Null(_context.Collections.Find(collection.Id)!.IssueSmartListId);
    }

    [Fact]
    public void IsSmart_TrueOnlyWhenARuleSlotIsSet()
    {
        var collection = CollectionService.Create(_context, "C");
        Assert.False(_context.Collections.Find(collection.Id)!.IsSmart);

        int listId = AddSmartList(SmartListTargetKind.Issue);
        CollectionService.SetIssueSmartList(_context, collection.Id, listId);

        Assert.True(_context.Collections.Find(collection.Id)!.IsSmart);
    }
}
