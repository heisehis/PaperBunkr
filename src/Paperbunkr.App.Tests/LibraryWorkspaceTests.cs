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
/// Library Saved Workspaces - capture / apply round-trip, active-label tracking, overwrite-by-name,
/// stale-collection fallback, and a built-in starter (docs/superpowers/specs/2026-09-03-library-
/// saved-workspaces-design.md). Uses the <see cref="PaperbunkrDbContext.DatabasePathOverride"/>
/// temp-file isolation <see cref="LibraryScreenViewModelTests"/> already relies on.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryWorkspaceTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public LibraryWorkspaceTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_library_workspace_test_{Guid.NewGuid():N}.db");
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

    private static LibraryScreenViewModel CreateVm(string enteredName = "My workspace") =>
        new(
            goDetail: _ => { },
            goReaderForIssue: _ => { },
            goToNewIssueProperties: (_, _, _) => { },
            promptForName: (_, cb) => cb(enteredName));

    private static int? ActiveWorkspaceIdInDb()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.GetOrCreateAppSettings().LibraryActiveWorkspaceId;
    }

    [Fact]
    public void SaveThenApply_RestoresTheCapturedViewState()
    {
        var vm = CreateVm();
        vm.SetViewModeCommand.Execute(LibraryViewMode.List);
        vm.IssueList.SortField = IssueListSortField.Title;
        vm.FilterUnreadOnly = true;

        vm.SaveWorkspaceAsCommand.Execute(null);
        int savedId = vm.Workspaces.Single(w => !w.IsBuiltIn).Id;

        vm.SetViewModeCommand.Execute(LibraryViewMode.PosterGrid);
        vm.IssueList.SortField = IssueListSortField.Added;
        vm.FilterUnreadOnly = false;

        vm.ApplyWorkspaceCommand.Execute(savedId);

        Assert.Equal(LibraryViewMode.List, vm.ViewMode);
        Assert.Equal(IssueListSortField.Title, vm.IssueList.SortField);
        Assert.True(vm.FilterUnreadOnly);
    }

    [Fact]
    public void Apply_SetsActiveLabelAndPersistsId_ThenAGovernedChangeClearsBoth()
    {
        new WorkspaceService().EnsureBuiltInsSeeded();
        var vm = CreateVm();
        int mangaId = vm.Workspaces.Single(w => w.Name == "Manga").Id;

        vm.ApplyWorkspaceCommand.Execute(mangaId);

        Assert.Equal("Manga", vm.ActiveWorkspaceLabel);
        Assert.Equal(mangaId, ActiveWorkspaceIdInDb());

        vm.IssueList.SortField = IssueListSortField.Title;

        Assert.Equal("Workspace", vm.ActiveWorkspaceLabel);
        Assert.Null(ActiveWorkspaceIdInDb());
    }

    [Fact]
    public void Apply_SurvivesAppRestart_UntilSomethingChanges()
    {
        new WorkspaceService().EnsureBuiltInsSeeded();
        var vm = CreateVm();
        int manga = vm.Workspaces.Single(w => w.Name == "Manga").Id;
        vm.ApplyWorkspaceCommand.Execute(manga);

        var reopened = CreateVm();

        Assert.Equal("Manga", reopened.ActiveWorkspaceLabel);
        Assert.Equal(LibraryContentGranularity.Series, reopened.Granularity);
        Assert.Equal(LibraryViewMode.PosterGrid, reopened.ViewMode);
        Assert.False(reopened.IsAllSeriesActive);
    }

    [Fact]
    public void CurrentlyReadingBuiltIn_AppliesUnreadFilterAndOpenedSort()
    {
        new WorkspaceService().EnsureBuiltInsSeeded();
        var vm = CreateVm();

        vm.ApplyWorkspaceCommand.Execute(vm.Workspaces.Single(w => w.Name == "Currently reading").Id);

        Assert.True(vm.FilterUnreadOnly);
        Assert.Equal(IssueListSortField.Opened, vm.IssueList.SortField);
        Assert.Equal(SortDirection.Descending, vm.IssueList.SortDirection);
    }

    [Fact]
    public void Apply_WithDeletedCollectionReference_FallsBackToAllSeries()
    {
        int collectionId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var collection = new Collection { Name = "Temp" };
            context.Collections.Add(collection);
            context.SaveChanges();
            collectionId = collection.Id;
        }

        var vm = CreateVm();
        vm.SelectCollectionById(collectionId);
        vm.SaveWorkspaceAsCommand.Execute(null);
        int savedId = vm.Workspaces.Single(w => !w.IsBuiltIn).Id;

        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Collections.Remove(context.Collections.Single(c => c.Id == collectionId));
            context.SaveChanges();
        }

        var reopened = CreateVm();
        reopened.ApplyWorkspaceCommand.Execute(savedId);

        Assert.True(reopened.IsAllSeriesActive);
    }

    [Fact]
    public void DeleteActiveWorkspace_ClearsTheLabel_ButLeavesTheViewAlone()
    {
        var vm = CreateVm();
        vm.SetViewModeCommand.Execute(LibraryViewMode.Tiles);
        vm.SaveWorkspaceAsCommand.Execute(null);
        int id = vm.Workspaces.Single(w => !w.IsBuiltIn).Id;
        vm.ApplyWorkspaceCommand.Execute(id);

        vm.DeleteWorkspaceCommand.Execute(id);

        Assert.Equal("Workspace", vm.ActiveWorkspaceLabel);
        Assert.Equal(LibraryViewMode.Tiles, vm.ViewMode);
        Assert.Null(ActiveWorkspaceIdInDb());
    }

    [Fact]
    public void SaveWorkspaceAs_ReusingAUserName_OverwritesInPlace()
    {
        var vm = CreateVm(enteredName: "Weekly");
        vm.SetViewModeCommand.Execute(LibraryViewMode.List);
        vm.SaveWorkspaceAsCommand.Execute(null);

        vm.SetViewModeCommand.Execute(LibraryViewMode.Tiles);
        vm.SaveWorkspaceAsCommand.Execute(null); // same name "Weekly"

        var weekly = vm.Workspaces.Where(w => !w.IsBuiltIn).ToList();
        Assert.Single(weekly);

        var fresh = CreateVm();
        fresh.ApplyWorkspaceCommand.Execute(weekly[0].Id);
        Assert.Equal(LibraryViewMode.Tiles, fresh.ViewMode);
    }

    [Fact]
    public void ResetToDefaultView_AppliesTheAllComicsBuiltIn()
    {
        new WorkspaceService().EnsureBuiltInsSeeded();
        var vm = CreateVm();
        vm.SetViewModeCommand.Execute(LibraryViewMode.Details);
        vm.FilterUnreadOnly = true;

        vm.ResetToDefaultViewCommand.Execute(null);

        Assert.Equal(LibraryViewMode.PosterGrid, vm.ViewMode);
        Assert.False(vm.FilterUnreadOnly);
        Assert.Equal("All comics", vm.ActiveWorkspaceLabel);
    }
}
