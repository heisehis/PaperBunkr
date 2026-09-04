using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="KeyBindingService"/> (docs/Paperbunkr-Roadmap.md P5 follow-up, extended by
/// docs/superpowers/specs/2026-08-16-remappable-reader-shortcuts-design.md). Uses an injected
/// in-memory-database context factory (same test-injection seam as <see cref="SkinService"/>) so
/// tests never touch the real per-user database.
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
    public void GetKey_NoRowStored_ReturnsRegistryDefault()
    {
        var service = CreateService();

        Assert.Equal(new KeyGesture(Key.Left), service.GetKey(KeyboardCommandRegistry.ReaderPageTurnLeft));
        Assert.Equal(new KeyGesture(Key.Right), service.GetKey(KeyboardCommandRegistry.ReaderPageTurnRight));
    }

    [Fact]
    public void GetKey_NoRowStored_ReturnsModifierRegistryDefault()
    {
        var service = CreateService();

        Assert.Equal(new KeyGesture(Key.R, KeyModifiers.Shift), service.GetKey(KeyboardCommandRegistry.ReaderRotateCounterClockwise));
    }

    [Fact]
    public void SetKey_ThenGetKey_ReturnsRemappedValue()
    {
        var service = CreateService();

        service.SetKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));

        Assert.Equal(new KeyGesture(Key.J), service.GetKey(KeyboardCommandRegistry.ReaderPageTurnLeft));
        // The other command is untouched.
        Assert.Equal(new KeyGesture(Key.Right), service.GetKey(KeyboardCommandRegistry.ReaderPageTurnRight));
    }

    [Fact]
    public void SetKey_ThenGetKey_RoundTripsModifierGesture()
    {
        var service = CreateService();

        service.SetKey(KeyboardCommandRegistry.ReaderRotateCounterClockwise, new KeyGesture(Key.R, KeyModifiers.Shift));

        Assert.Equal(new KeyGesture(Key.R, KeyModifiers.Shift), service.GetKey(KeyboardCommandRegistry.ReaderRotateCounterClockwise));
    }

    [Fact]
    public void SetKey_CalledTwice_UpdatesExistingRow_DoesNotDuplicate()
    {
        var service = CreateService();

        service.SetKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.J));
        service.SetKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.A));

        Assert.Equal(new KeyGesture(Key.A), service.GetKey(KeyboardCommandRegistry.ReaderPageTurnLeft));

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.KeyBindings.Where(k => k.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft));
    }

    [Fact]
    public void GetAllBindings_ReturnsEveryRegistryCommand_WithCurrentKeys()
    {
        var service = CreateService();
        service.SetKey(KeyboardCommandRegistry.ReaderPageTurnRight, new KeyGesture(Key.K));

        var all = service.GetAllBindings();

        Assert.Equal(KeyboardCommandRegistry.Commands.Count, all.Count);
        Assert.Contains(all, b => b.Command.Id == KeyboardCommandRegistry.ReaderPageTurnLeft && b.CurrentKey == new KeyGesture(Key.Left));
        Assert.Contains(all, b => b.Command.Id == KeyboardCommandRegistry.ReaderPageTurnRight && b.CurrentKey == new KeyGesture(Key.K));
    }

    [Fact]
    public void GetKey_PaperbunkrDbContextOverload_MatchesOwnContextOverload()
    {
        var service = CreateService();
        service.SetKey(KeyboardCommandRegistry.ReaderPageTurnLeft, new KeyGesture(Key.A));

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(new KeyGesture(Key.A), service.GetKey(context, KeyboardCommandRegistry.ReaderPageTurnLeft));
    }
}
