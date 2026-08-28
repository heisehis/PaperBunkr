using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>ContinuityMembershipJoinEntity</c> migration (docs/superpowers/specs/2026-08-28-
/// continuity-editing-design.md, Part C) copies every row of the outgoing implicit
/// <c>ContinuitySeries</c> join into the new explicit <c>ContinuityMemberships</c> table, with a
/// stable per-continuity <c>SortOrder</c> (0-based, by series name) and null <c>Note</c>.
/// </summary>
public class ContinuityMembershipMigrationTests : IDisposable
{
    private const string PriorMigration = "20260828104324_MetadataModelPhase4DeferredItems";
    private readonly string _dbPath;

    public ContinuityMembershipMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_continuitymembership_migration_{Guid.NewGuid():N}.db");
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
    public void Migration_CopiesEveryImplicitJoinRow_WithStablePerContinuitySortOrder()
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);

            context.Database.ExecuteSqlRaw("INSERT INTO Series (Id, Name, ContentType, ReadingMode, Status) VALUES (1, 'Charlie', 'Unknown', 'LeftToRight', 'Unknown');");
            context.Database.ExecuteSqlRaw("INSERT INTO Series (Id, Name, ContentType, ReadingMode, Status) VALUES (2, 'Alpha', 'Unknown', 'LeftToRight', 'Unknown');");
            context.Database.ExecuteSqlRaw("INSERT INTO Series (Id, Name, ContentType, ReadingMode, Status) VALUES (3, 'Bravo', 'Unknown', 'LeftToRight', 'Unknown');");

            context.Database.ExecuteSqlRaw("INSERT INTO Continuities (Id, Name, CreatedAt, UpdatedAt) VALUES (10, 'Earth-616', '2026-01-01', '2026-01-01');");
            context.Database.ExecuteSqlRaw("INSERT INTO Continuities (Id, Name, CreatedAt, UpdatedAt) VALUES (20, 'Ultimate', '2026-01-01', '2026-01-01');");

            // Continuity 10 gets all three series (inserted out of name order on purpose);
            // continuity 20 gets just one.
            context.Database.ExecuteSqlRaw("INSERT INTO ContinuitySeries (ContinuitiesId, SeriesId) VALUES (10, 1);");
            context.Database.ExecuteSqlRaw("INSERT INTO ContinuitySeries (ContinuitiesId, SeriesId) VALUES (10, 3);");
            context.Database.ExecuteSqlRaw("INSERT INTO ContinuitySeries (ContinuitiesId, SeriesId) VALUES (10, 2);");
            context.Database.ExecuteSqlRaw("INSERT INTO ContinuitySeries (ContinuitiesId, SeriesId) VALUES (20, 1);");

            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            var all = context.ContinuityMemberships.OrderBy(m => m.ContinuityId).ThenBy(m => m.SortOrder).ToList();
            Assert.Equal(4, all.Count);
            Assert.All(all, m => Assert.Null(m.Note));

            var c10 = all.Where(m => m.ContinuityId == 10).ToList();
            // Ordered by series name: Alpha(2)=0, Bravo(3)=1, Charlie(1)=2
            Assert.Equal(new[] { (2, 0), (3, 1), (1, 2) }, c10.Select(m => (m.SeriesId, m.SortOrder)));

            var c20 = Assert.Single(all.Where(m => m.ContinuityId == 20));
            Assert.Equal(1, c20.SeriesId);
            Assert.Equal(0, c20.SortOrder);
        }
    }
}
