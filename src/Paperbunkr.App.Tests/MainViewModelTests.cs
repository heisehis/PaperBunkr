using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;

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
}
