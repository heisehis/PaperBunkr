using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the content-type provenance migration (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md): every series that
/// already has a type is locked as <c>Existing</c> (provenance was never recorded, so none of them may be overridden), Unknown ones stay open.
/// </summary>
public class AddContentTypeProvenanceMigrationTests : IDisposable
{
    private const string PriorMigration = "20261005200000_AddMetronAccountSync";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_ctprov_migration_test_{Guid.NewGuid():N}.db");

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
    public void Migration_LocksExistingTypedSeries_AndLeavesUnknownOnesOpen()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw(
                "INSERT INTO Series (Name, ContentType, ReadingMode, Status, ReadingStatus, EmptyRowAcknowledged, TrackerPromptShown) VALUES " +
                "('Typed', 'Manga', 'RightToLeft', 'Unknown', 'Unknown', 0, 0), ('Open', 'Unknown', 'LeftToRight', 'Unknown', 'Unknown', 0, 0);");
        }

        using (var context = CreateContext())
        {
            context.Database.Migrate();

            var typed = context.Series.Single(s => s.Name == "Typed");
            Assert.True(typed.ContentTypeLocked);
            Assert.Equal(Entities.ContentTypeSource.Existing, typed.ContentTypeSource);

            var open = context.Series.Single(s => s.Name == "Open");
            Assert.False(open.ContentTypeLocked);
            Assert.Equal(Entities.ContentTypeSource.Unset, open.ContentTypeSource);
            Assert.Equal(Entities.ContentTypeCheck.None, open.ContentTypeCheck);

            Assert.False(context.GetOrCreateAppSettings().AskBeforeClassifying);
        }
    }
}
