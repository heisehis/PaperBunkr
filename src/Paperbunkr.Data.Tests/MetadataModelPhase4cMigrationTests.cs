using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>MetadataModelPhase4cReadingListOverhaul</c> migration against a real SQLite
/// database carrying pre-migration data (docs/superpowers/specs/2026-08-17-metadata-model-phase4c-
/// reading-list-overhaul-design.md) - unlike 4a/4b, this ALTERs an existing table
/// (<c>ReadingLists</c>) with real pre-existing rows, so the backfill itself is the thing under
/// test, not just "new empty tables."
/// </summary>
public class MetadataModelPhase4cMigrationTests : IDisposable
{
    private const string PriorMigration = "20260817162313_MetadataModelPhase4bStoryEvents";
    private readonly string _dbPath;

    public MetadataModelPhase4cMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_phase4c_migration_test_{Guid.NewGuid():N}.db");
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

    private PaperbunkrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_BackfillsExistingReadingList_TypeUserCreatedAtUpdatedAtSet_NewColumnsNull()
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            // Pre-4c schema: insert a reading list the way it existed before Type/CreatedAt/
            // UpdatedAt/StoryEventId existed.
            context.Database.ExecuteSql($"INSERT INTO ReadingLists (Name, SortOrder) VALUES ('My List', 0);");

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var list = context.ReadingLists.Single(r => r.Name == "My List");

            Assert.Equal(ReadingListType.User, list.Type);
            Assert.True(list.CreatedAt > DateTime.MinValue);
            Assert.True(list.UpdatedAt > DateTime.MinValue);
            Assert.Null(list.StoryEventId);
        }
    }

    [Fact]
    public void Migration_ExistingReadingListItem_RoleAndNotesNull()
    {
        int seriesId;
        int issueId;
        int listId;

        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSql($"INSERT INTO Series (Name, ContentType, ReadingMode, Status) VALUES ('Series A', 'Unknown', 'LeftToRight', 'Unknown');");
            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var series = context.Series.Single();
            seriesId = series.Id;
            var issue = new Issue { SeriesId = seriesId, Number = "1" };
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;

            var list = new ReadingList { Name = "My List", Type = ReadingListType.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.ReadingLists.Add(list);
            context.SaveChanges();
            listId = list.Id;

            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = listId, IssueId = issueId, SortOrder = 0 });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var item = context.ReadingListItems.Single();
            Assert.Null(item.Role);
            Assert.Null(item.Notes);
        }
    }

    [Fact]
    public void DeletingLinkedStoryEvent_SetsReadingListStoryEventIdNull_LeavesListIntact()
    {
        int listId;
        int storyEventId;

        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate();

            var storyEvent = new StoryEvent { Name = "Crisis Event", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.StoryEvents.Add(storyEvent);
            context.SaveChanges();
            storyEventId = storyEvent.Id;

            var list = new ReadingList { Name = "Crisis Reading Order", Type = ReadingListType.Event, StoryEventId = storyEventId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.ReadingLists.Add(list);
            context.SaveChanges();
            listId = list.Id;
        }

        using (var context = CreateContext())
        {
            context.StoryEvents.Remove(context.StoryEvents.Single());
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var list = context.ReadingLists.Single(r => r.Id == listId);
            Assert.Null(list.StoryEventId);
            Assert.Equal(ReadingListType.Event, list.Type); // Type is untouched by unlinking
        }
    }
}
