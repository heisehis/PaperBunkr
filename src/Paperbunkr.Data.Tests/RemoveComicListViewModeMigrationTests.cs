using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the hand-edited <c>RemoveComicListViewMode</c> migration's raw-SQL remap: the removed
/// <c>LibraryViewMode.IssueList</c> ("Comic List") display mode, stored by string name, is
/// rewritten to <c>Details</c> on upgrade so an old <c>AppSettings</c> row still parses. Same
/// shape as <see cref="LibraryPosterGridMigrationTests"/>.
/// </summary>
public class RemoveComicListViewModeMigrationTests : IDisposable
{
    // Seed at the same early point LibraryPosterGridMigrationTests uses - late enough that
    // AppSettings exists, early enough that (Id, LibraryViewMode) is a legal partial INSERT
    // (every other NOT NULL column still carries a DB-level default here). migrator.Migrate()
    // then runs everything through RemoveComicListViewMode.
    private const string PriorMigration = "20260827035758_AddRenderingBackendSettings";
    private readonly string _dbPath;

    public RemoveComicListViewModeMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_removecomiclist_migration_test_{Guid.NewGuid():N}.db");
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

    private string MigrateWithSeededViewMode(string legacyValue)
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSqlRaw(
                $"INSERT INTO AppSettings (Id, LibraryViewMode) VALUES (1, '{legacyValue}');");

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            return context.Database
                .SqlQueryRaw<string>("SELECT LibraryViewMode AS Value FROM AppSettings WHERE Id = 1")
                .Single();
        }
    }

    [Fact]
    public void Migration_IssueList_BecomesDetails()
    {
        Assert.Equal("Details", MigrateWithSeededViewMode("IssueList"));
    }

    [Theory]
    [InlineData("PosterGrid")]
    [InlineData("PanoramaGrid")]
    [InlineData("List")]
    [InlineData("Details")]
    [InlineData("Tiles")]
    public void Migration_SurvivingModes_Unchanged(string mode)
    {
        Assert.Equal(mode, MigrateWithSeededViewMode(mode));
    }
}
