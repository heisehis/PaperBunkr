using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary><see cref="QuickOpenViewModel"/> - query filtering, selection movement, activation.</summary>
public class QuickOpenViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public QuickOpenViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_quickopen_vm_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
        var s1 = new Series { Name = "Batman" };
        var s2 = new Series { Name = "Superman" };
        context.Series.AddRange(s1, s2);
        context.SaveChanges();
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

    private QuickOpenViewModel Create(Action<QuickOpenEntry>? activate = null, Action? close = null) =>
        new(activate ?? (_ => { }), close ?? (() => { }), new QuickOpenService(() => new PaperbunkrDbContext(_dbOptions)));

    [Fact]
    public void Open_ShowsRecencyListPlusScreens_AndClearsPriorQuery()
    {
        var vm = Create();
        vm.Open();
        vm.Query = "zzz";

        vm.Open();

        Assert.Equal(string.Empty, vm.Query);
        Assert.Contains(vm.Results, e => e.Kind == QuickOpenKind.Screen);
        Assert.All(vm.Results, e => Assert.NotEqual(QuickOpenKind.Action, e.Kind));
    }

    [Fact]
    public void Query_FiltersResults()
    {
        var vm = Create();
        vm.Open();

        vm.Query = "batman";

        Assert.Contains(vm.Results, e => e.Primary == "Batman");
        Assert.DoesNotContain(vm.Results, e => e.Primary == "Superman");
    }

    [Fact]
    public void MoveSelection_Clamps()
    {
        var vm = Create();
        vm.Open();

        vm.MoveSelection(-5);
        Assert.Equal(0, vm.SelectedIndex);
        vm.MoveSelection(999);
        Assert.Equal(vm.Results.Count - 1, vm.SelectedIndex);
    }

    [Fact]
    public void ActivateSelected_InvokesActivateThenClose()
    {
        QuickOpenEntry? activated = null;
        bool closed = false;
        var vm = Create(e => activated = e, () => closed = true);
        vm.Open();
        vm.Query = "superman";

        vm.ActivateSelected();

        Assert.Equal("Superman", activated?.Primary);
        Assert.True(closed);
    }

    [Fact]
    public void HasNoMatches_OnlyWhenNonEmptyQueryMatchesNothing()
    {
        var vm = Create();
        vm.Open();
        Assert.False(vm.HasNoMatches);

        vm.Query = "qqqqqqzzz";
        Assert.True(vm.HasNoMatches);
    }
}
