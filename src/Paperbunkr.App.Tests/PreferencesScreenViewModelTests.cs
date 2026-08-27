using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

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
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _scanRoot;
    private readonly string _graphicsCachePath;

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

        // Real pre-existing isolation gap, surfaced (not caused) by an unrelated schema change:
        // MigrationOverlayViewModel/NeedsReviewViewModel (constructed in CreateViewModel below)
        // have no injected context-factory seam, unlike every other service here - without this,
        // they silently fall through to the real default database path instead of this test's
        // isolated one. Matches the class-wide DatabasePathOverride pattern other test classes
        // (e.g. ReaderScreenViewModelTests) already use for exactly this reason.
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        // The Advanced tab's rendering-backend rows write a graphics.json cache
        // (docs/superpowers/specs/2026-08-27-hardware-accelerated-rendering-design.md) - keep that
        // off the real %AppData%, same static-override isolation approach as everything above.
        _graphicsCachePath = Path.Combine(root, "graphics.json");
        GraphicsBootstrap.CachePathOverride = _graphicsCachePath;

        _scanRoot = Path.Combine(root, "scan");
        Directory.CreateDirectory(_scanRoot);
    }

    public void Dispose()
    {
        SkinPaths.InstalledDirectory = _originalInstalledDirectory;
        SkinPaths.ExtractedDirectory = _originalExtractedDirectory;
        CoverThumbnailPaths.ThumbnailDirectory = _originalThumbnailDirectory;
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        GraphicsBootstrap.CachePathOverride = null;

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
        Action<ToastProgressViewModel>? closeProgressToast = null,
        Action? reloadFolderWatch = null)
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
            new PluginScreenViewModel(),
            openMigration ?? (() => { }),
            showProgressToast ?? (_ => { }),
            closeProgressToast ?? (_ => { }),
            reloadFolderWatch ?? (() => { }),
            () => { },
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

    /// <summary>docs/superpowers/specs/2026-08-24-design-language-foundation-design.md - same load/apply/persist shape as <see cref="SelectedFontFamily_Change_PersistsToAppSettings"/>.</summary>
    [Fact]
    public void ReducedMotion_Change_PersistsToAppSettings_AndAppliesLive()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        Assert.False(vm.ReducedMotion);

        vm.ReducedMotion = true;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.True(context.GetOrCreateAppSettings().ReducedMotion);
        Assert.Equal(TimeSpan.Zero, Avalonia.Application.Current!.Resources["PbMotionFast"]);
        Assert.Equal(TimeSpan.Zero, Avalonia.Application.Current!.Resources["PbMotionSlow"]);
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

    // App chrome (docs/superpowers/specs/2026-08-23-app-chrome-crash-reporter-and-tray-design.md §4)
    [Fact]
    public void EnsureLoaded_PopulatesMinimizeToTrayFromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().MinimizeToTray = true;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.True(vm.MinimizeToTray);
    }

    [Fact]
    public void TogglingMinimizeToTray_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.MinimizeToTray = true;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.True(context.GetOrCreateAppSettings().MinimizeToTray);
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

    // Rendering group on the Advanced tab
    // (docs/superpowers/specs/2026-08-27-hardware-accelerated-rendering-design.md §10)
    [Fact]
    public void EnsureLoaded_PopulatesRenderingBackendFromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var s = context.GetOrCreateAppSettings();
            s.RenderingBackend = RenderBackend.Software;
            s.PreferNativeOpenGl = true;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.Equal(RenderBackend.Software, vm.RenderingBackend);
        Assert.True(vm.PreferNativeOpenGl);
    }

    [Fact]
    public void ChangingRenderingBackend_PersistsToAppSettingsAndGraphicsCache()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.RenderingBackend = RenderBackend.Gpu;
        vm.PreferNativeOpenGl = true;

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var s = context.GetOrCreateAppSettings();
            Assert.Equal(RenderBackend.Gpu, s.RenderingBackend);
            Assert.True(s.PreferNativeOpenGl);
        }

        var (config, source) = GraphicsBootstrap.Resolve();
        Assert.Equal(new GraphicsConfig(RenderBackend.Gpu, true), config);
        Assert.Equal("graphics.json", source);
    }

    // Reader tab additions (docs/superpowers/specs/2026-08-10-preferences-reader-tab-design.md)
    [Fact]
    public void EnsureLoaded_PopulatesReaderTabAdditionsFromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.ResetZoomOnPageChange = true;
            settings.MouseWheelSpeed = 3.5;
            settings.DefaultPageFitMode = ImageFitMode.BestFit;
            settings.DefaultAutoRotate = true;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.True(vm.ResetZoomOnPageChange);
        Assert.Equal(3.5, vm.MouseWheelSpeed);
        Assert.Equal(ImageFitMode.BestFit, vm.DefaultPageFitMode);
        Assert.True(vm.DefaultAutoRotate);
    }

    // Page transition animations (docs/superpowers/specs/2026-08-13-reader-page-transition-animations-design.md)
    [Fact]
    public void EnsureLoaded_PopulatesPageTransitionSettingsFromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.PageTransitionStyle = PageTransitionStyle.Slide;
            settings.PageTransitionDurationMs = 400;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.Equal(PageTransitionStyle.Slide, vm.PageTransitionStyle);
        Assert.Equal(400, vm.PageTransitionDurationMs);
    }

    [Fact]
    public void ChangingPageTransitionStyle_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.PageTransitionStyle = PageTransitionStyle.Crossfade;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(PageTransitionStyle.Crossfade, context.GetOrCreateAppSettings().PageTransitionStyle);
    }

    [Fact]
    public void ChangingPageTransitionDurationMs_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.PageTransitionDurationMs = 500;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(500, context.GetOrCreateAppSettings().PageTransitionDurationMs);
    }

    // Double-page spread (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md)
    [Fact]
    public void EnsureLoaded_PopulatesDefaultPageLayoutModeFromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.DefaultPageLayoutMode = PageLayoutMode.Double;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.Equal(PageLayoutMode.Double, vm.DefaultPageLayoutMode);
    }

    [Fact]
    public void ChangingDefaultPageLayoutMode_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.DefaultPageLayoutMode = PageLayoutMode.Double;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(PageLayoutMode.Double, context.GetOrCreateAppSettings().DefaultPageLayoutMode);
    }

    [Fact]
    public void TogglingResetZoomOnPageChange_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.ResetZoomOnPageChange = true;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.True(context.GetOrCreateAppSettings().ResetZoomOnPageChange);
    }

    [Fact]
    public void ChangingMouseWheelSpeed_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.MouseWheelSpeed = 4.0;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(4.0, context.GetOrCreateAppSettings().MouseWheelSpeed);
    }

    [Fact]
    public void ChangingDefaultPageFitMode_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.DefaultPageFitMode = ImageFitMode.Original;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(ImageFitMode.Original, context.GetOrCreateAppSettings().DefaultPageFitMode);
    }

    [Fact]
    public void TogglingDefaultAutoRotate_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.DefaultAutoRotate = true;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.True(context.GetOrCreateAppSettings().DefaultAutoRotate);
    }

    [Fact]
    public void EnsureLoaded_PopulatesKeyBindings_OneRowPerRegisteredCommand()
    {
        var vm = CreateViewModel();

        vm.EnsureLoaded();

        int total = vm.NavigationKeyBindings.Count + vm.ZoomFitKeyBindings.Count + vm.DisplayKeyBindings.Count;
        Assert.Equal(KeyboardCommandRegistry.Commands.Count, total);
        Assert.Contains(vm.NavigationKeyBindings, r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft && r.SelectedKey.Gesture == new KeyGesture(Key.Left));
        Assert.Contains(vm.NavigationKeyBindings, r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnRight && r.SelectedKey.Gesture == new KeyGesture(Key.Right));
    }

    /// <summary>docs/superpowers/specs/2026-08-25-reader-chrome-design.md - a genuine new gap closed, not a restyle: import/export never existed before this (confirmed via grep, zero hits anywhere in src/).</summary>
    [Fact]
    public async Task ExportThenImportKeyBindings_RoundTripsARemappedBinding()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_keybindings_test_{Guid.NewGuid():N}.json");
        try
        {
            var vm = CreateViewModel(new FileRoundTripPicker { SavePathToReturn = path, OpenPathToReturn = path });
            vm.EnsureLoaded();
            var row = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
            row.SelectedKey = row.Options.Single(o => o.Gesture == new KeyGesture(Key.J));

            await vm.ExportKeyBindingsCommand.ExecuteAsync(null);

            // A second, independently-loaded VM (same underlying database) picks up the exported
            // file and re-applies it - proves the file round-trips the real gesture, not just that
            // the in-memory VM still remembers its own change.
            using (var context = new PaperbunkrDbContext(_dbOptions))
            {
                context.KeyBindings.Single(k => k.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft).Key = "K";
                context.SaveChanges();
            }

            await vm.ImportKeyBindingsCommand.ExecuteAsync(null);

            var reloaded = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
            Assert.Equal(new KeyGesture(Key.J), reloaded.SelectedKey.Gesture);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Per-entry tolerance, not all-or-nothing (docs/superpowers/specs/2026-08-25-reader-chrome-design.md) - mirrors KeyBindingService.GetKey's own catch (ArgumentException) fallback philosophy, applied at import time.</summary>
    [Fact]
    public async Task ImportKeyBindings_WithOneCorruptEntry_StillAppliesTheValidOnes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_keybindings_corrupt_test_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, $$"""
                [
                    {"CommandId": "{{KeyboardCommandRegistry.ReaderPageTurnLeft}}", "Gesture": "J"},
                    {"CommandId": "{{KeyboardCommandRegistry.ReaderPageTurnRight}}", "Gesture": "not a real gesture"}
                ]
                """);
            var vm = CreateViewModel(new FileRoundTripPicker { OpenPathToReturn = path });
            vm.EnsureLoaded();

            await vm.ImportKeyBindingsCommand.ExecuteAsync(null);

            var left = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
            Assert.Equal(new KeyGesture(Key.J), left.SelectedKey.Gesture);
            // The corrupt entry didn't throw and didn't block the valid one - right still holds its default.
            var right = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnRight);
            Assert.Equal(new KeyGesture(Key.Right), right.SelectedKey.Gesture);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ChangingKeyBindingRow_PersistsThroughKeyBindingService()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);

        row.SelectedKey = row.Options.Single(o => o.Gesture == new KeyGesture(Key.J));

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal("J", context.KeyBindings.Single(k => k.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft).Key);
    }

    [Fact]
    public void AssigningSameKeyToBothReaderBindings_SetsConflictError()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var left = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
        var right = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnRight);

        left.SelectedKey = left.Options.Single(o => o.Gesture == new KeyGesture(Key.J));
        right.SelectedKey = right.Options.Single(o => o.Gesture == new KeyGesture(Key.J));

        Assert.True(vm.HasKeyBindingConflictError);

        // Resolving it clears the error again.
        right.SelectedKey = right.Options.Single(o => o.Gesture == new KeyGesture(Key.K));
        Assert.False(vm.HasKeyBindingConflictError);
    }

    [Fact]
    public void FreshLoad_NoConflictError_DespitePagedUnzoomedPagedZoomedAndContinuousSharingDefaultLeft()
    {
        // PageTurnLeft/PanLeft/ScrollLeft all default to Left across three different, mutually
        // exclusive-at-runtime contexts (PagedUnzoomed/PagedZoomed/Continuous) - must not flag.
        var vm = CreateViewModel();

        vm.EnsureLoaded();

        Assert.False(vm.HasKeyBindingConflictError);
    }

    [Fact]
    public void SameContextCollision_SetsConflictError()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var panRight = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPanRight);

        // PanLeft (also PagedZoomed) still sits at its default Left.
        panRight.SelectedKey = panRight.Options.Single(o => o.Gesture == new KeyGesture(Key.Left));

        Assert.True(vm.HasKeyBindingConflictError);
    }

    [Fact]
    public void AlwaysContextCollidingWithModeSpecificCommand_SetsConflictError()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var zoomIn = vm.ZoomFitKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderZoomIn);

        // ZoomIn is Always-context - colliding with PageTurnLeft/PanLeft/ScrollLeft (all still at
        // their default Left) is a real conflict even though those three don't conflict with
        // each other.
        zoomIn.SelectedKey = zoomIn.Options.Single(o => o.Gesture == new KeyGesture(Key.Left));

        Assert.True(vm.HasKeyBindingConflictError);
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

    /// <summary>docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md - Plugins moved from a standalone rail screen into this tab.</summary>
    [Fact]
    public void GoPlugins_SetsActiveTabFlag()
    {
        var vm = CreateViewModel();

        vm.GoPluginsCommand.Execute(null);

        Assert.True(vm.IsPluginsTab);
        Assert.False(vm.IsAdvancedTab);
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

    /// <summary>Returns a configurable file path for both open/save dialogs - used by the keyboard-shortcut import/export round-trip tests, neither existing fake above supports this.</summary>
    private sealed class FileRoundTripPicker : IFilePickerService
    {
        public string? OpenPathToReturn { get; set; }
        public string? SavePathToReturn { get; set; }

        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult(OpenPathToReturn);

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult(SavePathToReturn);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }
}
