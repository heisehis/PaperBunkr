using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests.EventEnrichment;

public sealed class EventSmartFillMigrationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"event_smart_migration_{Guid.NewGuid():N}.db");
    [Fact]
    public void MigrationUpDownIsScopedAndPreservesLegacyEvent()
    {
        using var db = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_path}").Options);
        const string migration = "20261104091810_AddEventSmartFill";
        var list = db.Database.GetMigrations().ToList(); string previous = list[list.IndexOf(migration) - 1];
        var migrator = db.GetService<IMigrator>(); migrator.Migrate(previous);
        db.Database.ExecuteSqlRaw("INSERT INTO StoryEvents(Name,Description,CreatedAt,UpdatedAt,Origin) VALUES ('Legacy','Keep this','2026-01-01','2026-01-01','User')");
        migrator.Migrate(migration);
        Assert.Equal("Keep this", db.StoryEvents.Single().Description);
        Assert.Empty(db.EventMetadataStates); Assert.Empty(db.EventMetadataChanges);
        migrator.Migrate(previous);
        using var command = db.Database.GetDbConnection().CreateCommand(); db.Database.OpenConnection();
        command.CommandText = "SELECT Description FROM StoryEvents"; Assert.Equal("Keep this", command.ExecuteScalar());
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(_path)) File.Delete(_path);
    }
}
