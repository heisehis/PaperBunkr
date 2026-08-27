using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the hand-edited <c>AddIssueTags</c> migration's raw-SQL backfill against a real
/// SQLite database carrying pre-migration Genre/Tags CSV data (docs/superpowers/specs/2026-08-23-
/// weighted-categorized-tags-design.md) - same shape as
/// <see cref="MetadataModelPhase1MigrationTests"/>, catching a regression back to a naive
/// scaffolded migration that would drop Genre/Tags before the backfill could read them.
/// </summary>
public class AddIssueTagsMigrationTests : IDisposable
{
    private const string PriorMigration = "20260823101734_SeriesMetadataProposals";
    private readonly string _dbPath;

    public AddIssueTagsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_issuetags_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_SplitsGenreAndTagsCsv_IntoIssueTagRows_WithUnsetWeight()
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSql(
                $"INSERT INTO Series (Name, ContentType, ReadingMode) VALUES ('Old Shape Series', 'Unknown', 'LeftToRight');");
            const string insertPrefix = "INSERT INTO Issues (SeriesId, Number, Genre, Tags, FileIsMissing, Checked, IsPlaceholder, MissingAcknowledged, IsFinalIssue, OpenCount, ColorMode) SELECT Id, ";
            context.Database.ExecuteSqlRaw(
                insertPrefix + "'1', 'Action, Adventure, Sci-Fi', 'slow burn, found family', 0, 0, 0, 0, 0, 0, 'Unknown' FROM Series WHERE Name = 'Old Shape Series';");
            context.Database.ExecuteSqlRaw(
                insertPrefix + "'2', NULL, NULL, 0, 0, 0, 0, 0, 0, 'Unknown' FROM Series WHERE Name = 'Old Shape Series';");
            context.Database.ExecuteSqlRaw(
                insertPrefix + "'3', '', '', 0, 0, 0, 0, 0, 0, 'Unknown' FROM Series WHERE Name = 'Old Shape Series';");

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var series = context.Series.Include(s => s.Issues).ThenInclude(i => i.Tags).First(s => s.Name == "Old Shape Series");

            var issue1 = series.Issues.First(i => i.Number == "1");
            var genreTags = issue1.Tags.Where(t => t.Field == IssueTagField.Genre).OrderBy(t => t.Id).ToList();
            Assert.Equal(new[] { "Action", "Adventure", "Sci-Fi" }, genreTags.Select(t => t.Value));
            Assert.All(genreTags, t => Assert.Equal("Genre", t.Category));
            Assert.All(genreTags, t => Assert.Equal(IssueTagWeight.Unset, t.Weight));

            var tagsTags = issue1.Tags.Where(t => t.Field == IssueTagField.Tags).OrderBy(t => t.Id).ToList();
            Assert.Equal(new[] { "slow burn", "found family" }, tagsTags.Select(t => t.Value));
            Assert.All(tagsTags, t => Assert.Equal("Uncategorized", t.Category));

            var issue2 = series.Issues.First(i => i.Number == "2");
            Assert.Empty(issue2.Tags);

            var issue3 = series.Issues.First(i => i.Number == "3");
            Assert.Empty(issue3.Tags);
        }

        // Old flat columns are gone - a compile-time guarantee everywhere else, but this confirms
        // the migration actually dropped them at the schema level too.
        using (var context = CreateContext())
        {
            var columns = context.Database.SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Issues') WHERE name IN ('Genre', 'Tags');").ToList();
            Assert.Empty(columns);
        }
    }
}
