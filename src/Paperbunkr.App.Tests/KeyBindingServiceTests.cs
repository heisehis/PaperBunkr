using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="KeyBindingService"/> (docs/Paperbunkr-Roadmap.md P5 follow-up, extended by
/// docs/superpowers/specs/2026-08-16-remappable-reader-shortcuts-design.md and
/// docs/superpowers/specs/2026-09-07-keyboard-shortcuts-redesign-design.md's multi-binding support).
/// Uses an injected in-memory-database context factory (same test-injection seam as
/// <see cref="SkinService"/>) so tests never touch the real per-user database.
/// </summary>
public class KeyBindingServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public KeyBindingServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_keybindings_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
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

    private KeyBindingService CreateService() => new(() => new PaperbunkrDbContext(_dbOptions));

    [Fact]
    public void GetKeys_NoRowStored_ReturnsRegistryDefault()
    {
        var service = CreateService();

        Assert.Equal([new KeyGesture(Key.Left)], service.GetKeys(KeyboardCommandRegistry.ReaderPageTurnLeft));
        Assert.Equal([new KeyGesture(Key.Right)], service.GetKeys(KeyboardCommandRegistry.ReaderPageTurnRight));
    }

    [Fact]
    public void GetKeys_NoRowStored_ReturnsModifierRegistryDefault()
    {
        var service = CreateService();

        Assert.Equal([new KeyGesture(Key.R, KeyModifiers.Shift)], service.GetKeys(KeyboardCommandRegistry.ReaderRotateCounterClockwise));
    }

    [Fact]
    public void AddKey_ThenGetKeys_ReturnsRemappedValue()
    {
        var service = CreateService();

        service.AddKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));

        Assert.Equal([new KeyGesture(Key.J)], service.GetKeys(KeyboardCommandRegistry.ReaderPageTurnLeft));
        // The other command is untouched.
        Assert.Equal([new KeyGesture(Key.Right)], service.GetKeys(KeyboardCommandRegistry.ReaderPageTurnRight));
    }

    [Fact]
    public void AddKey_ThenGetKeys_RoundTripsModifierGesture()
    {
        var service = CreateService();

        service.AddKey(KeyboardCommandRegistry.ReaderRotateCounterClockwise, new KeyGesture(Key.R, KeyModifiers.Shift));

        Assert.Equal([new KeyGesture(Key.R, KeyModifiers.Shift)], service.GetKeys(KeyboardCommandRegistry.ReaderRotateCounterClockwise));
    }

    [Fact]
    public void AddKey_CalledTwiceWithSameGesture_DoesNotDuplicate()
    {
        var service = CreateService();

        service.AddKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));
        service.AddKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.KeyBindings.Where(k => k.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft));
    }

    [Fact]
    public void AddKey_DifferentGesture_AddsSecondBinding_BothReturnedByGetKeys()
    {
        var service = CreateService();

        service.AddKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));
        service.AddKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.K));

        var keys = service.GetKeys(KeyboardCommandRegistry.ReaderPageTurnLeft);
        // Stored rows are authoritative once any exist - the registry default ("Left") isn't
        // implicitly appended alongside explicit bindings.
        Assert.Equal(2, keys.Count);
        Assert.Contains(new KeyGesture(Key.J), keys);
        Assert.Contains(new KeyGesture(Key.K), keys);
    }

    [Fact]
    public void RemoveKey_DeletesMatchingRow()
    {
        var service = CreateService();
        service.AddKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));

        service.RemoveKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.KeyBindings.Where(k => k.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft));
        // Falls back to the registry default now that the only stored row is gone.
        Assert.Equal([new KeyGesture(Key.Left)], service.GetKeys(KeyboardCommandRegistry.ReaderPageTurnLeft));
    }

    [Fact]
    public void ReplaceKeys_ClearsExistingThenAddsGiven()
    {
        var service = CreateService();
        service.AddKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));
        service.AddKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.K));

        service.ReplaceKeys(KeyboardCommandRegistry.ReaderPageTurnLeft, [new KeyGesture(Key.A)]);

        Assert.Equal([new KeyGesture(Key.A)], service.GetKeys(KeyboardCommandRegistry.ReaderPageTurnLeft));
    }

    [Fact]
    public void ResetToDefaults_ClearsEveryRow_GetAllBindingsReturnsRegistryDefaults()
    {
        var service = CreateService();
        service.AddKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));
        service.AddKey(KeyboardCommandRegistry.ReaderZoomIn, new KeyGesture(Key.X));

        service.ResetToDefaults();

        var all = service.GetAllBindings();
        Assert.All(all, b => Assert.Equal([b.Command.DefaultGesture], b.Keys));
    }

    [Fact]
    public void GetAllBindings_ReturnsEveryRegistryCommand_WithCurrentKeys()
    {
        var service = CreateService();
        service.AddKey(KeyboardCommandRegistry.ReaderPageTurnRight, new KeyGesture(Key.K));

        var all = service.GetAllBindings();

        Assert.Equal(KeyboardCommandRegistry.Commands.Count, all.Count);
        Assert.Contains(all, b => b.Command.Id == KeyboardCommandRegistry.ReaderPageTurnLeft && b.Keys.SequenceEqual([new KeyGesture(Key.Left)]));
        Assert.Contains(all, b => b.Command.Id == KeyboardCommandRegistry.ReaderPageTurnRight && b.Keys.Contains(new KeyGesture(Key.K)));
    }

    [Fact]
    public void GetKeys_PaperbunkrDbContextOverload_MatchesOwnContextOverload()
    {
        var service = CreateService();
        service.AddKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.A));

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal([new KeyGesture(Key.A)], service.GetKeys(context, KeyboardCommandRegistry.ReaderPageTurnLeft));
    }
}
