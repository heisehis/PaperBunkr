using ClusterLibraryManager.ComicVine;
using ClusterLibraryManager.Dialogs;
using ClusterLibraryManager.Organizing;
using ClusterLibraryManager.Persistence;
using ClusterLibraryManager.Settings;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins.Abstractions.Native;
using Paperbunkr.Plugins.Abstractions.Ui;

namespace ClusterLibraryManager;

/// <summary>
/// Entry point for the Cluster Library Manager native plugin (docs/superpowers/specs/2026-09-11-
/// cluster-library-manager-design.md §2, implementation plan Phase 3). Wires the plugin's own local
/// database, its ComicVine/organizer services, its self-contained automation timer (design §9 - no
/// Scheduled Tasks extension point exists, verified), and its Library-hook commands together.
/// </summary>
public sealed class OrganizerScraperPlugin : INativePluginModule, INativePluginSettingsUi
{
    private INativePluginEnvironment? _environment;
    private PluginDatabase? _database;
    private PluginSettingsStore? _settingsStore;
    private OrganizerProfileStore? _profileStore;
    private UndoLog? _undoLog;
    private LibraryOrganizerService? _organizerService;
    private Timer? _automationTimer;

    public void Initialize(INativePluginEnvironment environment)
    {
        _environment = environment;

        // The plugin's own installed folder (design doc §10's shared LiteDB file) - not
        // environment.CommandPath, which isn't meaningfully set yet at this per-plugin (not
        // per-command) Initialize call.
        string? pluginDirectory = Path.GetDirectoryName(typeof(OrganizerScraperPlugin).Assembly.Location);
        string databasePath = Path.Combine(pluginDirectory ?? AppContext.BaseDirectory, "cluster-library-manager.db");

        _database = new PluginDatabase(databasePath);
        _settingsStore = new PluginSettingsStore(_database);
        _profileStore = new OrganizerProfileStore(_database);
        _undoLog = new UndoLog(_database);
        _organizerService = new LibraryOrganizerService(environment.Rules, _undoLog);

        StartAutomationTimerIfEnabled();
    }

    public void RegisterCommands(INativeCommandRegistrar registrar)
    {
        registrar.OnStartup("cluster-library-manager.startup", "Cluster Library Manager Activated",
            _ => Task.FromResult<object?>("Cluster Library Manager is active for this session."));

        registrar.OnLibrary("cluster-library-manager.organize", "Organize Library",
            OrganizeSelectedBooksAsync, confirmWrites: true);

        registrar.OnLibrary("cluster-library-manager.scrape", "Scrape with ComicVine",
            ScrapeSelectedBooksAsync, confirmWrites: true);
    }

    private async Task<object?> OrganizeSelectedBooksAsync(INativePluginEnvironment environment, IReadOnlyList<Issue> books)
    {
        if (_organizerService is null || _profileStore is null)
        {
            return "Plugin not initialized.";
        }

        OrganizerProfile? profile = _profileStore.GetAll().FirstOrDefault();
        if (profile is null)
        {
            return "No organizer profile configured yet - open this plugin's settings and add one first.";
        }

        bool isInteractive = environment is INativePluginUiEnvironment;
        OrganizePlan plan = await _organizerService.PlanAsync(books, profile).ConfigureAwait(false);
        OrganizeResult result = await _organizerService.ExecuteAsync(
            plan,
            profile,
            isInteractive,
            isInteractive ? (incoming, existingPath, ct) => ShowCollisionDialogAsync(environment, incoming, existingPath, ct) : null,
            environment.CreateDbContext).ConfigureAwait(false);

        return $"Organized {result.Succeeded.Count} book(s); {result.Skipped.Count} skipped; {result.Failed.Count} failed.";
    }

    private Task<(CollisionResolution Resolution, bool ApplyToAllRemaining)> ShowCollisionDialogAsync(
        INativePluginEnvironment environment, Issue incoming, string existingPath, CancellationToken cancellationToken)
    {
        if (environment is not INativePluginUiEnvironment uiEnvironment)
        {
            return Task.FromResult((CollisionResolution.Skip, false));
        }

        return uiEnvironment.ShowModalAsync<(CollisionResolution, bool)>(resolve =>
            new FileConflictDialogView
            {
                DataContext = new FileConflictDialogViewModel(
                    $"{incoming.Series?.Name} #{incoming.Number}", Path.GetFileName(existingPath), existingPath, resolve),
            });
    }

    private async Task<object?> ScrapeSelectedBooksAsync(INativePluginEnvironment environment, IReadOnlyList<Issue> books)
    {
        if (_settingsStore is null || _database is null)
        {
            return "Plugin not initialized.";
        }

        PluginSettings settings = _settingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return "No ComicVine API key configured yet - open this plugin's settings first.";
        }

        var comicVine = new ComicVineService(settings.ApiKey);
        var matchMemory = new ComicVineMatchMemory(_database);
        var orchestrator = new ComicVineScrapeOrchestrator(comicVine, matchMemory, settings);
        bool isInteractive = environment is INativePluginUiEnvironment;

        int applied = await orchestrator.ScrapeAsync(
            books,
            isInteractive,
            isInteractive ? (label, candidates) => ShowMatchReviewDialogAsync(environment, label, candidates) : null,
            environment.CreateDbContext).ConfigureAwait(false);

        return $"Applied a ComicVine match to {applied} of {books.Count} book(s).";
    }

    private Task<ComicVineVolumeSearchResult?> ShowMatchReviewDialogAsync(
        INativePluginEnvironment environment, string bookLabel, IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)> candidates)
    {
        if (environment is not INativePluginUiEnvironment uiEnvironment)
        {
            return Task.FromResult<ComicVineVolumeSearchResult?>(null);
        }

        return uiEnvironment.ShowModalAsync<ComicVineVolumeSearchResult?>(resolve =>
            new ComicVineMatchReviewDialogView
            {
                DataContext = new ComicVineMatchReviewDialogViewModel(bookLabel, candidates, resolve),
            });
    }

    public Avalonia.Controls.Control? CreateSettingsView(INativePluginUiEnvironment environment)
    {
        if (_settingsStore is null || _profileStore is null)
        {
            return null;
        }

        var settingsViewModel = new SettingsViewModel(_settingsStore, apiKey => new ComicVineService(apiKey));
        var profilesViewModel = new ProfileManagerViewModel(_profileStore);
        var rootViewModel = new SettingsRootViewModel(settingsViewModel, profilesViewModel);
        return new SettingsRootView { DataContext = rootViewModel };
    }

    /// <summary>Self-contained automation (design doc §9) - Paperbunkr's Scheduled Tasks catalog is
    /// closed/non-extensible (verified while surveying for the implementation plan), so this plugin
    /// runs its own in-process timer instead of registering into that system. Only "organize" is wired
    /// here; automatic ComicVine scraping across the whole library on a timer needs the same
    /// isInteractive:false path <see cref="ScrapeSelectedBooksAsync"/> already supports, but iterating
    /// "every book the user hasn't scraped yet" (rather than a right-clicked selection) is real
    /// follow-up work, not built in this pass.</summary>
    private void StartAutomationTimerIfEnabled()
    {
        if (_settingsStore is null)
        {
            return;
        }

        PluginSettings settings = _settingsStore.Load();
        if (!settings.AutomaticOrganizeEnabled || !settings.AutomaticOrganizeProfileId.HasValue)
        {
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, settings.AutomaticOrganizeIntervalHours));
        _automationTimer = new Timer(_ => RunAutomaticOrganize(), null, interval, interval);
    }

    private async void RunAutomaticOrganize()
    {
        if (_environment is null || _organizerService is null || _profileStore is null || _settingsStore is null)
        {
            return;
        }

        PluginSettings settings = _settingsStore.Load();
        if (!settings.AutomaticOrganizeProfileId.HasValue)
        {
            return;
        }

        OrganizerProfile? profile = _profileStore.Get(settings.AutomaticOrganizeProfileId.Value);
        if (profile is null)
        {
            return;
        }

        try
        {
            List<Issue> books = _environment.App.GetLibraryBooks().ToList();
            OrganizePlan plan = await _organizerService.PlanAsync(books, profile).ConfigureAwait(false);
            await _organizerService.ExecuteAsync(plan, profile, isInteractive: false, interactiveResolver: null, _environment.CreateDbContext).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort background automation - a failure here shouldn't crash the host; the next
            // timer tick simply tries again.
        }
    }
}
