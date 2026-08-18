using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>MetadataModelPhase4bStoryEvents</c> migration against a real SQLite database
/// carrying pre-migration data (docs/superpowers/specs/2026-08-17-metadata-model-phase4b-story-
/// events-design.md) - purely additive schema (two new tables, no existing-column changes),
/// following 2a/2b/3/4a's precedent.
/// </summary>
public class MetadataModelPhase4bMigrationTests : IDisposable
{
    private const string PriorMigration = "20260817161352_MetadataModelPhase4aContinuity";
    private readonly string _dbPath;

    public MetadataModelPhase4bMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_phase4b_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_PreservesExistingRows_AddsEmptyStoryEventTables_AndRestrictOnIssueWorks()
    {
        int seriesId;
        int issueId;

        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSql($"INSERT INTO Series (Name, ContentType, ReadingMode, Status) VALUES ('Green Lantern', 'Unknown', 'LeftToRight', 'Unknown');");
            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var series = context.Series.Single(s => s.Name == "Green Lantern");
            seriesId = series.Id;

            var issue = new Issue { SeriesId = seriesId, Number = "13" };
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;

            // Pre-existing rows untouched, new tables exist and start empty.
            Assert.Empty(context.StoryEvents);
            Assert.Empty(context.EventMemberships);

            var storyEvent = new StoryEvent { Name = "Rise of the Third Army", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.StoryEvents.Add(storyEvent);
            context.SaveChanges();

            context.EventMemberships.Add(new EventMembership { StoryEventId = storyEvent.Id, IssueId = issueId, Position = 0, Role = EventMembershipRole.Core });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var member = Assert.Single(context.EventMemberships);
            Assert.Equal(EventMembershipRole.Core, member.Role);

            // Restrict: deleting an Issue that's a tracked event member is blocked - enforced
            // client-side (Remove() itself throws) once both sides are tracked in the same
            // context, same as EventMembershipResolverTests.
            Assert.Throws<InvalidOperationException>(() => context.Issues.Remove(context.Issues.Single()));
        }

        using (var context = CreateContext())
        {
            // Cascade: deleting the StoryEvent clears its membership without touching the Issue.
            context.StoryEvents.Remove(context.StoryEvents.Single());
            context.SaveChanges();

            Assert.Empty(context.EventMemberships);
            Assert.NotNull(context.Issues.Find(issueId));
        }
    }
}
