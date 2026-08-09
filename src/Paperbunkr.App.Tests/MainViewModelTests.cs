using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using System.Linq;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="MainViewModel.EscapeCommand"/> (P5, docs/alpha-roadmap.md) - the single
/// app-wide Esc-to-close/cancel routing, since none of Migration/Issue Properties/Bulk Editing are
/// real Avalonia Windows/Popups with native dialog-Escape behavior. Redirects
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file, same approach as
/// <see cref="DetailScreenViewModelTests"/>, since closing the Migration overlay reloads the
/// Library from the database. Joins <see cref="AvaloniaTestCollection"/> since that override is a
/// shared static other test classes also mutate.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MainViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public MainViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mainvm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
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

    [Fact]
    public void Escape_NoDialogActive_NoOps()
    {
        var vm = new MainViewModel();

        vm.EscapeCommand.Execute(null);

        Assert.True(vm.IsLibrary);
    }

    [Fact]
    public void Escape_MigrationOverlayOpen_ClosesIt()
    {
        var vm = new MainViewModel();
        vm.IsMigrationOverlayOpen = true;

        vm.EscapeCommand.Execute(null);

        Assert.False(vm.IsMigrationOverlayOpen);
    }

    [Fact]
    public void Escape_IssuePropertiesOpen_CancelsBackToDetail()
    {
        var vm = new MainViewModel();
        vm.CurrentScreen = "issueProperties";

        vm.EscapeCommand.Execute(null);

        Assert.True(vm.IsDetail);
        Assert.False(vm.IsIssueProperties);
    }

    [Fact]
    public void Escape_BulkIssuePropertiesOpen_CancelsBackToDetail()
    {
        var vm = new MainViewModel();
        vm.CurrentScreen = "bulkIssueProperties";

        vm.EscapeCommand.Execute(null);

        Assert.True(vm.IsDetail);
        Assert.False(vm.IsBulkIssueProperties);
    }

    /// <summary>
    /// Regression test for a real bug: Library loads once at MainViewModel construction and never
    /// again (unlike Smart/Reading, which reload via EnsureListLoaded on every navigation). Data
    /// that lands after construction - a Book Folders scan, a CE migration commit reached via any
    /// path other than the migration overlay's own "X" close button, a direct DB write - never
    /// appeared until GoLibrary itself started reloading too.
    /// </summary>
    [Fact]
    public void GoLibrary_ReloadsFromDatabase_PickingUpDataAddedAfterConstruction()
    {
        var vm = new MainViewModel();
        Assert.Equal(0, vm.Library.AllSeriesCount);

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(options))
        {
            context.Series.Add(new Paperbunkr.Data.Entities.Series { Name = "Late-Arriving Series" });
            context.SaveChanges();
        }

        vm.GoLibraryCommand.Execute(null);

        Assert.Equal(1, vm.Library.AllSeriesCount);
    }

    /// <summary>
    /// P6 follow-up (docs/alpha-todo.md): unlike CE's <c>ComicBookDialog</c> (a true modal that
    /// blocks all other interaction by construction), Issue Properties/Bulk Editing here are just
    /// screen-swaps within one window, so the rail nav stayed fully clickable mid-edit with no
    /// warning until <see cref="MainViewModel.TryLeaveCurrentEditor"/> was added.
    /// </summary>
    [Fact]
    public void GoLibrary_WithDirtyIssueProperties_PromptsInsteadOfNavigating()
    {
        var vm = new MainViewModel();
        vm.CurrentScreen = "issueProperties";
        vm.IssueProperties.Title = "Unsaved Edit";

        vm.GoLibraryCommand.Execute(null);

        Assert.True(vm.IsDiscardConfirmOpen);
        Assert.True(vm.IsIssueProperties);
        Assert.False(vm.IsLibrary);
    }

    [Fact]
    public void GoLibrary_WithCleanIssueProperties_NavigatesImmediately_NoPrompt()
    {
        var vm = new MainViewModel();
        vm.CurrentScreen = "issueProperties";

        vm.GoLibraryCommand.Execute(null);

        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.True(vm.IsLibrary);
    }

    [Fact]
    public void ConfirmDiscard_RunsThePendingNavigation()
    {
        var vm = new MainViewModel();
        vm.CurrentScreen = "issueProperties";
        vm.IssueProperties.Title = "Unsaved Edit";
        vm.GoLibraryCommand.Execute(null);
        Assert.True(vm.IsDiscardConfirmOpen);

        vm.ConfirmDiscardCommand.Execute(null);

        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.True(vm.IsLibrary);
    }

    [Fact]
    public void CancelDiscard_StaysPut_AndDropsThePendingNavigation()
    {
        var vm = new MainViewModel();
        vm.CurrentScreen = "issueProperties";
        vm.IssueProperties.Title = "Unsaved Edit";
        vm.GoLibraryCommand.Execute(null);
        Assert.True(vm.IsDiscardConfirmOpen);

        vm.CancelDiscardCommand.Execute(null);

        Assert.False(vm.IsDiscardConfirmOpen);
        Assert.True(vm.IsIssueProperties);
        Assert.False(vm.IsLibrary);
    }

    [Fact]
    public void GoLibrary_WithDirtyBulkIssueProperties_PromptsInsteadOfNavigating()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        int issueId;
        using (var context = new PaperbunkrDbContext(options))
        {
            var series = new Paperbunkr.Data.Entities.Series { Name = "Test Series" };
            context.Series.Add(series);
            context.SaveChanges();
            var issue = new Paperbunkr.Data.Entities.Issue { SeriesId = series.Id, Number = "1", Publisher = "DC Comics" };
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;
        }

        var vm = new MainViewModel();
        vm.BulkIssueProperties.Load(new[] { issueId });
        vm.BulkIssueProperties.MainFields.Single(f => f.Label == "Publisher").Value = "Vertigo";
        vm.CurrentScreen = "bulkIssueProperties";

        vm.GoLibraryCommand.Execute(null);

        Assert.True(vm.IsDiscardConfirmOpen);
        Assert.True(vm.IsBulkIssueProperties);
    }
}
