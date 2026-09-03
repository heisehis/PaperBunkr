using System;
using System.IO;
using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Books Saved Workspaces - the three-field (sort / direction / group) counterpart to
/// <see cref="LibraryWorkspaceTests"/> (docs/superpowers/specs/2026-09-03-library-saved-workspaces-
/// design.md).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BooksWorkspaceTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public BooksWorkspaceTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_books_workspace_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static BooksScreenViewModel CreateVm(string enteredName = "Mine") =>
        new(
            goBookDetail: _ => { }, goBookSeriesDetail: _ => { }, goEditBook: _ => { },
            goBulkEdit: _ => { }, goEditSeries: _ => { }, goLibrarySettings: () => { },
            promptForName: (_, cb) => cb(enteredName));

    private static int? ActiveWorkspaceIdInDb()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.GetOrCreateAppSettings().BooksActiveWorkspaceId;
    }

    [Fact]
    public void SaveThenApply_RestoresSortAndGroup()
    {
        var vm = CreateVm();
        vm.SetSortFieldCommand.Execute(BooksSortField.Author);
        vm.SetGroupFieldCommand.Execute(BooksGroupField.Series);

        vm.SaveWorkspaceAsCommand.Execute(null);
        int id = vm.Workspaces.Single(w => !w.IsBuiltIn).Id;

        vm.SetSortFieldCommand.Execute(BooksSortField.Title);
        vm.SetGroupFieldCommand.Execute(BooksGroupField.None);

        vm.ApplyWorkspaceCommand.Execute(id);

        Assert.Equal(BooksSortField.Author, vm.SortField);
        Assert.Equal(BooksGroupField.Series, vm.GroupField);
    }

    [Fact]
    public void Apply_SetsLabelAndPersistsId_ClearedByAGovernedChange()
    {
        new WorkspaceService().EnsureBuiltInsSeeded();
        var vm = CreateVm();
        int bySeries = vm.Workspaces.Single(w => w.Name == "By series").Id;

        vm.ApplyWorkspaceCommand.Execute(bySeries);
        Assert.Equal("By series", vm.ActiveWorkspaceLabel);
        Assert.Equal(bySeries, ActiveWorkspaceIdInDb());

        vm.SetSortFieldCommand.Execute(BooksSortField.Author);
        Assert.Equal("Workspace", vm.ActiveWorkspaceLabel);
        Assert.Null(ActiveWorkspaceIdInDb());
    }

    [Fact]
    public void BySeriesBuiltIn_GroupsBySeries()
    {
        new WorkspaceService().EnsureBuiltInsSeeded();
        var vm = CreateVm();

        vm.ApplyWorkspaceCommand.Execute(vm.Workspaces.Single(w => w.Name == "By series").Id);

        Assert.Equal(BooksGroupField.Series, vm.GroupField);
        Assert.True(vm.IsGrouped);
    }

    [Fact]
    public void Apply_SurvivesRestart()
    {
        new WorkspaceService().EnsureBuiltInsSeeded();
        var vm = CreateVm();
        vm.ApplyWorkspaceCommand.Execute(vm.Workspaces.Single(w => w.Name == "Recently added").Id);

        var reopened = CreateVm();
        Assert.Equal("Recently added", reopened.ActiveWorkspaceLabel);
        Assert.Equal(BooksSortField.RecentlyAdded, reopened.SortField);
    }
}
