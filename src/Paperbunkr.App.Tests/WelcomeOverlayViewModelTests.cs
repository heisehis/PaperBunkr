using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The first-run welcome screen (docs/superpowers/specs/2026-08-31-first-run-onboarding-design.md) -
/// each setup path's command logic. Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/>
/// to a temp SQLite file, same pattern as <see cref="NewReadingListViewModelTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class WelcomeOverlayViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly FakeFilePickerService _filePicker = new();
    private int _reloadCount;
    private int _migrationOpenCount;
    private int _closeCount;

    public WelcomeOverlayViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pbwelcome-{Guid.NewGuid():N}.db");
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.Migrate();
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

    private WelcomeOverlayViewModel CreateViewModel() =>
        new(_filePicker, () => _reloadCount++, () => _migrationOpenCount++, () => _closeCount++);

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public string? FolderToReturn;
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderToReturn);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    [Fact]
    public async Task AddComicFolder_PickedPath_InsertsWatchedFolderAndCloses()
    {
        _filePicker.FolderToReturn = "C:\\Comics";
        var vm = CreateViewModel();

        await vm.AddComicFolderCommand.ExecuteAsync(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Single(context.WatchedFolders, w => w.Path == "C:\\Comics");
        Assert.Equal(1, _reloadCount);
        Assert.Equal(1, _closeCount);
    }

    [Fact]
    public async Task AddComicFolder_CancelledPicker_DoesNothing()
    {
        _filePicker.FolderToReturn = null;
        var vm = CreateViewModel();

        await vm.AddComicFolderCommand.ExecuteAsync(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.WatchedFolders);
        Assert.Equal(0, _reloadCount);
        Assert.Equal(0, _closeCount);
    }

    [Fact]
    public async Task AddComicFolder_DuplicatePath_NotReinserted()
    {
        _filePicker.FolderToReturn = "C:\\Comics";
        var vm = CreateViewModel();

        await vm.AddComicFolderCommand.ExecuteAsync(null);
        await vm.AddComicFolderCommand.ExecuteAsync(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Single(context.WatchedFolders.Where(w => w.Path == "C:\\Comics"));
        Assert.Equal(2, _closeCount);
    }

    [Fact]
    public async Task AddBookFolder_PickedPath_InsertsBookFolderAndCloses()
    {
        _filePicker.FolderToReturn = "C:\\Books";
        var vm = CreateViewModel();

        await vm.AddBookFolderCommand.ExecuteAsync(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Single(context.BookFolders, f => f.Path == "C:\\Books");
        Assert.Equal(0, _reloadCount);
        Assert.Equal(1, _closeCount);
    }

    [Fact]
    public void ImportFromCe_InvokesCallbackAndCloses()
    {
        var vm = CreateViewModel();

        vm.ImportFromCeCommand.Execute(null);

        Assert.Equal(1, _migrationOpenCount);
        Assert.Equal(1, _closeCount);
    }

    [Fact]
    public void Skip_JustCloses()
    {
        var vm = CreateViewModel();

        vm.SkipCommand.Execute(null);

        Assert.Equal(1, _closeCount);
        Assert.Equal(0, _migrationOpenCount);
    }

    [Fact]
    public void CeInstallDetected_ReflectsAssignedValue()
    {
        var vm = CreateViewModel();

        vm.CeInstallDetected = true;

        Assert.True(vm.CeInstallDetected);
    }
}
