using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Input;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The one-time import of the old KeyBinding table / exported layouts into the new keymap (docs/superpowers/specs/2026-10-03-input-service-design.md §4 Q11).</summary>
public class LegacyKeyBindingImporterTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_legacy_keys_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public LegacyKeyBindingImporterTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext NewContext() => new(_options);

    private void Seed(params (string CommandId, string Key)[] rows)
    {
        using var context = NewContext();
        foreach (var (commandId, key) in rows)
        {
            context.KeyBindings.Add(new Paperbunkr.Data.Entities.KeyBinding { CommandId = commandId, Key = key });
        }

        context.SaveChanges();
    }

    private static InputService NewService(MemoryKeymapStore? store = null) => new(InputActionCatalog.CreateWithCoreActions(), store ?? new MemoryKeymapStore());

    [Fact]
    public void StoredRows_BecomeTheCommandsOnlyBindings()
    {
        var service = NewService();

        int applied = LegacyKeyBindingImporter.Apply(
            service.Config, service.Actions,
            [(InputActionIds.ZoomIn, "J"), (InputActionIds.ZoomIn, "Ctrl+Shift+J"), (InputActionIds.RotateClockwise, "K")]);
        service.SaveConfig();

        Assert.Equal(3, applied);
        Assert.Equal([InputBinding.ForKey(Key.J), InputBinding.ForKey(Key.J, KeyModifiers.Control | KeyModifiers.Shift)], service.GetBindings(InputActionIds.ZoomIn));
        Assert.Equal([InputBinding.ForKey(Key.K)], service.GetBindings(InputActionIds.RotateClockwise));
    }

    [Fact]
    public void ACommandWithNoRows_KeepsItsDefaults()
    {
        var service = NewService();

        LegacyKeyBindingImporter.Apply(service.Config, service.Actions, [(InputActionIds.ZoomIn, "J")]);
        service.SaveConfig();

        Assert.Equal([InputBinding.ForKey(Key.R)], service.GetBindings(InputActionIds.RotateClockwise));
    }

    [Fact]
    public void UnknownCommands_UnparseableGestures_AndBlankIds_AreSkipped()
    {
        var service = NewService();

        int applied = LegacyKeyBindingImporter.Apply(
            service.Config, service.Actions,
            [("Reader.NoSuchCommand", "J"), (InputActionIds.ZoomIn, "not a gesture"), (InputActionIds.RotateClockwise, "K"), ("", "J"), ("  ", "J")]);

        Assert.Equal(1, applied);
        Assert.False(service.Config.Overrides.ContainsKey(InputActionIds.ZoomIn));
        Assert.True(service.Config.Overrides.ContainsKey(InputActionIds.RotateClockwise));
    }

    [Fact]
    public void ACommandWhoseRowsAllFailToParse_KeepsItsDefaults()
    {
        var service = NewService();

        LegacyKeyBindingImporter.Apply(service.Config, service.Actions, [(InputActionIds.ZoomIn, "garbage"), (InputActionIds.ZoomIn, "worse")]);
        service.SaveConfig();

        Assert.Contains(InputBinding.ForKey(Key.Z), service.GetBindings(InputActionIds.ZoomIn));
    }

    [Fact]
    public void RowsIdenticalToTheDefaults_StoreNoOverride()
    {
        var service = NewService();
        var defaults = service.GetBindings(InputActionIds.RotateClockwise).Select(b => (InputActionIds.RotateClockwise, b.ToString())).ToArray();

        LegacyKeyBindingImporter.Apply(service.Config, service.Actions, defaults);

        Assert.False(service.Config.Overrides.ContainsKey(InputActionIds.RotateClockwise));
    }

    [Fact]
    public void ImportOnce_ReadsTheDatabase_SetsTheFlag_AndSaves()
    {
        Seed((InputActionIds.ZoomIn, "J"), (InputActionIds.PageTurnLeft, "Left"), (InputActionIds.PageTurnLeft, "A"));
        var store = new MemoryKeymapStore();
        var service = NewService(store);

        int applied = LegacyKeyBindingImporter.ImportOnce(service, NewContext);

        Assert.Equal(3, applied);
        Assert.Equal([InputBinding.ForKey(Key.J)], service.GetBindings(InputActionIds.ZoomIn));
        Assert.Equal([InputBinding.ForKey(Key.Left), InputBinding.ForKey(Key.A)], service.GetBindings(InputActionIds.PageTurnLeft));
        Assert.True(store.Load().LegacyBindingsImported);
        Assert.Equal(["J"], store.Load().Overrides[InputActionIds.ZoomIn]);
    }

    [Fact]
    public void ImportOnce_NeverRunsTwice_AndNeverTouchesTheTable()
    {
        Seed((InputActionIds.ZoomIn, "J"));
        var store = new MemoryKeymapStore();
        var service = NewService(store);
        LegacyKeyBindingImporter.ImportOnce(service, NewContext);
        service.ResetAll();

        int second = LegacyKeyBindingImporter.ImportOnce(service, NewContext);

        Assert.Equal(0, second);
        Assert.Contains(InputBinding.ForKey(Key.Z), service.GetBindings(InputActionIds.ZoomIn));
        using var context = NewContext();
        Assert.Equal(1, context.KeyBindings.Count());
    }

    [Fact]
    public void ImportOnce_WithAnEmptyTable_StillMarksItDone()
    {
        var store = new MemoryKeymapStore();
        var service = NewService(store);

        Assert.Equal(0, LegacyKeyBindingImporter.ImportOnce(service, NewContext));

        Assert.True(store.Load().LegacyBindingsImported);
    }

    [Fact]
    public void ImportOnce_WithAnUnreadableDatabase_ReturnsZero_AndRetriesNextTime()
    {
        var store = new MemoryKeymapStore();
        var service = NewService(store);

        int applied = LegacyKeyBindingImporter.ImportOnce(service, () => throw new InvalidOperationException("no database"));

        Assert.Equal(0, applied);
        Assert.False(store.Load().LegacyBindingsImported);
    }

    [Fact]
    public void AnExportedLayout_IsParsedIntoRows_AndAppliedLikeTheTable()
    {
        const string json = """
            [
              { "CommandId": "Reader.ZoomIn", "Gesture": "J" },
              { "CommandId": "Reader.ZoomIn", "Gesture": "Ctrl+J" },
              { "CommandId": "", "Gesture": "X" }
            ]
            """;
        var service = NewService();

        var rows = LegacyKeyBindingImporter.ReadExportedLayout(json);
        int applied = LegacyKeyBindingImporter.Apply(service.Config, service.Actions, rows);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, applied);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    public void ANonLayoutFile_IsRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => LegacyKeyBindingImporter.ReadExportedLayout(json));
}
