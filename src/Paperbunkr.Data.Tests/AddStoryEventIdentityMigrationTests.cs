using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddStoryEventIdentity</c> migration (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §1): existing
/// events with an arc id are backfilled as <see cref="StoryEventOrigin.Provider"/>, the rest as <see cref="StoryEventOrigin.User"/>, and
/// the new alias / duplicate-dismissal tables accept rows. Forward-only: migrate to the previous migration, seed, migrate to head.
/// </summary>
public class AddStoryEventIdentityMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_event_identity_migration_test_{Guid.NewGuid():N}.db");

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
    public void ExistingEvents_BackfillOriginFromArcIds()
    {
        using (var context = CreateContext())
        {
            var migrations = context.Database.GetMigrations().ToList();
            string target = migrations.Single(m => m.EndsWith("_AddStoryEventIdentity", StringComparison.Ordinal));
            string previous = migrations[migrations.IndexOf(target) - 1];
            context.GetService<IMigrator>().Migrate(previous);

            context.Database.ExecuteSqlRaw(
                "INSERT INTO StoryEvents (Name, CreatedAt, UpdatedAt, ComicVineArcId) VALUES ('From ComicVine', '2026-01-01', '2026-01-01', '4512');" +
                "INSERT INTO StoryEvents (Name, CreatedAt, UpdatedAt, MetronArcId) VALUES ('From Metron', '2026-01-01', '2026-01-01', '77');" +
                "INSERT INTO StoryEvents (Name, CreatedAt, UpdatedAt) VALUES ('Hand made', '2026-01-01', '2026-01-01');");

            context.Database.Migrate();
        }

        using (var context = CreateContext())
        {
            var origins = context.StoryEvents.ToDictionary(e => e.Name, e => e.Origin);
            Assert.Equal(StoryEventOrigin.Provider, origins["From ComicVine"]);
            Assert.Equal(StoryEventOrigin.Provider, origins["From Metron"]);
            Assert.Equal(StoryEventOrigin.User, origins["Hand made"]);
            Assert.All(context.StoryEvents, e => Assert.Null(e.IdentityCheckedAt));
        }
    }

    [Fact]
    public void NewTables_AcceptRows_AndCascadeWithTheEvent()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        var a = new StoryEvent { Name = "Planet Hulk", Origin = StoryEventOrigin.Provider, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var b = new StoryEvent { Name = "Other", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        a.Aliases.Add(new StoryEventAlias { Name = "Hulk: Planet Hulk", Key = "hulkplanethulk", Source = StoryEventAliasSource.Merge });
        context.StoryEvents.AddRange(a, b);
        context.SaveChanges();
        context.StoryEventDuplicateDismissals.Add(new StoryEventDuplicateDismissal { LowerEventId = a.Id, HigherEventId = b.Id });
        context.SaveChanges();

        context.StoryEvents.Remove(a);
        context.SaveChanges();

        Assert.Empty(context.StoryEventAliases);
        Assert.Empty(context.StoryEventDuplicateDismissals);
    }
}
