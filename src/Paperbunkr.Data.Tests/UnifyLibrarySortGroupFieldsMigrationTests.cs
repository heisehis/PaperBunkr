using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the hand-edited <c>UnifyLibrarySortGroupFields</c> migration: a user's series
/// sort/group selection is best-effort carried over to the now-shared
/// <c>LibraryIssueListSort*/GroupField</c> columns when those are still untouched. The retired
/// <c>LibrarySortField</c> / <c>LibrarySortDirection</c> / <c>LibraryGroupField</c> columns are
/// left as unmapped orphans (not dropped) for older-build compatibility. Same shape as
/// <see cref="LibraryPosterGridMigrationTests"/>.
/// </summary>
public class UnifyLibrarySortGroupFieldsMigrationTests : IDisposable
{
    // Seed early enough that (Id + the 5 sort/group columns) is a legal partial INSERT (every
    // other NOT NULL column still has a DB-level default here); migrator.Migrate() then runs
    // through UnifyLibrarySortGroupFields. Same anchor the sibling migration tests use.
    private const string PriorMigration = "20260827035758_AddRenderingBackendSettings";
    private readonly string _dbPath;

    public UnifyLibrarySortGroupFieldsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_unifysortgroup_migration_test_{Guid.NewGuid():N}.db");
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

    private (string SortField, string SortDir, string GroupField, long OldColumnCount) Migrate(
        string seriesSort, string seriesDir, string seriesGroup, string issueSort = "Number", string issueGroup = "None")
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSqlRaw(
                "INSERT INTO AppSettings (Id, LibrarySortField, LibrarySortDirection, LibraryGroupField, " +
                "LibraryIssueListSortField, LibraryIssueListGroupField) VALUES " +
                $"(1, '{seriesSort}', '{seriesDir}', '{seriesGroup}', '{issueSort}', '{issueGroup}');");

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            string sort = context.Database.SqlQueryRaw<string>("SELECT LibraryIssueListSortField AS Value FROM AppSettings WHERE Id = 1").Single();
            string dir = context.Database.SqlQueryRaw<string>("SELECT LibraryIssueListSortDirection AS Value FROM AppSettings WHERE Id = 1").Single();
            string grp = context.Database.SqlQueryRaw<string>("SELECT LibraryIssueListGroupField AS Value FROM AppSettings WHERE Id = 1").Single();
            long oldCols = context.Database.SqlQueryRaw<long>(
                "SELECT COUNT(*) AS Value FROM pragma_table_info('AppSettings') WHERE name IN ('LibrarySortField','LibrarySortDirection','LibraryGroupField')").Single();
            return (sort, dir, grp, oldCols);
        }
    }

    [Fact]
    public void LeavesTheOldSeriesColumnsInPlace_ForOlderBuildCompatibility()
    {
        var r = Migrate("UnreadCount", "Ascending", "ContentType");
        Assert.Equal(3, r.OldColumnCount);
    }

    [Theory]
    [InlineData("Name", "Series")]
    [InlineData("DateAdded", "Added")]
    [InlineData("LastRead", "Opened")]
    [InlineData("Size", "FileSize")]
    [InlineData("IssueCount", "SeriesIssueCount")]
    [InlineData("UnreadCount", "SeriesUnreadCount")]
    [InlineData("Publisher", "Publisher")]
    public void CarriesSeriesSortFieldOver_WhenIssueListSortUntouched(string seriesSort, string expected)
    {
        var r = Migrate(seriesSort, "Ascending", "None");
        Assert.Equal(expected, r.SortField);
        Assert.Equal("Ascending", r.SortDir);
    }

    [Theory]
    [InlineData("ContentType")]
    [InlineData("Publisher")]
    [InlineData("Alphabetical")]
    public void CarriesSeriesGroupFieldOver_WhenIssueListGroupUntouched(string seriesGroup)
    {
        var r = Migrate("Name", "Descending", seriesGroup);
        Assert.Equal(seriesGroup, r.GroupField);
    }

    [Fact]
    public void DoesNotClobberARealPerIssueSelection()
    {
        var r = Migrate("UnreadCount", "Ascending", "ContentType", issueSort: "Writer", issueGroup: "Genre");
        Assert.Equal("Writer", r.SortField);
        Assert.Equal("Genre", r.GroupField);
    }
}
