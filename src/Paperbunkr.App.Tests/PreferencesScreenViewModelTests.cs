using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PreferencesScreenViewModel"/> (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md
/// §5) against a real <see cref="SkinService"/> pointed at a temp skins folder + in-memory-database
/// context factory, same isolation approach as <see cref="SkinServiceTests"/>. Joins
/// <see cref="AvaloniaTestCollection"/> since skin selection touches Application resources.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PreferencesScreenViewModelTests : IDisposable
{
    private readonly string _originalInstalledDirectory;
    private readonly string _originalExtractedDirectory;
    private readonly string _originalThumbnailDirectory;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _scanRoot;

    public PreferencesScreenViewModelTests()
    {
        _originalInstalledDirectory = SkinPaths.InstalledDirectory;
        _originalExtractedDirectory = SkinPaths.ExtractedDirectory;
        _originalThumbnailDirectory = CoverThumbnailPaths.ThumbnailDirectory;

        string root = Path.Combine(Path.GetTempPath(), $"paperbunkr_prefsvm_test_{Guid.NewGuid():N}");
        SkinPaths.InstalledDirectory = Path.Combine(root, "skins");
        SkinPaths.ExtractedDirectory = Path.Combine(root, "skins-extracted");
        CoverThumbnailPaths.ThumbnailDirectory = Path.Combine(root, "thumbs");

        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_prefsvm_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _scanRoot = Path.Combine(root, "scan");
        Directory.CreateDirectory(_scanRoot);
    }

    public void Dispose()
    {
        SkinPaths.InstalledDirectory = _originalInstalledDirectory;
        SkinPaths.ExtractedDirectory = _originalExtractedDirectory;
        CoverThumbnailPaths.ThumbnailDirectory = _originalThumbnailDirectory;

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

    private PreferencesScreenViewModel CreateViewModel(
        IFilePickerService? filePicker = null,
        IShellFileAssociation? shell = null,
        Action<string, string>? showToast = null,
        MigrationOverlayViewModel? migration = null,
        Action? openMigration = null,
        Action<ToastProgressViewModel>? showProgressToast = null,
        Action<ToastProgressViewModel>? closeProgressToast = null)
    {
        var skinService = new SkinService(() => new PaperbunkrDbContext(_dbOptions));
        var scanner = new LibraryFolderScanner(() => new PaperbunkrDbContext(_dbOptions));
        var fileAssociationService = new FileAssociationService(shell ?? new FakeShellFileAssociation());
        var backupService = new BackupService(() => new PaperbunkrDbContext(_dbOptions));
        var keyBindingService = new KeyBindingService(() => new PaperbunkrDbContext(_dbOptions));
        return new PreferencesScreenViewModel(
            skinService,
            filePicker ?? new NoOpFilePicker(),
            scanner,
            fileAssociationService,
            backupService,
            keyBindingService,
            showToast ?? ((_, _) => { }),
            migration ?? new MigrationOverlayViewModel(filePicker ?? new NoOpFilePicker(), _ => { }),
            openMigration ?? (() => { }),
            showProgressToast ?? (_ => { }),
            closeProgressToast ?? (_ => { }),
            () => new PaperbunkrDbContext(_dbOptions));
    }

    [Fact]
    public void GoAppearance_SetsActiveTabFlag()
    {
        var vm = CreateViewModel();

        vm.GoAppearanceCommand.Execute(null);

        Assert.True(vm.IsAppearanceTab);
    }

    [Fact]
    public void EnsureLoaded_PopulatesSkinsAndFontsOnce()
    {
        var vm = CreateViewModel();

        vm.EnsureLoaded();
        int skinCountAfterFirstLoad = vm.Skins.Count;
        vm.EnsureLoaded();

        Assert.Single(vm.Skins);
        Assert.Equal(skinCountAfterFirstLoad, vm.Skins.Count);
        Assert.Contains("System Default", vm.FontFamilies);
        Assert.Equal("System Default", vm.SelectedFontFamily);
    }

    [Fact]
    public void SelectSkin_AppliesSkin_AndRefreshesActiveFlag()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var defaultSkin = vm.Skins[0];

        vm.SelectSkinCommand.Execute(defaultSkin);

        Assert.True(vm.Skins[0].IsActive);
    }

    [Fact]
    public void SelectedFontFamily_Change_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.SelectedFontFamily = "Consolas";

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal("Consolas", context.GetOrCreateAppSettings().SelectedFontFamily);
    }

    [Fact]
    public void GoBehavior_SetsActiveTabFlag()
    {
        var vm = CreateViewModel();

        vm.GoBehaviorCommand.Execute(null);

        Assert.True(vm.IsBehaviorTab);
        Assert.False(vm.IsAppearanceTab);
    }

    [Fact]
    public void EnsureLoaded_PopulatesBehaviorFlagsFromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.OpenLastPage = false;
            settings.AutoNavigateComics = false;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.OpenLastPage);
        Assert.False(vm.AutoNavigateComics);
    }

    [Fact]
    public void TogglingBehaviorFlags_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.OpenLastPage = false;
        vm.AutoNavigateComics = false;

        using var context = new PaperbunkrDbContext(_dbOptions);
        var settings = context.GetOrCreateAppSettings();
        Assert.False(settings.OpenLastPage);
        Assert.False(settings.AutoNavigateComics);
    }

    [Fact]
    public void GoReader_SetsActiveTabFlag()
    {
        var vm = CreateViewModel();

        vm.GoReaderCommand.Execute(null);

        Assert.True(vm.IsReaderTab);
        Assert.False(vm.IsAppearanceTab);
    }

    [Fact]
    public void EnsureLoaded_PopulatesReverseRtlNavigationFromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().ReverseRtlNavigation = false;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.ReverseRtlNavigation);
    }

    [Fact]
    public void TogglingReverseRtlNavigation_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.ReverseRtlNavigation = false;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.False(context.GetOrCreateAppSettings().ReverseRtlNavigation);
    }

    [Fact]
    public void EnsureLoaded_PopulatesHighQualityPageDisplayFromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().HighQualityPageDisplay = false;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.HighQualityPageDisplay);
    }

    [Fact]
    public void TogglingHighQualityPageDisplay_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.HighQualityPageDisplay = false;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.False(context.GetOrCreateAppSettings().HighQualityPageDisplay);
    }

    [Fact]
    public void EnsureLoaded_PopulatesKeyBindings_OneRowPerRegisteredCommand()
    {
        var vm = CreateViewModel();

        vm.EnsureLoaded();

        Assert.Equal(KeyboardCommandRegistry.Commands.Count, vm.KeyBindings.Count);
        Assert.Contains(vm.KeyBindings, r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft && r.SelectedKey.Key == Key.Left);
        Assert.Contains(vm.KeyBindings, r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnRight && r.SelectedKey.Key == Key.Right);
    }

    [Fact]
    public void ChangingKeyBindingRow_PersistsThroughKeyBindingService()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = vm.KeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);

        row.SelectedKey = row.Options.Single(o => o.Key == Key.J);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal("J", context.KeyBindings.Single(k => k.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft).Key);
    }

    [Fact]
    public void AssigningSameKeyToBothReaderBindings_SetsConflictError()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var left = vm.KeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
        var right = vm.KeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnRight);

        left.SelectedKey = left.Options.Single(o => o.Key == Key.J);
        right.SelectedKey = right.Options.Single(o => o.Key == Key.J);

        Assert.True(vm.HasKeyBindingConflictError);

        // Resolving it clears the error again.
        right.SelectedKey = right.Options.Single(o => o.Key == Key.K);
        Assert.False(vm.HasKeyBindingConflictError);
    }

    [Fact]
    public void BrowseForSkin_UserCancels_LeavesErrorUntouched()
    {
        var vm = CreateViewModel(new NoOpFilePicker());
        vm.EnsureLoaded();

        vm.BrowseForSkinCommand.Execute(null);

        Assert.False(vm.HasInstallSkinError);
    }

    [Fact]
    public void GoLibraries_SetsActiveTabFlag()
    {
        var vm = CreateViewModel();

        vm.GoLibrariesCommand.Execute(null);

        Assert.True(vm.IsLibrariesTab);
    }

    [Fact]
    public void AddVirtualTag_CreatesAndSelectsNewTag()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.AddVirtualTagCommand.Execute(null);

        Assert.Single(vm.VirtualTags);
        Assert.True(vm.HasSelectedVirtualTag);
        Assert.Equal("New Tag", vm.VirtualTagName);
    }

    [Fact]
    public void EditingSelectedVirtualTag_PersistsAndUpdatesPreview()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        vm.AddVirtualTagCommand.Execute(null);

        vm.VirtualTagName = "Series + Number";
        vm.VirtualTagCaptionFormat = "{Series} #{Number}";

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var tag = Assert.Single(context.VirtualTagDefinitions);
            Assert.Equal("Series + Number", tag.Name);
            Assert.Equal("{Series} #{Number}", tag.CaptionFormat);
        }

        Assert.Equal("Sample Series #1", vm.VirtualTagPreview);
    }

    [Fact]
    public void DeleteVirtualTag_RemovesItAndClearsSelection()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        vm.AddVirtualTagCommand.Execute(null);

        vm.DeleteVirtualTagCommand.Execute(null);

        Assert.Empty(vm.VirtualTags);
        Assert.False(vm.HasSelectedVirtualTag);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.VirtualTagDefinitions);
    }

    [Fact]
    public async Task AddFolder_UserPicksFolder_PersistsAndRefreshesList()
    {
        var picker = new StubFilePicker { FolderToReturn = @"C:\Comics" };
        var vm = CreateViewModel(picker);
        vm.EnsureLoaded();

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Single(vm.WatchedFolders);
        Assert.Equal(@"C:\Comics", vm.WatchedFolders[0].Path);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.WatchedFolders);
    }

    [Fact]
    public async Task AddFolder_UserCancels_DoesNotAdd()
    {
        var vm = CreateViewModel(new StubFilePicker { FolderToReturn = null });
        vm.EnsureLoaded();

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Empty(vm.WatchedFolders);
    }

    [Fact]
    public void RemoveFolder_DeletesItFromListAndDatabase()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.WatchedFolders.Add(new Paperbunkr.Data.Entities.WatchedFolder { Path = @"C:\Comics" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var folder = vm.WatchedFolders[0];

        vm.RemoveFolderCommand.Execute(folder);

        Assert.Empty(vm.WatchedFolders);
        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(verify.WatchedFolders);
    }

    [Fact]
    public async Task ScanNow_ReportsResultInScanStatus()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        await vm.ScanNowCommand.ExecuteAsync(null);

        Assert.Equal("No new issues found.", vm.ScanStatus);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task ScanNow_FiresToast_OnCompletion()
    {
        (string Title, string Message)? toast = null;
        var vm = CreateViewModel(showToast: (title, message) => toast = (title, message));
        vm.EnsureLoaded();

        await vm.ScanNowCommand.ExecuteAsync(null);

        Assert.NotNull(toast);
        Assert.Equal("Scan complete", toast!.Value.Title);
        Assert.Equal("No new issues found.", toast!.Value.Message);
    }

    [Fact]
    public async Task GenerateCoversCommand_ShowsThenClosesProgressToast_ThenFiresCompletionToast()
    {
        ToastProgressViewModel? shown = null;
        ToastProgressViewModel? closed = null;
        (string Title, string Message)? completionToast = null;
        var vm = CreateViewModel(
            showProgressToast: t => shown = t,
            closeProgressToast: t => closed = t,
            showToast: (title, message) => completionToast = (title, message));

        await vm.GenerateCoversCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Same(shown, closed); // same toast instance shown and closed, not a new one each time
        Assert.False(vm.IsGeneratingCovers);
        Assert.NotNull(completionToast);
        Assert.Equal("Covers generated", completionToast!.Value.Title);
    }

    [Fact]
    public async Task SyncMetadataCommand_ShowsThenClosesProgressToast_ThenFiresCompletionToast()
    {
        ToastProgressViewModel? shown = null;
        ToastProgressViewModel? closed = null;
        (string Title, string Message)? completionToast = null;
        var vm = CreateViewModel(
            showProgressToast: t => shown = t,
            closeProgressToast: t => closed = t,
            showToast: (title, message) => completionToast = (title, message));

        await vm.SyncMetadataCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Same(shown, closed);
        Assert.False(vm.IsSyncingMetadata);
        Assert.NotNull(completionToast);
        Assert.Equal("Metadata sync complete", completionToast!.Value.Title);
        Assert.Equal("No new metadata found.", completionToast!.Value.Message);
    }

    [Fact]
    public async Task ScanNow_GeneratesCoverThumbnail_ForNewlyScannedIssue()
    {
        string cbzPath = Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz");
        CbzFixture.Create(cbzPath, pageCount: 1);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.WatchedFolders.Add(new Paperbunkr.Data.Entities.WatchedFolder { Path = _scanRoot });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        await vm.ScanNowCommand.ExecuteAsync(null);

        Assert.Equal("Added 1 issue across 1 series.", vm.ScanStatus);
        using var verifyContext = new PaperbunkrDbContext(_dbOptions);
        var issue = Assert.Single(verifyContext.Issues);
        Assert.True(File.Exists(CoverThumbnailPaths.GetCachePath(issue.Id)));
    }

    [Fact]
    public void GoAdvanced_SetsActiveTabFlag()
    {
        var vm = CreateViewModel();

        vm.GoAdvancedCommand.Execute(null);

        Assert.True(vm.IsAdvancedTab);
    }

    [Fact]
    public void OpenMigrationCommand_InvokesInjectedCallback()
    {
        bool called = false;
        var vm = CreateViewModel(openMigration: () => called = true);

        vm.OpenMigrationCommand.Execute(null);

        Assert.True(called);
    }

    [Fact]
    public void EnsureLoaded_PopulatesFileAssociations_NoneAssociatedInitially()
    {
        var vm = CreateViewModel();

        vm.EnsureLoaded();

        Assert.NotEmpty(vm.FileAssociations);
        Assert.All(vm.FileAssociations, f => Assert.False(f.IsAssociated));
    }

    [Fact]
    public void ToggleFileAssociation_RegistersThroughToShell_AndRefreshesList()
    {
        var shell = new FakeShellFileAssociation();
        var vm = CreateViewModel(shell: shell);
        vm.EnsureLoaded();
        var format = vm.FileAssociations[0];

        vm.ToggleFileAssociationCommand.Execute(format);

        Assert.True(vm.FileAssociations[0].IsAssociated);
        Assert.True(shell.RefreshCalled);
    }

    [Fact]
    public void ChangingBackupsToKeep_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.BackupsToKeep = 10;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(10, context.GetOrCreateAppSettings().BackupsToKeep);
    }

    [Fact]
    public void BackupNow_CreatesBackupRow_AndSetsStatus()
    {
        string? originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        string backupRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_prefsvm_backups_{Guid.NewGuid():N}");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        try
        {
            var vm = CreateViewModel();
            vm.EnsureLoaded();
            vm.BackupLocation = backupRoot;

            vm.BackupNowCommand.Execute(null);

            Assert.Equal("Backup created.", vm.BackupStatus);
            Assert.Single(vm.Backups);
        }
        finally
        {
            PaperbunkrDbContext.DatabasePathOverride = originalOverride;
            try { if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void BackupRow_Restore_RequiresTwoClicks()
    {
        string? originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        string backupRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_prefsvm_backups_{Guid.NewGuid():N}");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        try
        {
            var vm = CreateViewModel();
            vm.EnsureLoaded();
            vm.BackupLocation = backupRoot;
            vm.BackupNowCommand.Execute(null);
            var row = vm.Backups[0];

            row.RestoreCommand.Execute(null);
            Assert.Equal("Confirm restore?", row.RestoreLabel);
            Assert.Equal("Backup created.", vm.BackupStatus);

            row.RestoreCommand.Execute(null);
            Assert.Equal("Restored — restart Paperbunkr for the change to take effect.", vm.BackupStatus);
        }
        finally
        {
            PaperbunkrDbContext.DatabasePathOverride = originalOverride;
            try { if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class FakeShellFileAssociation : IShellFileAssociation
    {
        private readonly HashSet<(string TypeId, string Extension)> _registered = new();

        public bool RefreshCalled { get; private set; }

        public bool IsRegistered(string typeId, string extension) => _registered.Contains((typeId, extension));

        public void Register(string typeId, string extension, string displayName, string appPath) => _registered.Add((typeId, extension));

        public void Unregister(string typeId, string extension) => _registered.Remove((typeId, extension));

        public void RefreshShell() => RefreshCalled = true;
    }

    private sealed class NoOpFilePicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
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
