using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The dismissal table behind "Not a gap" / "This is intended" (docs/superpowers/specs/2026-10-06-smart-features-design.md §2): the
/// migration applies on top of the prior one, rolls back cleanly, and re-applies; and <see cref="HealthDismissals"/> is idempotent.
/// </summary>
public class AddHealthFindingDismissalsMigrationTests : IDisposable
{
    private const string PriorMigration = "20261006044237_AddContentTypeProvenance";
    private const string ThisMigration = "20261006090932_AddHealthFindingDismissals";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_health_dismissal_migration_test_{Guid.NewGuid():N}.db");

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

    private bool TableExists()
    {
        using var context = CreateContext();
        return context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'HealthFindingDismissals'").Single() == 1;
    }

    [Fact]
    public void Migration_AppliesRollsBackAndReapplies()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
        }

        Assert.False(TableExists());

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(ThisMigration);
        }

        Assert.True(TableExists());

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
        }

        Assert.False(TableExists());

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(ThisMigration);
        }

        Assert.True(TableExists());
    }

    [Fact]
    public void Dismiss_IsIdempotent_Restore_BringsTheFindingBack_AndKindsDoNotMix()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        HealthDismissals.Dismiss(context, HealthDismissals.CollectionGap, "12:4,6", "Saga");
        HealthDismissals.Dismiss(context, HealthDismissals.CollectionGap, "12:4,6", "Saga");
        HealthDismissals.Dismiss(context, HealthDismissals.Consistency, "12:4,6");

        Assert.Equal(["12:4,6"], HealthDismissals.KeysFor(context, HealthDismissals.CollectionGap));
        Assert.Equal("Saga", Assert.Single(HealthDismissals.RowsFor(context, HealthDismissals.CollectionGap)).Label);
        Assert.True(HealthDismissals.IsDismissed(context, HealthDismissals.Consistency, "12:4,6"));
        Assert.False(HealthDismissals.IsDismissed(context, HealthDismissals.CollectionGap, "12:4"));

        Assert.True(HealthDismissals.Restore(context, HealthDismissals.CollectionGap, "12:4,6"));
        Assert.False(HealthDismissals.Restore(context, HealthDismissals.CollectionGap, "12:4,6"));
        Assert.Empty(HealthDismissals.KeysFor(context, HealthDismissals.CollectionGap));
        Assert.True(HealthDismissals.IsDismissed(context, HealthDismissals.Consistency, "12:4,6"));
    }
}
