using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests.ReadingLists;

/// <summary><see cref="ReadingListManager.MoveItemsTo"/> and <see cref="ReadingListManager.SetGroupLabel"/> - Edit mode's writes
/// (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §8).</summary>
public class ReadingListManagerEditTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_edit_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly LibraryEvents _events = new();
    private readonly List<ReadingListChangedEvent> _heard = new();

    public ReadingListManagerEditTests()
    {
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

    private (int ListId, int[] ItemIds) Seed(int count)
    {
        using var context = new PaperbunkrDbContext(_options);
        var series = new Series { Name = "S" };
        var list = new ReadingList { Name = "L" };
        for (int i = 0; i < count; i++)
        {
            list.Items.Add(new ReadingListItem { Issue = new Issue { Series = series, Number = (i + 1).ToString() }, SortOrder = i });
        }

        context.ReadingLists.Add(list);
        context.SaveChanges();
        return (list.Id, list.Items.OrderBy(i => i.SortOrder).Select(i => i.Id).ToArray());
    }

    private int[] Order(int listId)
    {
        using var context = new PaperbunkrDbContext(_options);
        return context.ReadingListItems.Where(i => i.ReadingListId == listId).OrderBy(i => i.SortOrder).Select(i => i.Id).ToArray();
    }

    [Fact]
    public void MoveItemsTo_KeepsRelativeOrder_AndAnnouncesOnce()
    {
        var (listId, ids) = Seed(5);
        using (var context = new PaperbunkrDbContext(_options))
        {
            Assert.True(ReadingListManager.MoveItemsTo(context, listId, new[] { ids[4], ids[2] }, 0, _events));
            context.SaveChanges();
        }

        Assert.Equal(new[] { ids[2], ids[4], ids[0], ids[1], ids[3] }, Order(listId));
        Assert.Equal(ReadingListChangeKind.Reordered, Assert.Single(_heard).Kind);
    }

    [Fact]
    public void MoveItemsTo_NoOpAndClamp()
    {
        var (listId, ids) = Seed(3);
        using var context = new PaperbunkrDbContext(_options);
        Assert.False(ReadingListManager.MoveItemsTo(context, listId, new[] { ids[0] }, 0, _events));   // already there
        Assert.True(ReadingListManager.MoveItemsTo(context, listId, new[] { ids[0] }, 99, _events));   // clamped to the end
        context.SaveChanges();
        Assert.Equal(new[] { ids[1], ids[2], ids[0] }, Order(listId));
    }

    [Fact]
    public void SetGroupLabel_ChangesLabels_AnnouncesNothing_AndBlankClears()
    {
        var (listId, ids) = Seed(3);
        using (var context = new PaperbunkrDbContext(_options))
        {
            Assert.Equal(2, ReadingListManager.SetGroupLabel(context, listId, new[] { ids[0], ids[1] }, "  Prelude "));
            context.SaveChanges();
        }

        using (var context = new PaperbunkrDbContext(_options))
        {
            Assert.Equal(new[] { "Prelude", "Prelude", null }, context.ReadingListItems.OrderBy(i => i.SortOrder).Select(i => i.GroupLabel));
            Assert.Equal(1, ReadingListManager.SetGroupLabel(context, listId, new[] { ids[0] }, " "));
            context.SaveChanges();
        }

        Assert.Empty(_heard);
    }
}
