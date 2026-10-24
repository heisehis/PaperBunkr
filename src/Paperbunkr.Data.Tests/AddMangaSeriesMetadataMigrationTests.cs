using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the manga series-metadata migration (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §4): existing series stay
/// sync-enabled, existing issues owe nothing, and the four new tables accept rows with their cascades.
/// </summary>
public class AddMangaSeriesMetadataMigrationTests : IDisposable
{
    private const string PriorMigration = "20261013193949_AddReadingListItemSource";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_manga_meta_migration_test_{Guid.NewGuid():N}.db");

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
    public void Migration_LeavesExistingSeriesSyncEnabled_AndExistingIssuesClean()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw(
                "INSERT INTO Series (Name, ContentType, ReadingMode, Status, ReadingStatus, EmptyRowAcknowledged, TrackerPromptShown) VALUES " +
                "('Old', 'Manga', 'RightToLeft', 'Unknown', 'Unknown', 0, 0);");
        }

        using (var context = CreateContext())
        {
            context.Database.Migrate();

            var series = context.Series.Single(s => s.Name == "Old");
            Assert.True(series.EmbeddedSyncEnabled);
            Assert.Null(series.LockedFields);
            Assert.Equal(0, series.CanonicalReviewVersion);
        }
    }

    [Fact]
    public void NewTables_AcceptRows_AndCascadeWithTheirOwner()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var series = new Entities.Series { Name = "S", ContentType = Entities.ContentType.Manga };
        var issue = new Entities.Issue { FilePath = "a.cbz" };
        series.Issues.Add(issue);
        series.Credits.Add(new Entities.SeriesCredit { Role = "Writer", Name = "A", SortOrder = 0 });
        series.SeriesTags.Add(new Entities.SeriesTag { Field = Entities.IssueTagField.Genre, Value = "Action", Category = "Genre" });
        issue.FieldOverrides.Add(new Entities.IssueFieldOverride { FieldKey = "Publisher" });
        context.Series.Add(series);
        context.SaveChanges();
        context.IssueMetadataSnapshots.Add(new Entities.IssueMetadataSnapshot { IssueId = issue.Id, FieldsJson = "{}" });
        context.SaveChanges();

        // One override per (issue, field).
        context.IssueFieldOverrides.Add(new Entities.IssueFieldOverride { IssueId = issue.Id, FieldKey = "Publisher" });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();

        context.Series.Where(s => s.Id == series.Id).ExecuteDelete();
        Assert.Empty(context.SeriesCredits);
        Assert.Empty(context.SeriesTagRows);
        Assert.Empty(context.IssueFieldOverrides);
        Assert.Empty(context.IssueMetadataSnapshots);
    }
}
