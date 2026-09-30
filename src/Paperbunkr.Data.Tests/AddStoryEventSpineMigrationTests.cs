using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddStoryEventSpine</c> migration (docs/superpowers/specs/2026-09-25-event-map-design.md §2):
/// <c>StoryEvent.SpineSeriesId</c> is nullable, existing events read back null ("auto"), and the relay sentinel 0
/// round-trips distinctly from null. Forward-only (no up-down-up; see the migration up-down-up antipattern note).
/// </summary>
public class AddStoryEventSpineMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_spine_migration_test_{Guid.NewGuid():N}.db");

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
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_ExistingEvent_ReadsBackNull()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.StoryEvents.Add(new StoryEvent { Name = "Crisis", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Null(context.StoryEvents.Single().SpineSeriesId);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public void SpineSeriesId_RoundTrips_IncludingTheRelaySentinel(int value)
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.StoryEvents.Add(new StoryEvent { Name = "Crisis", SpineSeriesId = value, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(value, context.StoryEvents.Single().SpineSeriesId);
        }
    }
}
