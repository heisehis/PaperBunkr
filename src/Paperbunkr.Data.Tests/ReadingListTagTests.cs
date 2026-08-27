using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Basic CRUD/cascade-delete coverage for <see cref="ReadingListTag"/> (docs/superpowers/specs/
/// 2026-08-23-reading-list-tags-design.md) - a brand-new table with no backfill, so unlike
/// <c>AddIssueTagsMigrationTests</c> there's no migration-runner test to write; this instead
/// verifies the EF configuration (cascade delete via <c>PaperbunkrDbContext.OnModelCreating</c>)
/// and the diff-not-replace <see cref="ReadingListTagExtensions.MergeFrom"/> helper.
/// </summary>
public class ReadingListTagTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_readinglisttag_test_{Guid.NewGuid():N}.db");

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

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    [Fact]
    public void AddTag_SaveAndReload_RoundTripsValueCategoryWeight()
    {
        int listId;
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            var list = new ReadingList { Name = "Test List", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            list.Tags.Add(new ReadingListTag { Value = "Dark", Category = "Tone", Weight = IssueTagWeight.Core });
            context.ReadingLists.Add(list);
            context.SaveChanges();
            listId = list.Id;
        }

        using var verify = CreateContext();
        var reloaded = verify.ReadingLists.Include(r => r.Tags).Single(r => r.Id == listId);
        var tag = Assert.Single(reloaded.Tags);
        Assert.Equal("Dark", tag.Value);
        Assert.Equal("Tone", tag.Category);
        Assert.Equal(IssueTagWeight.Core, tag.Weight);
    }

    [Fact]
    public void DeletingReadingList_CascadeDeletesItsTags()
    {
        int listId;
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            var list = new ReadingList { Name = "Test List", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            list.Tags.Add(new ReadingListTag { Value = "Dark" });
            context.ReadingLists.Add(list);
            context.SaveChanges();
            listId = list.Id;
        }

        using (var context = CreateContext())
        {
            context.ReadingLists.Remove(context.ReadingLists.Single(r => r.Id == listId));
            context.SaveChanges();
        }

        using var verify = CreateContext();
        Assert.Empty(verify.ReadingListTags.Where(t => t.ReadingListId == listId));
    }

    [Fact]
    public void MergeFrom_AddsNewValues_RemovesMissingValues_PreservesSurvivingCategoryWeight()
    {
        var list = new ReadingList { Name = "Test List" };
        list.Tags.Add(new ReadingListTag { Value = "Dark", Category = "Tone", Weight = IssueTagWeight.Core });
        list.Tags.Add(new ReadingListTag { Value = "Old", Category = "Uncategorized", Weight = IssueTagWeight.Unset });

        list.MergeFrom(new[] { "Dark, New Value" });

        Assert.Equal(2, list.Tags.Count);
        var dark = list.Tags.Single(t => t.Value == "Dark");
        Assert.Equal("Tone", dark.Category);
        Assert.Equal(IssueTagWeight.Core, dark.Weight);
        var newValue = list.Tags.Single(t => t.Value == "New Value");
        Assert.Equal("Uncategorized", newValue.Category);
        Assert.Equal(IssueTagWeight.Unset, newValue.Weight);
        Assert.DoesNotContain(list.Tags, t => t.Value == "Old");
    }

    [Fact]
    public void JoinedTags_EmptyCollection_ReturnsNull()
    {
        var list = new ReadingList { Name = "Test List" };
        Assert.Null(list.JoinedTags());
    }
}
