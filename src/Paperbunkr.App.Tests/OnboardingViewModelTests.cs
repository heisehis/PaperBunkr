using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="OnboardingViewModel"/> - the general first-run welcome flow that replaced
/// unconditionally auto-opening <see cref="MigrationOverlayViewModel"/> on a detected ComicRack CE
/// install. Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file
/// since <see cref="OnboardingViewModel"/> has no injected context-factory seam of its own, same
/// approach as <see cref="NeedsReviewViewModelTests"/>; joins <see cref="AvaloniaTestCollection"/>
/// for the same reason.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class OnboardingViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _scanRoot;

    public OnboardingViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_onboardingvm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _scanRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_onboardingvm_scan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scanRoot);
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_scanRoot)) Directory.Delete(_scanRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private OnboardingViewModel CreateViewModel(
        IFilePickerService? filePicker = null,
        Action? reloadFolderWatch = null,
        Action? onImportFromCe = null,
        Action? onFinished = null)
    {
        var scanner = new LibraryFolderScanner(() => new PaperbunkrDbContext(_dbOptions));
        return new OnboardingViewModel(
            filePicker ?? new StubFilePicker(),
            scanner,
            reloadFolderWatch ?? (() => { }),
            onImportFromCe ?? (() => { }),
            onFinished ?? (() => { }));
    }

    [Fact]
    public void Open_ResetsToChoiceStage()
    {
        var vm = CreateViewModel();
        vm.AddAnotherFolderCommand.Execute(null); // no-op from Choice, just exercising the surface

        vm.Open();

        Assert.True(vm.IsChoice);
    }

    [Fact]
    public void Finish_InvokesOnFinishedCallback()
    {
        bool finished = false;
        var vm = CreateViewModel(onFinished: () => finished = true);

        vm.FinishCommand.Execute(null);

        Assert.True(finished);
    }

    [Fact]
    public void ImportFromCe_InvokesOnImportFromCeCallback_WithoutTouchingStage()
    {
        bool importRequested = false;
        var vm = CreateViewModel(onImportFromCe: () => importRequested = true);

        vm.ImportFromCeCommand.Execute(null);

        Assert.True(importRequested);
        // Onboarding hands off to the Migration overlay rather than switching its own Stage - the
        // shell (MainViewModel) is what actually closes this overlay and opens Migration.
        Assert.True(vm.IsChoice);
    }

    [Fact]
    public async Task AddFolder_UserCancels_StaysOnChoiceStage_AndAddsNoWatchedFolder()
    {
        var vm = CreateViewModel(filePicker: new StubFilePicker { FolderToReturn = null });

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.True(vm.IsChoice);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.WatchedFolders);
    }

    [Fact]
    public async Task AddFolder_EmptyFolder_PersistsWatchedFolder_AndLandsOnDoneWithNoIssuesAdded()
    {
        var vm = CreateViewModel(filePicker: new StubFilePicker { FolderToReturn = _scanRoot });

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.True(vm.IsDone);
        Assert.False(vm.HasIssuesAdded);
        Assert.Contains("No comics found", vm.ResultSummaryLabel);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var folder = Assert.Single(context.WatchedFolders);
        Assert.Equal(_scanRoot, folder.Path);
    }

    [Fact]
    public async Task AddFolder_ReloadsFolderWatch()
    {
        bool reloaded = false;
        var vm = CreateViewModel(filePicker: new StubFilePicker { FolderToReturn = _scanRoot }, reloadFolderWatch: () => reloaded = true);

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.True(reloaded);
    }

    [Fact]
    public void AddAnotherFolder_FromDone_ReturnsToChoiceStage()
    {
        var vm = CreateViewModel();
        vm.Stage = OnboardingStage.Done;

        vm.AddAnotherFolderCommand.Execute(null);

        Assert.True(vm.IsChoice);
    }

    private sealed class StubFilePicker : IFilePickerService
    {
        public string? FolderToReturn { get; set; }

        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderToReturn);

        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }
}
