using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddMultipleKeyBindingsPerCommand</c> migration (docs/superpowers/specs/2026-09-07-
/// keyboard-shortcuts-redesign-design.md §1): drops the unique index on <c>KeyBindings.CommandId</c>
/// alone (which previously enforced single-binding-per-command at the schema level, not just in
/// application code) and replaces it with a unique index on <c>(CommandId, Key)</c>, so a command may
/// have several distinct bound gestures but never the exact same gesture twice. <c>Down</c>
/// deliberately drops only the new composite index rather than recreating the old single-column one -
/// same reasoning as the AppSettings/Issues orphaned-column no-op-Down migrations: restoring a unique
/// index on CommandId alone could fail against real post-migration data (a command with 2+ bindings).
/// </summary>
public class AddMultipleKeyBindingsPerCommandMigrationTests : IDisposable
{
    private const string PriorMigration = "20260907163358_AddLibraryHealthConfirmedMissingThreshold";
    private readonly string _dbPath;

    public AddMultipleKeyBindingsPerCommandMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_multikeybindings_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AllowsTwoDistinctGesturesForTheSameCommand()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.KeyBindings.Add(new KeyBinding { CommandId = "Reader.PageTurnLeft", Key = "Left" });
        context.KeyBindings.Add(new KeyBinding { CommandId = "Reader.PageTurnLeft", Key = "J" });
        context.SaveChanges();

        Assert.Equal(2, context.KeyBindings.Count(k => k.CommandId == "Reader.PageTurnLeft"));
    }

    [Fact]
    public void Migration_StillRejectsTheExactSameGestureTwiceForOneCommand()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.KeyBindings.Add(new KeyBinding { CommandId = "Reader.PageTurnLeft", Key = "Left" });
        context.SaveChanges();

        context.KeyBindings.Add(new KeyBinding { CommandId = "Reader.PageTurnLeft", Key = "Left" });
        Assert.ThrowsAny<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void Down_DropsTheCompositeIndex_WithoutRestoringTheOldSingleColumnOne()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.GetService<IMigrator>().Migrate(PriorMigration);

        var indexes = context.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_index_list('KeyBindings');")
            .ToList();
        Assert.DoesNotContain("IX_KeyBindings_CommandId_Key", indexes);
        Assert.DoesNotContain("IX_KeyBindings_CommandId", indexes);
    }
}
