using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddReaderProfiles</c> migration (docs/superpowers/specs/2026-09-25-comic-reader-profiles-design.md): the nullable <c>Series.ReaderProfileId</c> and
/// <c>AppSettings.DefaultReaderProfileId</c> pointers. <c>Down</c> is a deliberate no-op, so only the migrate-to-HEAD state and the round trip are asserted.
/// </summary>
public class AddReaderProfilesMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_profiles_migration_test_{Guid.NewGuid():N}.db");

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
    public void Migration_AddsNullablePointers_ThatRoundTrip()
    {
        int seriesId;
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            Assert.Null(settings.DefaultReaderProfileId);

            var series = new Series { Name = "S" };
            context.Series.Add(series);
            Assert.Null(series.ReaderProfileId);
            settings.DefaultReaderProfileId = 5;
            series.ReaderProfileId = 6;
            context.SaveChanges();
            seriesId = series.Id;
        }

        using (var context = CreateContext())
        {
            Assert.Equal(5, context.GetOrCreateAppSettings().DefaultReaderProfileId);
            Assert.Equal(6, context.Series.Find(seriesId)!.ReaderProfileId);
        }
    }

    [Fact]
    public void WorkspaceScreen_ReaderValue_RoundTripsThroughTheExistingColumn()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            context.Workspaces.Add(new Workspace { Screen = WorkspaceScreen.Reader, Name = "P", StateJson = "{}" });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(WorkspaceScreen.Reader, context.Workspaces.Single().Screen);
        }
    }
}
