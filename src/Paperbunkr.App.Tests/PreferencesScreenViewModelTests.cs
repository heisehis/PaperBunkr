using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Scheduling;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Credentials;
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
    private readonly string _originalBookThumbnailDirectory;
    private readonly string _originalCustomCoverDirectory;
    private readonly string _originalCustomBookCoverDirectory;
    private readonly string _originalCoverCacheStateFile;
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
        _originalBookThumbnailDirectory = BookCoverThumbnailPaths.ThumbnailDirectory;
        _originalCustomCoverDirectory = Paperbunkr.App.Services.Covers.CustomCoverPaths.Directory;
        _originalCustomBookCoverDirectory = Paperbunkr.App.Services.Covers.CustomBookCoverPaths.Directory;
        _originalCoverCacheStateFile = Paperbunkr.App.Services.Covers.CoverCacheState.FilePath;

        string root = Path.Combine(Path.GetTempPath(), $"paperbunkr_prefsvm_test_{Guid.NewGuid():N}");
        SkinPaths.InstalledDirectory = Path.Combine(root, "skins");
        SkinPaths.ExtractedDirectory = Path.Combine(root, "skins-extracted");
        CoverThumbnailPaths.ThumbnailDirectory = Path.Combine(root, "thumbs");
        BookCoverThumbnailPaths.ThumbnailDirectory = Path.Combine(root, "book-thumbs");
        Paperbunkr.App.Services.Covers.CustomCoverPaths.Directory = Path.Combine(root, "custom-covers");
        Paperbunkr.App.Services.Covers.CustomBookCoverPaths.Directory = Path.Combine(root, "custom-book-covers");
        Paperbunkr.App.Services.Covers.CoverCacheState.FilePath = Path.Combine(root, "cover-cache-state.json");

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
        BookCoverThumbnailPaths.ThumbnailDirectory = _originalBookThumbnailDirectory;
        Paperbunkr.App.Services.Covers.CustomCoverPaths.Directory = _originalCustomCoverDirectory;
        Paperbunkr.App.Services.Covers.CustomBookCoverPaths.Directory = _originalCustomBookCoverDirectory;
        Paperbunkr.App.Services.Covers.CoverCacheState.FilePath = _originalCoverCacheStateFile;
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
        IActivityService? activity = null,
        IDialogService? dialogService = null,
        Action? reloadFolderWatch = null,
        Action<int, bool>? enqueueMetadataWriteBack = null)
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
            new PluginScreenViewModel(filePicker ?? new NoOpFilePicker(), new FakeDialogService()),
            openMigration ?? (() => { }),
            activity ?? new ActivityService(a => a(), _ => { }),
            dialogService ?? new FakeDialogService(),
            reloadFolderWatch ?? (() => { }),
            () => { },
            new UpdateService(),
            () => new PaperbunkrDbContext(_dbOptions),
            enqueueMetadataWriteBack ?? ((_, _) => { }));
    }

    // ===================== Sidebar hard-switch (reverted back from the single-scroll shell -
    // docs/superpowers/specs/2026-09-07-preferences-tile-hub-redesign-design.md's shell was tried
    // and reverted the same session, too annoying to navigate once actually built) =====================

    [Fact]
    public void GoAppearance_SetsActiveSectionFlag()
    {
        var vm = CreateViewModel();

        vm.GoAppearanceCommand.Execute(null);

        Assert.True(vm.IsAppearanceSection);
    }

    [Fact]
    public void GoGeneral_SetsActiveSectionFlag()
    {
        var vm = CreateViewModel();
        vm.GoAdvancedCommand.Execute(null);

        vm.GoGeneralCommand.Execute(null);

        Assert.True(vm.IsGeneralSection);
        Assert.False(vm.IsAdvancedSection);
    }

    [Fact]
    public void GoKeyboardShortcuts_SetsActiveSectionFlag()
    {
        var vm = CreateViewModel();

        vm.GoKeyboardShortcutsCommand.Execute(null);

        Assert.True(vm.IsKeyboardShortcutsSection);
        Assert.False(vm.IsGeneralSection);
    }

    [Fact]
    public void GoConnections_SetsActiveSectionFlag()
    {
        var vm = CreateViewModel();

        vm.GoConnectionsCommand.Execute(null);

        Assert.True(vm.IsConnectionsSection);
        Assert.False(vm.IsGeneralSection);
    }

    [Fact]
    public void RequestScrollToAnchor_RaisesScrollToAnchorRequested()
    {
        var vm = CreateViewModel();
        string? anchor = null;
        vm.ScrollToAnchorRequested += a => anchor = a;

        vm.RequestScrollToAnchor("library.comicFolders");

        Assert.Equal("library.comicFolders", anchor);
    }

    // ===================== Connections list+dialog (docs/superpowers/specs/2026-09-06-connections-
    // tracker-dialog-redesign-design.md) =====================

    [Fact]
    public void EnsureLoaded_PopulatesSourceAndTrackerProviderRows()
    {
        var vm = CreateViewModel();

        vm.EnsureLoaded();

        Assert.Equal(2, vm.SourceProviderRows.Count);
        Assert.Equal(7, vm.TrackerProviderRows.Count);
        Assert.Contains(vm.SourceProviderRows, r => r.Id == "ComicVine");
        Assert.Contains(vm.SourceProviderRows, r => r.Id == "Metron");
        Assert.Contains(vm.TrackerProviderRows, r => r.Id == nameof(TrackingService.AniList));
    }

    [Fact]
    public void OpenConnectionDialogCommand_SetsSelectedProviderAndOpensDialog()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = vm.TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.AniList));

        vm.OpenConnectionDialogCommand.Execute(row);

        Assert.Same(row, vm.SelectedConnectionProvider);
        Assert.True(vm.IsConnectionDialogOpen);
    }

    [Fact]
    public void OpenConnectionDialogCommand_SwitchingProviders_ClearsStalePasteBackFields()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var aniList = vm.TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.AniList));
        var myAnimeList = vm.TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.MyAnimeList));

        vm.OpenConnectionDialogCommand.Execute(aniList);
        aniList.PastedValue = "stale-token";

        vm.OpenConnectionDialogCommand.Execute(myAnimeList);

        Assert.Equal(string.Empty, aniList.PastedValue);
        Assert.Same(myAnimeList, vm.SelectedConnectionProvider);
    }

    [Fact]
    public void OpenConnectionDialogCommand_SwitchingProviders_ClearsStaleConnectionDialogStatus()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var comicVine = vm.SourceProviderRows.Single(r => r.Id == "ComicVine");
        var metron = vm.SourceProviderRows.Single(r => r.Id == "Metron");

        vm.OpenConnectionDialogCommand.Execute(comicVine);
        vm.ConnectionDialogStatus = "ComicVine API key saved.";

        vm.OpenConnectionDialogCommand.Execute(metron);

        Assert.Equal(string.Empty, vm.ConnectionDialogStatus);
    }

    [Theory]
    [InlineData(nameof(TrackingService.AniList))]
    [InlineData(nameof(TrackingService.Bangumi))]
    public void ConnectionProviderRow_CommandReferencesWired_MatchExistingViewModelCommands(string trackerId)
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = vm.TrackerProviderRows.Single(r => r.Id == trackerId);

        Assert.NotNull(row.DisconnectCommand);

        if (trackerId == nameof(TrackingService.AniList))
        {
            Assert.Same(vm.ConnectAniListCommand, row.ConnectCommand);
            Assert.Same(vm.CompleteAniListConnectCommand, row.CompleteCommand);
            Assert.Same(vm.DisconnectAniListCommand, row.DisconnectCommand);
        }
        else
        {
            Assert.Same(vm.SaveBangumiTokenCommand, row.SaveCommand);
            Assert.Same(vm.DisconnectBangumiCommand, row.DisconnectCommand);
        }
    }

    [Fact]
    public void ConnectionProviderRow_CommandReferencesWired_MetronCredentialShape()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = vm.SourceProviderRows.Single(r => r.Id == "Metron");

        Assert.Same(vm.SaveMetronCredentialsCommand, row.PrimaryCommand);
        Assert.Same(vm.DisconnectMetronCommand, row.DisconnectCommand);
    }

    [Fact]
    public void CloseConnectionDialogCommand_ClearsSelectionAndCloses()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        vm.OpenConnectionDialogCommand.Execute(vm.TrackerProviderRows[0]);

        vm.CloseConnectionDialogCommand.Execute(null);

        Assert.Null(vm.SelectedConnectionProvider);
        Assert.False(vm.IsConnectionDialogOpen);
    }

    [Fact]
    public void DisconnectAniList_ClearsCredentialAndFlipsConnectedFlag()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            CredentialStore.Set(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken, "token");
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();
        Assert.True(vm.IsAniListConnected);
        Assert.True(vm.TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.AniList)).IsConnected);

        vm.DisconnectAniListCommand.Execute(null);

        Assert.False(vm.IsAniListConnected);
        Assert.False(vm.TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.AniList)).IsConnected);
        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Null(CredentialStore.Get(verify, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken));
    }

    [Fact]
    public void DisconnectBangumi_ClearsCredentialAndFlipsConnectedFlag()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            CredentialStore.Set(context, nameof(TrackingService.Bangumi), CredentialKind.ApiKey, "pat");
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = vm.TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.Bangumi));
        row.SecretValue = "pat";

        vm.DisconnectBangumiCommand.Execute(null);

        Assert.False(vm.IsBangumiConnected);
        Assert.Equal(string.Empty, row.SecretValue);
        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Null(CredentialStore.Get(verify, nameof(TrackingService.Bangumi), CredentialKind.ApiKey));
    }

    [Fact]
    public void DisconnectKitsu_ClearsSessionTokenAndUiFields()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            CredentialStore.Set(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken, "access");
            CredentialStore.Set(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthRefreshToken, "refresh");
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = vm.TrackerProviderRows.Single(r => r.Id == nameof(TrackingService.Kitsu));
        row.Username = "someone";
        row.Password = "hunter2";

        vm.DisconnectKitsuCommand.Execute(null);

        Assert.False(vm.IsKitsuConnected);
        Assert.Equal(string.Empty, row.Username);
        Assert.Equal(string.Empty, row.Password);
        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Null(CredentialStore.Get(verify, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken));
        Assert.Null(CredentialStore.Get(verify, nameof(TrackingService.Kitsu), CredentialKind.OAuthRefreshToken));
    }

    [Fact]
    public void RefreshSourceCredentials_ComicVineAndMetron_ReportConnectedState()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            CredentialStore.Set(context, "ComicVine", CredentialKind.ApiKey, "key");
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.True(vm.IsComicVineConnected);
        Assert.False(vm.IsMetronConnected);
        Assert.True(vm.SourceProviderRows.Single(r => r.Id == "ComicVine").IsConnected);
        Assert.False(vm.SourceProviderRows.Single(r => r.Id == "Metron").IsConnected);
    }

    [Fact]
    public void DisconnectMetron_ClearsCredentialsAndFlipsNewConnectedFlag()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            CredentialStore.Set(context, "Metron", CredentialKind.Username, "someone");
            CredentialStore.Set(context, "Metron", CredentialKind.Password, "hunter2");
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();
        Assert.True(vm.IsMetronConnected);

        vm.DisconnectMetronCommand.Execute(null);

        Assert.False(vm.IsMetronConnected);
        var metronRow = vm.SourceProviderRows.Single(r => r.Id == "Metron");
        Assert.Equal(string.Empty, metronRow.Username);
        Assert.Equal(string.Empty, metronRow.Password);
        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Null(CredentialStore.Get(verify, "Metron", CredentialKind.Username));
    }

    [Fact]
    public void SearchQuery_FiltersResults_AndOpenResultNavigatesAndPulses()
    {
        var vm = CreateViewModel();
        string? pulsedAnchor = null;
        vm.ScrollToAnchorRequested += a => pulsedAnchor = a;

        vm.SearchQuery = "double page";

        Assert.True(vm.IsSearching);
        Assert.Contains(vm.SearchResults, r => r.AnchorKey == "reader.display");

        var result = vm.SearchResults.First(r => r.AnchorKey == "reader.display");
        vm.OpenSearchResultCommand.Execute(result);

        Assert.True(vm.IsReaderSection);
        Assert.Equal(string.Empty, vm.SearchQuery);
        Assert.False(vm.IsSearching);
        Assert.Equal("reader.display", pulsedAnchor);
    }

    [Fact]
    public void SearchQuery_Empty_ClearsResults()
    {
        var vm = CreateViewModel();
        vm.SearchQuery = "anilist";
        Assert.NotEmpty(vm.SearchResults);

        vm.SearchQuery = "   ";

        Assert.Empty(vm.SearchResults);
        Assert.False(vm.IsSearching);
        Assert.False(vm.NoSearchResults);
    }

    [Fact]
    public void SearchQuery_NoMatch_SetsNoSearchResults()
    {
        var vm = CreateViewModel();

        vm.SearchQuery = "zzzznotasetting";

        Assert.Empty(vm.SearchResults);
        Assert.True(vm.NoSearchResults);
    }

    [Fact]
    public void EnsureLoaded_PopulatesSkinsAndFontsOnce()
    {
        var vm = CreateViewModel();

        vm.EnsureLoaded();
        int skinCountAfterFirstLoad = vm.Skins.Count;
        vm.EnsureLoaded();

        // 5 built-ins now (docs/superpowers/specs/2026-09-07-preferences-tile-hub-redesign-
        // design.md §3 - Default + Windows 11 + 3 new), not just Default - the "Once" in this
        // test's name is about EnsureLoaded's own idempotency guard, asserted by the equality
        // check below, not about the skin count itself.
        Assert.Equal(5, vm.Skins.Count);
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
        // docs/superpowers/specs/2026-09-04-navigation-transition-system-design.md - PbMotionStandard/
        // PbMotionLarge get the same zero-on-reduced-motion treatment as Fast/Slow above.
        Assert.Equal(TimeSpan.Zero, Avalonia.Application.Current!.Resources["PbMotionStandard"]);
        Assert.Equal(TimeSpan.Zero, Avalonia.Application.Current!.Resources["PbMotionLarge"]);
    }

    [Fact]
    public void GoGeneral_FromAppearance_SetsActiveSectionFlag()
    {
        var vm = CreateViewModel();
        vm.GoAppearanceCommand.Execute(null);

        vm.GoGeneralCommand.Execute(null);

        Assert.True(vm.IsGeneralSection);
        Assert.False(vm.IsAppearanceSection);
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
    public void EnsureLoaded_PopulatesBehaviorBatch2FlagsFromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.RestoreSessionOnStartup = false;
            settings.PromptReviewOnFinish = true;
            settings.EnableDragDropImport = false;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.RestoreSessionOnStartup);
        Assert.True(vm.PromptReviewOnFinish);
        Assert.False(vm.EnableDragDropImport);
    }

    [Fact]
    public void TogglingBehaviorBatch2Flags_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.RestoreSessionOnStartup = false;
        vm.PromptReviewOnFinish = true;
        vm.EnableDragDropImport = false;

        using var context = new PaperbunkrDbContext(_dbOptions);
        var settings = context.GetOrCreateAppSettings();
        Assert.False(settings.RestoreSessionOnStartup);
        Assert.True(settings.PromptReviewOnFinish);
        Assert.False(settings.EnableDragDropImport);
    }

    /// <summary>docs/superpowers/specs/2026-09-05-nav-rail-hover-toggle-and-undo-redo-removal-design.md - same load/persist shape as the batch2 flags above.</summary>
    [Fact]
    public void EnsureLoaded_PopulatesNavRailHoverExpandEnabled_FromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().NavRailHoverExpandEnabled = false;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.NavRailHoverExpandEnabled);
    }

    [Fact]
    public void NavRailHoverExpandEnabled_Change_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        Assert.True(vm.NavRailHoverExpandEnabled);

        vm.NavRailHoverExpandEnabled = false;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.False(context.GetOrCreateAppSettings().NavRailHoverExpandEnabled);
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

    // Auto-update (docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md)
    [Fact]
    public void EnsureLoaded_PopulatesCheckForUpdatesOnStartupFromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().CheckForUpdatesOnStartup = false;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void TogglingCheckForUpdatesOnStartup_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.CheckForUpdatesOnStartup = false;

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.False(context.GetOrCreateAppSettings().CheckForUpdatesOnStartup);
    }

    [Fact]
    public void GoReader_SetsActiveSectionFlag()
    {
        var vm = CreateViewModel();

        vm.GoReaderCommand.Execute(null);

        Assert.True(vm.IsReaderSection);
        Assert.False(vm.IsGeneralSection);
    }

    [Fact]
    public void GoAbout_SetsActiveSectionFlag()
    {
        var vm = CreateViewModel();

        vm.GoAboutCommand.Execute(null);

        Assert.True(vm.IsAboutSection);
        Assert.Equal(PreferencesSection.About, vm.ActiveSection);
    }

    // About redesign (docs/superpowers/specs/2026-09-07-about-redesign-design.md) - legal docs open
    // in the in-app viewer instead of Process.Start.
    [Fact]
    public void OpenLegalDocument_ExistingFile_PopulatesBlocksAndOpensViewer()
    {
        var vm = CreateViewModel();

        vm.OpenLegalDocumentCommand.Execute("LICENSE");

        Assert.True(vm.IsLegalDocumentViewerOpen);
        Assert.Equal("License", vm.SelectedLegalDocumentTitle);
        Assert.NotEmpty(vm.SelectedLegalDocumentBlocks);
    }

    [Fact]
    public void OpenLegalDocument_MissingFile_LeavesViewerClosed()
    {
        var vm = CreateViewModel();

        vm.OpenLegalDocumentCommand.Execute("DOES_NOT_EXIST.md");

        Assert.False(vm.IsLegalDocumentViewerOpen);
        Assert.Null(vm.SelectedLegalDocumentTitle);
        Assert.Empty(vm.SelectedLegalDocumentBlocks);
    }

    [Fact]
    public void CloseLegalDocumentViewer_ClosesViewer()
    {
        var vm = CreateViewModel();
        vm.OpenLegalDocumentCommand.Execute("PRIVACY.md");

        vm.CloseLegalDocumentViewerCommand.Execute(null);

        Assert.False(vm.IsLegalDocumentViewerOpen);
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
        Assert.Contains(vm.NavigationKeyBindings, r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft && r.BoundKeys.Single().Gesture == new KeyGesture(Key.Left));
        Assert.Contains(vm.NavigationKeyBindings, r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnRight && r.BoundKeys.Single().Gesture == new KeyGesture(Key.Right));
    }

    /// <summary>Replaces a row's sole default binding with a new one via Add-then-Remove (docs/superpowers/specs/2026-09-07-keyboard-shortcuts-redesign-design.md - there's no single "replace" command since a row can hold more than one gesture).</summary>
    private static void ReplaceBinding(KeyBindingRowViewModel row, KeyOption newOption)
    {
        var oldOption = row.BoundKeys.Single();
        row.AddKeyCommand.Execute(newOption);
        row.RemoveKeyCommand.Execute(oldOption);
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
            ReplaceBinding(row, row.AvailableKeyOptions.Single(o => o.Gesture == new KeyGesture(Key.J)));

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
            Assert.Equal(new KeyGesture(Key.J), reloaded.BoundKeys.Single().Gesture);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Per-entry tolerance, not all-or-nothing (docs/superpowers/specs/2026-08-25-reader-chrome-design.md) - mirrors KeyBindingService.GetKeys's own catch (ArgumentException) fallback philosophy, applied at import time.</summary>
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
            Assert.Equal(new KeyGesture(Key.J), left.BoundKeys.Single().Gesture);
            // The corrupt entry didn't throw and didn't block the valid one - right still holds its default.
            var right = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnRight);
            Assert.Equal(new KeyGesture(Key.Right), right.BoundKeys.Single().Gesture);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Genuine multi-binding, not a replace (docs/superpowers/specs/2026-09-07-keyboard-shortcuts-redesign-design.md) - adding a second gesture keeps the first, and both survive a reload.</summary>
    [Fact]
    public void AddKeyCommand_SecondGesture_BothPersistAndBothReturnedOnReload()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);

        row.AddKeyCommand.Execute(row.AvailableKeyOptions.Single(o => o.Gesture == new KeyGesture(Key.J)));

        Assert.Equal(2, row.BoundKeys.Count);
        Assert.Contains(row.BoundKeys, k => k.Gesture == new KeyGesture(Key.Left));
        Assert.Contains(row.BoundKeys, k => k.Gesture == new KeyGesture(Key.J));

        var reloadedVm = CreateViewModel();
        reloadedVm.EnsureLoaded();
        var reloadedRow = reloadedVm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
        Assert.Equal(2, reloadedRow.BoundKeys.Count);
        Assert.Contains(reloadedRow.BoundKeys, k => k.Gesture == new KeyGesture(Key.Left));
        Assert.Contains(reloadedRow.BoundKeys, k => k.Gesture == new KeyGesture(Key.J));
    }

    [Fact]
    public void RemoveKeyCommand_LastRemainingGesture_NoOps()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
        var onlyOption = row.BoundKeys.Single();

        row.RemoveKeyCommand.Execute(onlyOption);

        Assert.Single(row.BoundKeys);
        Assert.Equal(onlyOption, row.BoundKeys.Single());
    }

    [Fact]
    public void ChangingKeyBindingRow_PersistsThroughKeyBindingService()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);

        ReplaceBinding(row, row.AvailableKeyOptions.Single(o => o.Gesture == new KeyGesture(Key.J)));

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal("J", context.KeyBindings.Single(k => k.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft).Key);
    }

    [Fact]
    public void TwoRowsSharingAGesture_BothMarkedIsConflicted_AndClearingOneUnmarksBoth()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var left = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
        var right = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnRight);

        ReplaceBinding(left, left.AvailableKeyOptions.Single(o => o.Gesture == new KeyGesture(Key.J)));
        ReplaceBinding(right, right.AvailableKeyOptions.Single(o => o.Gesture == new KeyGesture(Key.J)));

        Assert.True(vm.HasKeyBindingConflictError);
        Assert.True(left.IsConflicted);
        Assert.True(right.IsConflicted);

        // Resolving it clears the error and both rows' flags again.
        ReplaceBinding(right, right.AvailableKeyOptions.Single(o => o.Gesture == new KeyGesture(Key.K)));
        Assert.False(vm.HasKeyBindingConflictError);
        Assert.False(left.IsConflicted);
        Assert.False(right.IsConflicted);
    }

    [Fact]
    public void RowConflictingWithTwoOthers_AllThreeMarkedIsConflicted()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var pageTurnLeft = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
        var panLeft = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPanLeft);
        var scrollLeft = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderScrollLeft);

        // These three default to Left across three mutually-exclusive-at-runtime contexts, so they
        // don't conflict with each other by default (see FreshLoad_NoConflictError... below). Adding
        // an Always-context gesture (ZoomIn) to all three forces genuine cross-context conflicts:
        // ZoomIn's Always context collides with each of the other three's own context individually.
        var zoomIn = vm.ZoomFitKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderZoomIn);
        ReplaceBinding(zoomIn, zoomIn.AvailableKeyOptions.Single(o => o.Gesture == new KeyGesture(Key.Left)));

        Assert.True(vm.HasKeyBindingConflictError);
        Assert.True(zoomIn.IsConflicted);
        Assert.True(pageTurnLeft.IsConflicted);
        Assert.True(panLeft.IsConflicted);
        Assert.True(scrollLeft.IsConflicted);
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
        ReplaceBinding(panRight, panRight.AvailableKeyOptions.Single(o => o.Gesture == new KeyGesture(Key.Left)));

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
        ReplaceBinding(zoomIn, zoomIn.AvailableKeyOptions.Single(o => o.Gesture == new KeyGesture(Key.Left)));

        Assert.True(vm.HasKeyBindingConflictError);
    }

    [Fact]
    public void ResetKeyBindingsCommand_RevertsEveryRowToDefault_AndClearsConflictError()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var pageTurnLeft = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
        var pageTurnRight = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnRight);
        ReplaceBinding(pageTurnLeft, pageTurnLeft.AvailableKeyOptions.Single(o => o.Gesture == new KeyGesture(Key.J)));
        ReplaceBinding(pageTurnRight, pageTurnRight.AvailableKeyOptions.Single(o => o.Gesture == new KeyGesture(Key.J)));
        Assert.True(vm.HasKeyBindingConflictError);

        vm.ResetKeyBindingsCommand.Execute(null);

        Assert.False(vm.HasKeyBindingConflictError);
        var reloadedLeft = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
        var reloadedRight = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnRight);
        Assert.Equal(new KeyGesture(Key.Left), reloadedLeft.BoundKeys.Single().Gesture);
        Assert.Equal(new KeyGesture(Key.Right), reloadedRight.BoundKeys.Single().Gesture);
    }

    [Fact]
    public async Task ImportKeyBindings_MultipleEntriesForSameCommand_AppliesBothAsSeparateBindings()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_keybindings_multi_test_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, $$"""
                [
                    {"CommandId": "{{KeyboardCommandRegistry.ReaderPageTurnLeft}}", "Gesture": "J"},
                    {"CommandId": "{{KeyboardCommandRegistry.ReaderPageTurnLeft}}", "Gesture": "K"}
                ]
                """);
            var vm = CreateViewModel(new FileRoundTripPicker { OpenPathToReturn = path });
            vm.EnsureLoaded();

            await vm.ImportKeyBindingsCommand.ExecuteAsync(null);

            var row = vm.NavigationKeyBindings.Single(r => r.CommandId == KeyboardCommandRegistry.ReaderPageTurnLeft);
            Assert.Equal(2, row.BoundKeys.Count);
            Assert.Contains(row.BoundKeys, k => k.Gesture == new KeyGesture(Key.J));
            Assert.Contains(row.BoundKeys, k => k.Gesture == new KeyGesture(Key.K));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
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
    public void GoLibrary_SetsActiveSectionFlag()
    {
        var vm = CreateViewModel();

        vm.GoLibraryCommand.Execute(null);

        Assert.True(vm.IsLibrarySection);
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
    public void HasVirtualTags_ReflectsListState()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.HasVirtualTags);

        vm.AddVirtualTagCommand.Execute(null);

        Assert.True(vm.HasVirtualTags);

        vm.DeleteVirtualTagCommand.Execute(null);

        Assert.False(vm.HasVirtualTags);
    }

    [Fact]
    public void SelectVirtualTag_MarksOnlyThatRowSelected()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        vm.AddVirtualTagCommand.Execute(null);
        var first = vm.VirtualTags[0];
        vm.AddVirtualTagCommand.Execute(null);
        var second = vm.VirtualTags[1];

        Assert.False(vm.VirtualTags.Single(t => t.Id == first.Id).IsSelected);
        Assert.True(vm.VirtualTags.Single(t => t.Id == second.Id).IsSelected);

        vm.SelectVirtualTagCommand.Execute(first);

        Assert.True(vm.VirtualTags.Single(t => t.Id == first.Id).IsSelected);
        Assert.False(vm.VirtualTags.Single(t => t.Id == second.Id).IsSelected);
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

    // ===================== Folder management redesign (docs/superpowers/specs/2026-09-07-library-
    // folder-management-redesign-design.md) - empty states, segmented tabs, combined busy flag. =====================

    [Fact]
    public async Task HasWatchedFolders_ReflectsListState()
    {
        var vm = CreateViewModel(new StubFilePicker { FolderToReturn = @"C:\Comics" });
        vm.EnsureLoaded();

        Assert.False(vm.HasWatchedFolders);

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.True(vm.HasWatchedFolders);

        vm.RemoveFolderCommand.Execute(vm.WatchedFolders[0]);

        Assert.False(vm.HasWatchedFolders);
    }

    [Fact]
    public async Task HasBookFolders_ReflectsListState()
    {
        var vm = CreateViewModel(new StubFilePicker { FolderToReturn = @"C:\Books" });
        vm.EnsureLoaded();

        Assert.False(vm.HasBookFolders);

        await vm.AddBookFolderCommand.ExecuteAsync(null);

        Assert.True(vm.HasBookFolders);

        vm.RemoveBookFolderCommand.Execute(vm.BookFolders[0]);

        Assert.False(vm.HasBookFolders);
    }

    [Fact]
    public void ComicFoldersTabToggle_SwitchesBetweenFoldersAndMaintenance()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.IsComicFoldersMaintenanceTabActive);

        vm.ShowComicFoldersMaintenanceTabCommand.Execute(null);
        Assert.True(vm.IsComicFoldersMaintenanceTabActive);

        vm.ShowComicFoldersFoldersTabCommand.Execute(null);
        Assert.False(vm.IsComicFoldersMaintenanceTabActive);
    }

    [Fact]
    public void BookFoldersTabToggle_SwitchesBetweenFoldersAndMaintenance()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.IsBookFoldersMaintenanceTabActive);

        vm.ShowBookFoldersMaintenanceTabCommand.Execute(null);
        Assert.True(vm.IsBookFoldersMaintenanceTabActive);

        vm.ShowBookFoldersFoldersTabCommand.Execute(null);
        Assert.False(vm.IsBookFoldersMaintenanceTabActive);
    }

    [Fact]
    public void IsAnyComicFolderOperationRunning_ReflectsAnyIndividualFlag()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.IsAnyComicFolderOperationRunning);

        vm.IsGeneratingCovers = true;
        Assert.True(vm.IsAnyComicFolderOperationRunning);
        vm.IsGeneratingCovers = false;

        vm.IsSyncingMetadata = true;
        Assert.True(vm.IsAnyComicFolderOperationRunning);
        vm.IsSyncingMetadata = false;

        Assert.False(vm.IsAnyComicFolderOperationRunning);
    }

    [Fact]
    public void IsAnyBookFolderOperationRunning_ReflectsAnyIndividualFlag()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.IsAnyBookFolderOperationRunning);

        vm.IsClearingBookCoverCache = true;
        Assert.True(vm.IsAnyBookFolderOperationRunning);
        vm.IsClearingBookCoverCache = false;

        Assert.False(vm.IsAnyBookFolderOperationRunning);
    }

    [Fact]
    public async Task ScanNow_RunsAsActivityJob_ThatSucceeds()
    {
        var activity = new ActivityService(a => a(), _ => { });
        var vm = CreateViewModel(activity: activity);
        vm.EnsureLoaded();

        await vm.ScanNowCommand.ExecuteAsync(null);

        var job = Assert.Single(activity.RecentJobs);
        Assert.Equal(ActivityJobStatus.Succeeded, job.Status);
        Assert.Equal("No new issues found.", job.ResultSummary);
        Assert.False(vm.IsScanning);
        Assert.Null(vm.CurrentComicFolderJob);
    }

    // ===================== Scanning: missing-file handling (docs/superpowers/specs/2026-09-06-
    // scan-missing-file-handling-design.md) - AutoRemoveMissingOnScan's effect on Scan Now.
    // Reuses SeedMissingIssue above (already scopes FilePath under _scanRoot). =====================

    [Fact]
    public async Task ScanNow_AutoRemoveMissingOnScan_On_ReachesTwoStrikes_AutoRemoves()
    {
        // Already at one strike from a prior Verify - Scan Now's own auto-triggered Verify supplies
        // the second, crossing the two-strikes threshold.
        SeedMissingIssue("gone.cbz", missingVerificationCount: 1);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().AutoRemoveMissingOnScan = true;
            context.SaveChanges();
        }

        var activity = new ActivityService(a => a(), _ => { });
        var vm = CreateViewModel(activity: activity);
        vm.EnsureLoaded();

        await vm.ScanNowCommand.ExecuteAsync(null);

        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(verify.Issues);
        Assert.Single(verify.RemovedLibraryEntries);
        Assert.Single(verify.RemovedFilePaths);
        var job = Assert.Single(activity.RecentJobs);
        Assert.Contains("Automatically removed 1 confirmed-missing file", job.ResultSummary);
    }

    [Fact]
    public async Task ScanNow_AutoRemoveMissingOnScan_Off_LeavesConfirmedMissingIssueInPlace()
    {
        // Setting stays at its default (false) - no AppSettings write.
        SeedMissingIssue("gone.cbz", missingVerificationCount: 1);

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        await vm.ScanNowCommand.ExecuteAsync(null);

        using var verify = new PaperbunkrDbContext(_dbOptions);
        var issue = Assert.Single(verify.Issues);
        Assert.True(issue.FileIsMissing);
        Assert.Empty(verify.RemovedLibraryEntries);
    }

    [Fact]
    public async Task ScanNow_AutoRemoveMissingOnScan_On_UnreachableDriveRoot_LeavesIssueForManualReview()
    {
        // "?:" is not a valid drive letter on any real Windows install - stands in for "unplugged",
        // same trick LibraryHealthServiceTests uses for IsPathRootReachable. Built inline (not via
        // SeedMissingIssue, which always scopes the path under _scanRoot) so the path stays exactly
        // this bogus root.
        int issueId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().AutoRemoveMissingOnScan = true;
            var series = new Series { Name = "Ghost Series" };
            var issue = new Issue { Series = series, Number = "1", FilePath = @"?:\comics\gone.cbz", FileIsMissing = true, MissingVerificationCount = 1 };
            context.Series.Add(series);
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        await vm.ScanNowCommand.ExecuteAsync(null);

        using var verify = new PaperbunkrDbContext(_dbOptions);
        var issueAfter = verify.Issues.Find(issueId);
        Assert.NotNull(issueAfter);
        Assert.True(issueAfter!.FileIsMissing);
        Assert.Equal(2, issueAfter.MissingVerificationCount);
        Assert.Empty(verify.RemovedLibraryEntries);
    }

    // --- Book Folders (novels) - moved here from the Books screen
    // (docs/superpowers/specs/2026-08-27-books-section-restyle-and-folders-to-preferences-plan.md) ---

    [Fact]
    public async Task AddBookFolder_UserPicksFolder_PersistsAndRefreshesList()
    {
        var vm = CreateViewModel(new StubFilePicker { FolderToReturn = @"D:\Novels" });
        vm.EnsureLoaded();

        await vm.AddBookFolderCommand.ExecuteAsync(null);

        Assert.Single(vm.BookFolders);
        Assert.Equal(@"D:\Novels", vm.BookFolders[0].Path);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.BookFolders);
    }

    [Fact]
    public async Task AddBookFolder_UserCancels_DoesNotAdd()
    {
        var vm = CreateViewModel(new StubFilePicker { FolderToReturn = null });
        vm.EnsureLoaded();

        await vm.AddBookFolderCommand.ExecuteAsync(null);

        Assert.Empty(vm.BookFolders);
    }

    [Fact]
    public void RemoveBookFolder_DeletesItFromListAndDatabase()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.BookFolders.Add(new Paperbunkr.Data.Entities.BookFolder { Path = @"D:\Novels" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.RemoveBookFolderCommand.Execute(vm.BookFolders[0]);

        Assert.Empty(vm.BookFolders);
        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(verify.BookFolders);
    }

    [Fact]
    public async Task ScanBooksNow_RunsAsActivityJob_ThatSucceeds()
    {
        var activity = new ActivityService(a => a(), _ => { });
        var vm = CreateViewModel(activity: activity);
        vm.EnsureLoaded();

        await vm.ScanBooksNowCommand.ExecuteAsync(null);

        var job = Assert.Single(activity.RecentJobs);
        Assert.Equal(ActivityJobStatus.Succeeded, job.Status);
        Assert.Equal("No new books found.", job.ResultSummary);
        Assert.False(vm.IsScanningBooks);
        Assert.Null(vm.CurrentBookFolderJob);
    }

    [Fact]
    public async Task ScanNow_RecordsSucceededActivityJob_OnCompletion()
    {
        var activity = new ActivityService(a => a(), _ => { });
        var vm = CreateViewModel(activity: activity);
        vm.EnsureLoaded();

        await vm.ScanNowCommand.ExecuteAsync(null);

        var job = Assert.Single(activity.RecentJobs);
        Assert.Equal(ActivityJobStatus.Succeeded, job.Status);
        Assert.Equal("No new issues found.", job.ResultSummary);
        Assert.Empty(activity.ActiveJobs);
    }

    [Fact]
    public async Task GenerateCoversCommand_RunsAsActivityJob_ThatSucceeds()
    {
        var activity = new ActivityService(a => a(), _ => { });
        ToastRequest? completionToast = null;
        activity.CompletionToastRequested += request => completionToast = request;
        var vm = CreateViewModel(activity: activity);

        await vm.GenerateCoversCommand.ExecuteAsync(null);

        var job = Assert.Single(activity.RecentJobs);
        Assert.Equal(ActivityJobStatus.Succeeded, job.Status);
        Assert.False(vm.IsGeneratingCovers);
        Assert.NotNull(completionToast);
        Assert.Equal("Generating covers finished", completionToast!.Title);
    }

    /// <summary>
    /// docs/superpowers/specs/2026-08-30-cover-thumbnail-content-verification-design.md - separate
    /// command from GenerateCovers, covers comics and books in one run. Seeds one of each so the
    /// completion toast's combined total (a real bug caught during design review: two sequential
    /// progress callbacks sharing one toast can make the total undercount/jump backward if not
    /// accumulated) is actually checkable.
    /// </summary>
    [Fact]
    public async Task VerifyCoversCommand_RunsAsActivityJob_ThatSucceedsWithCombinedTotal()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = context.Series.Add(new Series { Name = "Verify Covers Series" }).Entity;
            context.SaveChanges();
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", FilePath = @"C:\nowhere\missing.cbz" });
            context.Books.Add(new Book { Title = "T", Format = BookFormat.Epub, FilePath = @"C:\nowhere\missing.epub" });
            context.SaveChanges();
        }

        var activity = new ActivityService(a => a(), _ => { });
        ToastRequest? completionToast = null;
        activity.CompletionToastRequested += request => completionToast = request;
        var vm = CreateViewModel(activity: activity);

        await vm.VerifyCoversCommand.ExecuteAsync(null);

        var job = Assert.Single(activity.RecentJobs);
        Assert.Equal(ActivityJobStatus.Succeeded, job.Status);
        Assert.False(vm.IsVerifyingCovers);
        Assert.NotNull(completionToast);
        Assert.Equal("Verifying covers finished", completionToast!.Title);
        Assert.Equal("Re-checked 2 covers", completionToast!.Message); // 1 issue + 1 book
    }

    // --- Clear Cover Cache (docs/superpowers/specs/2026-08-30-cover-thumbnail-content-
    //     verification-design.md) - TwoStepConfirm escape hatch, independent of VerifyCovers. ---

    [Fact]
    public void ClearComicCoverCacheConfirm_FirstTrigger_ArmsWithoutDeletingAnything()
    {
        Directory.CreateDirectory(CoverThumbnailPaths.ThumbnailDirectory);
        string existing = Path.Combine(CoverThumbnailPaths.ThumbnailDirectory, "1-deadbeef.jpg");
        File.WriteAllBytes(existing, new byte[] { 1 });
        var vm = CreateViewModel();

        vm.ClearComicCoverCacheConfirm.TriggerCommand.Execute(null);

        Assert.True(vm.ClearComicCoverCacheConfirm.IsArmed);
        Assert.True(File.Exists(existing));
    }

    [Fact]
    public async Task ClearComicCoverCacheConfirm_SecondTrigger_DeletesEverythingAndRebuilds()
    {
        CbzFixture.Create(Path.Combine(_scanRoot, "1.cbz"), pageCount: 1);
        int issueId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = context.Series.Add(new Series { Name = "Clear Cache Series" }).Entity;
            context.SaveChanges();
            var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = Path.Combine(_scanRoot, "1.cbz"), FileSize = 1 };
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;
        }
        new CoverThumbnailService(() => new PaperbunkrDbContext(_dbOptions)).TryGenerateThumbnail(issueId, Path.Combine(_scanRoot, "1.cbz"), fileSize: 1);
        Directory.CreateDirectory(CoverThumbnailPaths.ThumbnailDirectory);
        string strayFile = Path.Combine(CoverThumbnailPaths.ThumbnailDirectory, "999-cafef00d.jpg"); // an orphan that must go too
        File.WriteAllBytes(strayFile, new byte[] { 1 });

        var activity = new ActivityService(a => a(), _ => { });
        ToastRequest? completionToast = null;
        activity.CompletionToastRequested += request => completionToast = request;
        var vm = CreateViewModel(activity: activity);

        vm.ClearComicCoverCacheConfirm.TriggerCommand.Execute(null); // arm
        vm.ClearComicCoverCacheConfirm.TriggerCommand.Execute(null); // confirm - fire-and-forget
        await Task.Delay(500); // let the fire-and-forget rebuild finish (headless dispatcher, no completion signal to await directly)

        Assert.False(File.Exists(strayFile)); // wiped, not just skipped
        Assert.False(vm.IsGeneratingCovers);
        Assert.NotNull(completionToast);
        Assert.Equal("Rebuilding comic covers finished", completionToast!.Title);
        var stem = CoverFingerprint.Stem(issueId, Path.Combine(_scanRoot, "1.cbz"), 1);
        Assert.True(File.Exists(CoverThumbnailPaths.GetCachePath(stem))); // regenerated, not left blank
    }

    [Fact]
    public void ClearBookCoverCacheConfirm_FirstTrigger_ArmsWithoutDeletingAnything()
    {
        Directory.CreateDirectory(BookCoverThumbnailPaths.ThumbnailDirectory);
        string existing = Path.Combine(BookCoverThumbnailPaths.ThumbnailDirectory, "1-deadbeef.jpg");
        File.WriteAllBytes(existing, new byte[] { 1 });
        var vm = CreateViewModel();

        vm.ClearBookCoverCacheConfirm.TriggerCommand.Execute(null);

        Assert.True(vm.ClearBookCoverCacheConfirm.IsArmed);
        Assert.True(File.Exists(existing));
    }

    [Fact]
    public async Task ClearBookCoverCacheConfirm_SecondTrigger_DeletesEverythingAndRebuilds()
    {
        Directory.CreateDirectory(BookCoverThumbnailPaths.ThumbnailDirectory);
        string strayFile = Path.Combine(BookCoverThumbnailPaths.ThumbnailDirectory, "999-cafef00d.jpg");
        File.WriteAllBytes(strayFile, new byte[] { 1 });

        var activity = new ActivityService(a => a(), _ => { });
        ToastRequest? completionToast = null;
        activity.CompletionToastRequested += request => completionToast = request;
        var vm = CreateViewModel(activity: activity);

        vm.ClearBookCoverCacheConfirm.TriggerCommand.Execute(null); // arm
        vm.ClearBookCoverCacheConfirm.TriggerCommand.Execute(null); // confirm - fire-and-forget
        await Task.Delay(500);

        Assert.False(File.Exists(strayFile));
        Assert.False(vm.IsClearingBookCoverCache);
        Assert.NotNull(completionToast);
        Assert.Equal("Rebuilding book covers finished", completionToast!.Title);
    }

    [Fact]
    public async Task SyncMetadataCommand_RunsAsActivityJob_ThatSucceeds()
    {
        var activity = new ActivityService(a => a(), _ => { });
        var vm = CreateViewModel(activity: activity);

        await vm.SyncMetadataCommand.ExecuteAsync(null);

        var job = Assert.Single(activity.RecentJobs);
        Assert.Equal(ActivityJobStatus.Succeeded, job.Status);
        Assert.Equal("No new metadata found", job.ResultSummary);
        Assert.False(vm.IsSyncingMetadata);
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

        var activity = new ActivityService(a => a(), _ => { });
        var vm = CreateViewModel(activity: activity);
        vm.EnsureLoaded();

        await vm.ScanNowCommand.ExecuteAsync(null);

        var job = Assert.Single(activity.RecentJobs);
        Assert.Equal("Added 1 issue across 1 series.", job.ResultSummary);
        using var verifyContext = new PaperbunkrDbContext(_dbOptions);
        var issue = Assert.Single(verifyContext.Issues);
        string stem = CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize);
        Assert.True(File.Exists(CoverThumbnailPaths.GetCachePath(stem)));
    }

    [Fact]
    public void GoAdvanced_SetsActiveSectionFlag()
    {
        var vm = CreateViewModel();

        vm.GoAdvancedCommand.Execute(null);

        Assert.True(vm.IsAdvancedSection);
    }

    /// <summary>docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md - Plugins
    /// moved from a standalone rail screen into this section.</summary>
    [Fact]
    public void GoPlugins_SetsActiveSectionFlag()
    {
        var vm = CreateViewModel();

        vm.GoPluginsCommand.Execute(null);

        Assert.True(vm.IsPluginsSection);
        Assert.False(vm.IsAdvancedSection);
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

    /// <summary>docs/superpowers/specs/2026-08-29-db-corruption-safeguards-design.md §2.</summary>
    [Fact]
    public void ChangingAutoBackupSettings_PersistsToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.AutoBackupEnabled = false;
        vm.AutoBackupMinIntervalHours = 12;

        using var context = new PaperbunkrDbContext(_dbOptions);
        var settings = context.GetOrCreateAppSettings();
        Assert.False(settings.AutoBackupEnabled);
        Assert.Equal(12, settings.AutoBackupMinIntervalHours);
    }

    [Fact]
    public void EnsureLoaded_LoadsAutoBackupSettings_FromAppSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.AutoBackupEnabled = false;
            settings.AutoBackupMinIntervalHours = 8;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.False(vm.AutoBackupEnabled);
        Assert.Equal(8, vm.AutoBackupMinIntervalHours);
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

    private sealed class FakeDialogService : IDialogService
    {
        private readonly int _answer;
        public ConfirmDialogRequest? LastRequest { get; private set; }

        public FakeDialogService(int answer = 0) => _answer = answer;

        public Task<int> ShowAsync(ConfirmDialogRequest request)
        {
            LastRequest = request;
            return Task.FromResult(_answer);
        }

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(_answer == 0);
    }

    private sealed class StubFilePicker : IFilePickerService
    {
        public string? FolderToReturn { get; set; }

        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderToReturn);

        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    [Fact]
    public void MetadataWriteBackToggles_PersistToAppSettings()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.WriteMetadataToFiles = true;
        vm.WriteMetadataAutomatically = true;
        vm.WriteNativeSidecar = true;

        using var context = new PaperbunkrDbContext(_dbOptions);
        var settings = context.GetOrCreateAppSettings();
        Assert.True(settings.WriteMetadataToFiles);
        Assert.True(settings.WriteMetadataAutomatically);
        Assert.True(settings.WriteNativeSidecar);
    }

    [Fact]
    public void WriteAllMetadataToFiles_EnqueuesEveryFiledIssue_AsManual()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().WriteMetadataToFiles = true;
            var series = new Paperbunkr.Data.Entities.Series { Name = "S" };
            context.Series.Add(series);
            context.SaveChanges();
            context.Issues.Add(new Paperbunkr.Data.Entities.Issue { SeriesId = series.Id, Number = "1", FilePath = @"C:\a.cbz" });
            context.Issues.Add(new Paperbunkr.Data.Entities.Issue { SeriesId = series.Id, Number = "2", FilePath = @"C:\b.cbz" });
            context.Issues.Add(new Paperbunkr.Data.Entities.Issue { SeriesId = series.Id, Number = "3", IsPlaceholder = true }); // no file - skipped
            context.SaveChanges();
        }

        var enqueued = new List<(int Id, bool Manual)>();
        var vm = CreateViewModel(enqueueMetadataWriteBack: (id, manual) => enqueued.Add((id, manual)));
        vm.EnsureLoaded();

        vm.WriteAllMetadataToFilesCommand.Execute(null);

        Assert.Equal(2, enqueued.Count);
        Assert.All(enqueued, e => Assert.True(e.Manual));
    }

    // --- Library Health (docs/superpowers/specs/2026-09-06-missing-files-library-health-design.md) ---

    private int SeedMissingIssue(string fileName = "gone.cbz", int missingVerificationCount = 0, bool missingAcknowledged = false, string seriesName = "Ghost Series")
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = seriesName };
        var issue = new Issue
        {
            Series = series,
            Number = "1",
            FilePath = Path.Combine(_scanRoot, fileName),
            FileIsMissing = true,
            MissingVerificationCount = missingVerificationCount,
            MissingAcknowledged = missingAcknowledged,
        };
        context.Series.Add(series);
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public async Task VerifyLibraryHealthNow_UpdatesSummaryCountsAndLastVerified()
    {
        SeedMissingIssue();
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        await vm.VerifyLibraryHealthNowCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.LibraryHealthChecked);
        Assert.Equal(1, vm.LibraryHealthMissingNow);
        Assert.NotEqual("Never", vm.LibraryHealthLastVerifiedLabel);
        Assert.False(vm.IsVerifyingLibraryHealth);
        Assert.Null(vm.CurrentLibraryHealthJob);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.NotNull(context.GetOrCreateAppSettings().LastLibraryHealthVerifyUtc);
    }

    [Fact]
    public void ChangingConfirmedMissingThreshold_PersistsAndRecomputesCounts()
    {
        SeedMissingIssue(missingVerificationCount: 2);
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        Assert.Equal(2, vm.LibraryHealthConfirmedMissingThreshold);
        Assert.True(vm.HasConfirmedMissingItems);

        vm.LibraryHealthConfirmedMissingThreshold = 3;

        Assert.False(vm.HasConfirmedMissingItems);
        Assert.Equal(0, vm.LibraryHealthConfirmedMissing);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(3, context.GetOrCreateAppSettings().LibraryHealthConfirmedMissingThreshold);
    }

    [Fact]
    public async Task RemoveFolder_RunsScopedVerify_OnThatFoldersIssues()
    {
        int issueId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.WatchedFolders.Add(new WatchedFolder { Path = _scanRoot });
            var series = new Series { Name = "Ghost Series" };
            var issue = new Issue { Series = series, Number = "1", FilePath = Path.Combine(_scanRoot, "gone.cbz") };
            context.Series.Add(series);
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;
        }

        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var folder = vm.WatchedFolders[0];

        await vm.RemoveFolderCommand.ExecuteAsync(folder);

        Assert.Empty(vm.WatchedFolders);
        using var verify = new PaperbunkrDbContext(_dbOptions);
        var issueAfter = verify.Issues.Find(issueId)!;
        Assert.True(issueAfter.FileIsMissing);
        Assert.Equal(1, issueAfter.MissingVerificationCount);
    }

    [Fact]
    public void RelinkMissingFile_ClearsMissingFlagAndResetsCount()
    {
        int issueId = SeedMissingIssue(missingVerificationCount: 3);
        string newPath = Path.Combine(_scanRoot, "found.cbz");
        var vm = CreateViewModel(new FileRoundTripPicker { OpenPathToReturn = newPath });
        vm.EnsureLoaded();
        var row = Assert.Single(vm.MissingFileItems);

        row.RelinkCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var issue = context.Issues.Find(issueId)!;
        Assert.False(issue.FileIsMissing);
        Assert.Equal(0, issue.MissingVerificationCount);
        Assert.Equal(newPath, issue.FilePath);
    }

    [Fact]
    public void DismissMissingFile_SetsAcknowledged_AndDropsFromList()
    {
        SeedMissingIssue();
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = Assert.Single(vm.MissingFileItems);

        row.DismissCommand.Execute(null);

        Assert.Empty(vm.MissingFileItems);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.True(context.Issues.Single().MissingAcknowledged);
    }

    [Fact]
    public void RemoveMissingFile_WritesRemovedLibraryEntry_ThenDeletesIssue()
    {
        int issueId = SeedMissingIssue();
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = Assert.Single(vm.MissingFileItems);

        row.DeleteConfirm.TriggerCommand.Execute(null);
        row.DeleteConfirm.TriggerCommand.Execute(null);

        Assert.Empty(vm.MissingFileItems);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Null(context.Issues.Find(issueId));
        var entry = Assert.Single(context.RemovedLibraryEntries);
        Assert.Equal("Ghost Series", entry.SeriesName);
        Assert.Equal(RemovedLibraryEntryReason.MissingFileCleanup, entry.Reason);
        Assert.Single(vm.RecentlyRemovedItems);
    }

    [Fact]
    public void BulkDismissMissingFiles_AcknowledgesEveryListedItem_RegardlessOfStrikeCount()
    {
        int notYetConfirmedId = SeedMissingIssue("recent-gone.cbz", missingVerificationCount: 0);
        int confirmedId = SeedMissingIssue("confirmed-gone.cbz", missingVerificationCount: 2);
        var vm = CreateViewModel();
        vm.EnsureLoaded();

        vm.BulkDismissMissingFilesCommand.Execute(null);

        Assert.Empty(vm.MissingFileItems);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.True(context.Issues.Find(notYetConfirmedId)!.MissingAcknowledged);
        Assert.True(context.Issues.Find(confirmedId)!.MissingAcknowledged);
    }

    [Fact]
    public async Task BulkRelinkMissingFiles_MatchesByFileNameUnderChosenFolder()
    {
        SeedMissingIssue("kilo-station-012.cbz");
        string newRoot = Path.Combine(_scanRoot, "reorganized");
        Directory.CreateDirectory(newRoot);
        string newPath = Path.Combine(newRoot, "kilo-station-012.cbz");
        File.WriteAllText(newPath, "fixture");
        var toasts = new List<(string Title, string Message)>();
        var vm = CreateViewModel(new StubFilePicker { FolderToReturn = newRoot }, showToast: (title, message) => toasts.Add((title, message)));
        vm.EnsureLoaded();

        await vm.BulkRelinkMissingFilesCommand.ExecuteAsync(null);

        Assert.Empty(vm.MissingFileItems);
        Assert.Single(toasts);
        Assert.Equal("Relinked 1 of 1 missing issue.", toasts[0].Message);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var issue = context.Issues.Single();
        Assert.False(issue.FileIsMissing);
        Assert.Equal(newPath, issue.FilePath);
    }

    [Fact]
    public async Task BulkRelinkMissingFiles_UserCancels_DoesNothing()
    {
        SeedMissingIssue();
        var toasts = new List<(string Title, string Message)>();
        var vm = CreateViewModel(new StubFilePicker { FolderToReturn = null }, showToast: (title, message) => toasts.Add((title, message)));
        vm.EnsureLoaded();

        await vm.BulkRelinkMissingFilesCommand.ExecuteAsync(null);

        Assert.Single(vm.MissingFileItems);
        Assert.Empty(toasts);
    }

    [Fact]
    public void ToggleRecentlyRemovedExpanded_FlipsTheBool()
    {
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        Assert.False(vm.IsRecentlyRemovedExpanded);

        vm.ToggleRecentlyRemovedExpandedCommand.Execute(null);
        Assert.True(vm.IsRecentlyRemovedExpanded);

        vm.ToggleRecentlyRemovedExpandedCommand.Execute(null);
        Assert.False(vm.IsRecentlyRemovedExpanded);
    }

    [Fact]
    public async Task OpenBulkRemoveConfirm_ShowsSharedDialog_WithEligibleIssuesOnly()
    {
        SeedMissingIssue("confirmed-gone.cbz", missingVerificationCount: 2);
        SeedMissingIssue("recent-gone.cbz", missingVerificationCount: 1);
        var dialog = new FakeDialogService();
        var vm = CreateViewModel(dialogService: dialog);
        vm.EnsureLoaded();

        await vm.OpenBulkRemoveConfirmCommand.ExecuteAsync(null);

        Assert.NotNull(dialog.LastRequest);
        Assert.Single(dialog.LastRequest!.Items!);
        Assert.Contains("confirmed-gone.cbz", dialog.LastRequest.Items![0]);
        Assert.True(dialog.LastRequest.IsDestructive);
    }

    [Fact]
    public async Task OpenBulkRemoveConfirm_PrimaryAnswer_RemovesOnlyConfirmedMissingIssues()
    {
        int confirmedId = SeedMissingIssue("confirmed-gone.cbz", missingVerificationCount: 2);
        int notYetConfirmedId = SeedMissingIssue("recent-gone.cbz", missingVerificationCount: 1);
        var vm = CreateViewModel(dialogService: new FakeDialogService(answer: 0));
        vm.EnsureLoaded();

        await vm.OpenBulkRemoveConfirmCommand.ExecuteAsync(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Null(context.Issues.Find(confirmedId));
        Assert.NotNull(context.Issues.Find(notYetConfirmedId));
        Assert.Single(context.RemovedLibraryEntries);
    }

    [Fact]
    public async Task OpenBulkRemoveConfirm_NonPrimaryAnswer_RemovesNothing()
    {
        int confirmedId = SeedMissingIssue("confirmed-gone.cbz", missingVerificationCount: 2);
        var vm = CreateViewModel(dialogService: new FakeDialogService(answer: 1));
        vm.EnsureLoaded();

        await vm.OpenBulkRemoveConfirmCommand.ExecuteAsync(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.NotNull(context.Issues.Find(confirmedId));
        Assert.Empty(context.RemovedLibraryEntries);
    }

    [Fact]
    public void RestoreRemovedEntry_RecreatesIssue_StillFlaggedMissing()
    {
        SeedMissingIssue();
        var vm = CreateViewModel();
        vm.EnsureLoaded();
        var row = Assert.Single(vm.MissingFileItems);
        row.DeleteConfirm.TriggerCommand.Execute(null);
        row.DeleteConfirm.TriggerCommand.Execute(null);
        var removedRow = Assert.Single(vm.RecentlyRemovedItems);

        removedRow.RestoreCommand.Execute(null);

        Assert.Empty(vm.RecentlyRemovedItems);
        Assert.Single(vm.MissingFileItems);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.RemovedLibraryEntries);
        var restored = Assert.Single(context.Issues);
        Assert.True(restored.FileIsMissing);
        Assert.Equal(0, restored.MissingVerificationCount);
        Assert.Equal("Ghost Series", context.Series.Single().Name);
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

    // Automation redesign (docs/superpowers/specs/2026-09-08-automation-tasks-redesign-design.md) -
    // CreateViewModel never wires a scheduler, so these tests attach their own fake.

    private static ScheduledTaskRow FakeRow(string id, bool enabled) => new()
    {
        TaskId = id,
        DisplayName = id,
        Description = "",
        ActivityKind = ActivityJobKind.Other,
        Enabled = enabled,
    };

    [Fact]
    public void RunAllScheduledTasksNow_CallsRunNowAsyncForEveryTask_RegardlessOfEnabled()
    {
        var vm = CreateViewModel();
        var scheduler = new FakeSchedulerService();
        scheduler.Tasks.Add(FakeRow("a", enabled: true));
        scheduler.Tasks.Add(FakeRow("b", enabled: false));
        scheduler.Tasks.Add(FakeRow("c", enabled: true));
        vm.AttachScheduler(scheduler);

        vm.RunAllScheduledTasksNowCommand.Execute(null);

        Assert.Equal(new[] { "a", "b", "c" }, scheduler.RunNowCalls.OrderBy(id => id));
    }

    [Fact]
    public void RunAllScheduledTasksNow_WithNoSchedulerAttached_NoOps()
    {
        var vm = CreateViewModel();

        vm.RunAllScheduledTasksNowCommand.Execute(null);
    }

    [Fact]
    public void TogglePauseAll_DisablesEveryTask()
    {
        var vm = CreateViewModel();
        var scheduler = new FakeSchedulerService();
        scheduler.Tasks.Add(FakeRow("a", enabled: true));
        scheduler.Tasks.Add(FakeRow("b", enabled: false));
        scheduler.Tasks.Add(FakeRow("c", enabled: true));
        vm.AttachScheduler(scheduler);

        vm.TogglePauseAllScheduledTasksCommand.Execute(null);

        Assert.True(vm.IsSchedulerPaused);
        Assert.All(scheduler.Tasks, row => Assert.False(row.Enabled));
    }

    [Fact]
    public void TogglePauseAll_ThenToggleAgain_RestoresOriginalEnabledStates_NotAllTrue()
    {
        var vm = CreateViewModel();
        var scheduler = new FakeSchedulerService();
        scheduler.Tasks.Add(FakeRow("a", enabled: true));
        scheduler.Tasks.Add(FakeRow("b", enabled: false));
        scheduler.Tasks.Add(FakeRow("c", enabled: true));
        scheduler.Tasks.Add(FakeRow("d", enabled: false));
        vm.AttachScheduler(scheduler);

        vm.TogglePauseAllScheduledTasksCommand.Execute(null);
        vm.TogglePauseAllScheduledTasksCommand.Execute(null);

        Assert.False(vm.IsSchedulerPaused);
        Assert.True(scheduler.Tasks.Single(r => r.TaskId == "a").Enabled);
        Assert.False(scheduler.Tasks.Single(r => r.TaskId == "b").Enabled);
        Assert.True(scheduler.Tasks.Single(r => r.TaskId == "c").Enabled);
        Assert.False(scheduler.Tasks.Single(r => r.TaskId == "d").Enabled);
    }

    // --- SuggestBox string projections (docs/superpowers/specs/2026-09-10-suggestbox-migration-plan.md) ---

    [Theory]
    [InlineData(nameof(ImageFitMode.FitHeight))]
    [InlineData(nameof(ImageFitMode.BestFit))]
    public void DefaultPageFitModeText_RoundTrips(string name)
    {
        var vm = CreateViewModel();

        vm.DefaultPageFitModeText = name;

        Assert.Equal(Enum.Parse<ImageFitMode>(name), vm.DefaultPageFitMode);
        Assert.Equal(name, vm.DefaultPageFitModeText);
        Assert.Contains(name, vm.FitModeNames);
    }

    [Fact]
    public void EnumText_IgnoresTextThatIsNotAMember_AndNamesMatchTheEnum()
    {
        var vm = CreateViewModel();
        var keptFit = vm.DefaultPageFitMode;
        var keptBackend = vm.RenderingBackend;

        vm.DefaultPageFitModeText = "Nonsense";
        vm.RenderingBackendText = "Nonsense";

        Assert.Equal(keptFit, vm.DefaultPageFitMode);
        Assert.Equal(keptBackend, vm.RenderingBackend);
        Assert.Equal(Enum.GetNames<PageLayoutMode>(), vm.PageLayoutModeNames);
        Assert.Equal(Enum.GetNames<PageTransitionStyle>(), vm.PageTransitionStyleNames);
        Assert.Equal(Enum.GetNames<ImageBackgroundMode>(), vm.BackgroundModeNames);
        Assert.Equal(Enum.GetNames<RenderBackend>(), vm.RenderBackendNames);
        Assert.Equal(Enum.GetNames<ScheduledTaskNotificationLevel>(), vm.NotificationLevelNames);
    }

    /// <summary>Records calls and mirrors <see cref="ISchedulerService.SetEnabled"/> onto the backing
    /// rows (and raises Changed, matching real SchedulerService behavior) so
    /// PreferencesScreenViewModel's RebuildScheduledTasks subscription keeps ScheduledTasks in
    /// sync the same way it would against the real scheduler.</summary>
    private sealed class FakeSchedulerService : ISchedulerService
    {
        public List<ScheduledTaskRow> Tasks { get; } = new();

        IReadOnlyList<ScheduledTaskRow> ISchedulerService.Tasks => Tasks;

        public event EventHandler? Changed;

        public List<string> RunNowCalls { get; } = new();

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public Task RunNowAsync(string taskId)
        {
            RunNowCalls.Add(taskId);
            return Task.CompletedTask;
        }

        public void NotifyRan(string taskId, ScheduledRunStatus status)
        {
        }

        public void SetEnabled(string taskId, bool enabled)
        {
            var row = Tasks.Single(r => r.TaskId == taskId);
            row.Enabled = enabled;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void SetSchedule(string taskId, ScheduleMode mode, int intervalHours, int dailyAtMinutes)
        {
        }
    }
}
