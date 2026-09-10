using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Coverage for <see cref="KeyBindingRowViewModel"/>'s <c>SuggestBox</c> "Add shortcut…" picker
/// projection (docs/superpowers/specs/2026-09-10-suggestbox-migration-plan.md). The picker moved
/// from a <c>ComboBox</c> bound to <c>PendingAddOption</c> to a string-only <c>SuggestBox</c>
/// bound to <see cref="KeyBindingRowViewModel.PendingAddText"/>.
/// </summary>
public class KeyBindingRowViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public KeyBindingRowViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_keybindingrow_test_{Guid.NewGuid():N}.db");
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

    private KeyBindingRowViewModel CreateRow(List<int>? changes = null)
    {
        var service = new KeyBindingService(() => new PaperbunkrDbContext(_dbOptions));
        var descriptor = KeyboardCommandRegistry.Commands.First(c => c.Id == KeyboardCommandRegistry.ReaderPageTurnLeft);
        return new KeyBindingRowViewModel(descriptor, service.GetKeys(descriptor.Id), service, () => changes?.Add(1));
    }

    [Fact]
    public void PendingAddText_GetterIsAlwaysEmpty()
    {
        Assert.Equal(string.Empty, CreateRow().PendingAddText);
    }

    [Fact]
    public void AvailableKeyOptionNames_AreTheCuratedLabelsMinusWhatIsBound()
    {
        var row = CreateRow();

        var expected = KeyOptions.All.Where(o => !row.BoundKeys.Contains(o)).Select(o => o.Label);
        Assert.Equal(expected, row.AvailableKeyOptionNames);
        Assert.DoesNotContain(row.BoundKeys.Single().Label, row.AvailableKeyOptionNames);
    }

    [Fact]
    public void PendingAddText_SetToACuratedLabel_AddsThatGesture()
    {
        var changes = new List<int>();
        var row = CreateRow(changes);
        var toAdd = row.AvailableKeyOptionNames.First();

        row.PendingAddText = toAdd;

        Assert.Contains(row.BoundKeys, k => k.Label == toAdd);
        Assert.NotEmpty(changes);
        Assert.DoesNotContain(toAdd, row.AvailableKeyOptionNames);
    }

    [Fact]
    public void PendingAddText_SetToUnknownText_AddsNothing()
    {
        var row = CreateRow();
        var boundBefore = row.BoundKeys.Count;

        row.PendingAddText = "Ctrl+Shift+Whatever";

        Assert.Equal(boundBefore, row.BoundKeys.Count);
    }
}
