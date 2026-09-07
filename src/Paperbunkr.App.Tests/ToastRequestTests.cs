using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="MainViewModel.ShowToast(string,string)"/> wraps its plain title+message into a
/// <see cref="ToastRequest"/> (docs/superpowers/specs/2026-09-06-feedback-notification-system-
/// design.md §5 / plan Step 9) - every existing caller of that overload is unaffected, only the
/// <c>ToastRequested</c> event's payload shape changed.
///
/// Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file, same
/// approach as <see cref="MainViewModelTests"/> - <c>new MainViewModel()</c> touches the real
/// per-user database otherwise, which failed here once with "database disk image is malformed"
/// (unrelated to this change - a real environment issue with the dev DB, not something to paper
/// over by skipping isolation).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ToastRequestTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly CoverCacheTestRedirect _coverRedirect;

    public ToastRequestTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_toastrequest_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        _coverRedirect = new CoverCacheTestRedirect();

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        _coverRedirect.Dispose();
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
    public void ShowToastForPlugin_RaisesAToastRequest_WithDefaultInfoSeverity()
    {
        var vm = new MainViewModel();
        ToastRequest? captured = null;
        vm.ToastRequested += r => captured = r;

        vm.ShowToastForPlugin("Plugin error", "Something went wrong.");

        Assert.NotNull(captured);
        Assert.Equal("Plugin error", captured!.Title);
        Assert.Equal("Something went wrong.", captured.Message);
        Assert.Equal(ToastSeverity.Info, captured.Severity);
        Assert.Null(captured.Actions);
    }

    [Fact]
    public void ShowMinimizeToTrayNotice_RaisesAToastRequest()
    {
        var vm = new MainViewModel();
        ToastRequest? captured = null;
        vm.ToastRequested += r => captured = r;

        vm.ShowMinimizeToTrayNotice();

        Assert.NotNull(captured);
        Assert.Equal("Still running", captured!.Title);
    }
}
