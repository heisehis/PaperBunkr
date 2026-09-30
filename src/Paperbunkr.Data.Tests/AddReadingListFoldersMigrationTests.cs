using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The <c>AddReadingListFolders</c> migration (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §1, §7 and
/// ...-build-from-events-design.md §2): the folder and dismissal tables, the new list columns, and the one-time SortOrder renumber that
/// gives every existing list a distinct position.
/// </summary>
public class AddReadingListFoldersMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_folders_migration_test_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    [Fact]
    public void Migration_RenumbersExistingLists_InTheirCurrentOrder()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate("AddContinuitySidebarTab");
            // Imports left SortOrder at 0: two zeros (ids 1, 2), then a 5, then another 0 inserted later (id 4).
            context.Database.ExecuteSqlRaw(
                "INSERT INTO ReadingLists (Id, Name, SortOrder, Type, FollowArc, CreatedAt, UpdatedAt) VALUES " +
                "(1, 'a', 0, 'User', 0, '2026-01-01', '2026-01-01'), (2, 'b', 0, 'User', 0, '2026-01-01', '2026-01-01'), " +
                "(3, 'c', 5, 'User', 0, '2026-01-01', '2026-01-01'), (4, 'd', 0, 'User', 0, '2026-01-01', '2026-01-01');");
            context.GetService<IMigrator>().Migrate();
        }

        using (var context = CreateContext())
        {
            var order = context.ReadingLists.OrderBy(r => r.SortOrder).Select(r => new { r.Id, r.SortOrder }).ToList();
            Assert.Equal(new[] { 1, 2, 4, 3 }, order.Select(o => o.Id));
            Assert.Equal(new[] { 0, 1, 2, 3 }, order.Select(o => o.SortOrder));
            Assert.All(context.ReadingLists.ToList(), r => Assert.Null(r.FolderId));
        }
    }

    [Fact]
    public void NewColumnsAndTables_RoundTrip_AndDismissalsCascade()
    {
        int listA, listB;
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var continuity = new Continuity { Name = "Earth-616" };
            context.Continuities.Add(continuity);
            var parent = new ReadingListFolder { Name = "Crisis Events" };
            context.ReadingListFolders.Add(parent);
            context.SaveChanges();
            var child = new ReadingListFolder { Name = "Tie-ins", ParentFolderId = parent.Id };
            context.ReadingListFolders.Add(child);
            var a = new ReadingList { Name = "A", Folder = child, ContinuityId = continuity.Id, ContinuityOrderKind = ContinuityOrderKind.StoryOrder };
            var b = new ReadingList { Name = "B" };
            context.ReadingLists.AddRange(a, b);
            context.SaveChanges();
            listA = a.Id;
            listB = b.Id;
            context.ReadingListOverlapDismissals.Add(new ReadingListOverlapDismissal { ListAId = listA, ListBId = listB, CreatedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var a = context.ReadingLists.Include(r => r.Folder).Single(r => r.Id == listA);
            Assert.Equal("Tie-ins", a.Folder!.Name);
            Assert.NotNull(a.Folder.ParentFolderId);
            Assert.Equal(ContinuityOrderKind.StoryOrder, a.ContinuityOrderKind);
            Assert.NotNull(a.ContinuityId);

            context.ReadingLists.Remove(context.ReadingLists.Single(r => r.Id == listB));
            context.SaveChanges();
            Assert.Empty(context.ReadingListOverlapDismissals);
        }
    }
}
