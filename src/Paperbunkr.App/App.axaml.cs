using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Wrap the trace sink installed by Program.BuildAvaloniaApp's .LogToTrace() so Avalonia's
        // own platform/rendering log events (GPU init failure, software fallback) also land in
        // startup.log (docs/superpowers/specs/2026-08-27-hardware-accelerated-rendering-design.md
        // §5). Runs before the compositor creates its GPU context, so the fallback messages are
        // captured. Idempotent.
        CompositeLogSink.EnsureRenderCaptureInstalled();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Splash-first async startup (docs/superpowers/specs/2026-09-09-startup-onboarding-
            // whats-new-design.md, Decision 2). The heavy DB work runs off the UI thread while the
            // borderless SplashWindow shows step-based progress; MainWindow is built and shown when
            // it's done, then the splash fades out. Kicked off as a Task here (this method itself
            // stays synchronous so it can call base normally); ShutdownMode.OnLastWindowClose keeps
            // the app alive while only the splash is open. A fatal DB failure is routed to
            // DiagnosticsService.ReportFatalStartupError (the strict no-Continue crash dialog) since
            // a bare throw from the async body would only reach the log-only unobserved-task path.
            _ = RunDesktopStartupAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task RunDesktopStartupAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        const int TotalPhases = 5;

        var splashViewModel = new SplashViewModel();
        var splash = new SplashWindow { DataContext = splashViewModel };
        splash.Show();
        var splashShownAtUtc = DateTime.UtcNow;

        try
        {
            await RunStartupSequenceAsync(desktop, splash, splashViewModel, splashShownAtUtc, TotalPhases);
        }
        catch (Exception ex)
        {
            // This body is a fire-and-forget Task, so an unhandled throw here would only reach the
            // log-only TaskScheduler.UnobservedTaskException path. Route anything unexpected during
            // startup to the strict no-Continue crash dialog instead, matching the synchronous
            // OnFrameworkInitializationCompleted this replaced.
            DiagnosticsService.ReportFatalStartupError("Startup sequence", ex);
        }
    }

    private async Task RunStartupSequenceAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splash,
        SplashViewModel splashViewModel,
        DateTime splashShownAtUtc,
        int TotalPhases)
    {
        DiagnosticsService.LogMilestone("Checking database integrity...");
        splashViewModel.ReportPhase(0, TotalPhases, "Checking database…");

        // Corruption safeguard (docs/superpowers/specs/2026-08-29-db-corruption-safeguards-
        // design.md §3) - runs before HasAnySeries()/EnsureCreated() ever touch the live file, so a
        // genuinely corrupt database is caught before EF/migrations attempt to open it and crash.
        // CheckIntegrity() itself returns true (nothing to check) on a fresh install. Run on a
        // background thread - a PRAGMA integrity_check on a multi-GB library is one of the slowest
        // parts of cold start.
        string? integrityDetail = null;
        bool integrityOk = await Task.Run(() => DatabaseIntegrityService.CheckIntegrity(out integrityDetail));
        if (!integrityOk)
        {
            DiagnosticsService.LogMilestone($"Database integrity check failed: {integrityDetail}");
            // DatabaseRecoveryWindow is a blocking modal on the UI thread (nested DispatcherFrame);
            // the splash sits behind it, still animating. Restore/Quit terminate the process inside
            // the call; only Start Fresh returns true and lets startup continue.
            if (!HandleDatabaseRecovery(desktop, integrityDetail))
            {
                splash.Close();
                return;
            }
        }

        DiagnosticsService.LogMilestone("Applying pending database migrations...");
        splashViewModel.ReportPhase(1, TotalPhases, "Applying database migrations…");

        // No demo/placeholder data is ever seeded (see PaperbunkrDb.EnsureCreated) - HasAnySeries
        // applies pending migrations itself (see its own doc comment), so this is the first point a
        // stuck/broken migration surfaces; EnsureCreated finishes the job. No longer used to decide
        // whether to show onboarding (docs/superpowers/specs/2026-08-31-first-run-onboarding-
        // design.md) - that's gated on AppSettings.WelcomeScreenShown below. A failure here is
        // fatal: the app cannot run without its database.
        try
        {
            await Task.Run(() =>
            {
                PaperbunkrDb.HasAnySeries();
                PaperbunkrDb.EnsureCreated();
            });
        }
        catch (Exception ex)
        {
            DiagnosticsService.ReportFatalStartupError("Database migration/open", ex);
            return;
        }

        // Still detected the same way, now just badges the welcome screen's CE card instead of
        // driving an auto-launch decision.
        bool ceInstallDetected = File.Exists(MigrationViewModel.GetDefaultCePath());

        // Panorama's cover aspect-ratio store persists progressively-learned ratios via a
        // debounced write-back - wire its DB access now that migrations have run. Left unset
        // under test so a stray Report can never reach the real per-user database.
        CoverAspectRatioStore.ContextFactory = PaperbunkrDb.CreateContext;

        // Auto-backup, the content-type sweep, and the old "scan folders on startup" behaviour
        // are now catalog tasks driven by the scheduler (docs/superpowers/specs/2026-09-06-
        // scheduled-tasks-and-cover-durability-design.md, Part 1) - its startup pass runs
        // anything overdue. The unconditional backup-on-clean-shutdown call below stays as a
        // belt-and-suspenders (its own 4h floor prevents thrash).

        // Activity Center history prune (docs/superpowers/specs/2026-09-03-activity-center-
        // design.md) - same fire-and-forget, self-swallowing shape as the two triggers above.
        System.Threading.Tasks.Task.Run(ActivityHistoryStore.PruneOnStartup);

        DiagnosticsService.LogMilestone("Database ready. Applying skin/theme...");
        splashViewModel.ReportPhase(2, TotalPhases, "Loading appearance…");
        var skinService = new SkinService();
        skinService.ApplyPersistedSettings();

        // Reconcile the pre-UI graphics.json cache to the now-readable AppSettings source of
        // truth (docs/superpowers/specs/2026-08-27-hardware-accelerated-rendering-design.md
        // §2). Takes effect next launch - the rendering backend is already chosen for this one.
        try
        {
            using var settingsContext = PaperbunkrDb.CreateContext();
            var appSettings = settingsContext.GetOrCreateAppSettings();
            if (GraphicsBootstrap.SyncCache(appSettings.RenderingBackend, appSettings.PreferNativeOpenGl))
            {
                DiagnosticsService.LogMilestone(
                    $"graphics.json synced to settings: {appSettings.RenderingBackend} preferNativeOpenGl={appSettings.PreferNativeOpenGl} (restart to apply)");
            }
        }
        catch (Exception ex)
        {
            // Bootstrap already succeeded from the cache, so rendering is unaffected - just
            // note it and move on.
            DiagnosticsService.LogMilestone($"graphics.json sync skipped: {ex.GetType().Name} {ex.Message}");
        }

        DiagnosticsService.LogMilestone("Building main window...");
        splashViewModel.ReportPhase(3, TotalPhases, "Getting things ready…");

        // docs/superpowers/specs/2026-09-04-navigation-transition-system-design.md - real
        // shared-element flight duration/easing come from the same App.axaml resources every
        // XAML-declared transition uses, read live at flight time (not once at startup) so a
        // runtime resource change (skin reload) is picked up.
        var transitionCoordinator = new NavigationTransitionCoordinator(
            SharedElementTransitionService.Shared,
            isReducedMotion: skinService.GetReducedMotion,
            flightDuration: () => (TimeSpan)(Application.Current!.Resources["PbMotionLarge"] ?? TimeSpan.FromMilliseconds(320)),
            easing: new CubicEaseOut());
        var mainViewModel = new MainViewModel(transitionCoordinator.RunAsync);
        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel,
        };
        desktop.MainWindow = mainWindow;

        // App shell navigation history (docs/superpowers/specs/2026-08-30-app-shell-navigation-
        // history-design.md) - a CLI deep link takes priority over restoring the prior session's
        // last screen; on a fresh install (offerFirstRunMigration below) there's nothing to
        // restore yet, so RestoreLastScreen's own "no usable last screen" fallback to Home
        // covers that case too, no separate branch needed here. RestoreLastScreen itself honours
        // AppSettings.RestoreSessionOnStartup (docs/superpowers/specs/2026-09-04-behavior-
        // settings-batch2-design.md §3.1) - off means it just goes Home.
        if (NavigationCliArgs.TryParseOpenArg(desktop.Args ?? Array.Empty<string>(), out var deepLinkTarget) && deepLinkTarget is not null)
        {
            mainViewModel.OpenDeepLink(deepLinkTarget);
        }
        else
        {
            mainViewModel.RestoreLastScreen();
        }

        // Show the main window, then hold the splash to its 400ms floor and fade it out over it
        // (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-design.md, Decision 2).
        splashViewModel.ReportPhase(4, TotalPhases, "Almost there…");
        mainWindow.Show();
        await splashViewModel.EnforceMinimumVisibleAsync(splashShownAtUtc);
        await splash.FadeOutAndCloseAsync();

        // Startup first-look modal (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-
        // design.md, Decision 7) - mutually exclusive, so two never stack:
        //   fresh install     -> welcome overlay (+ tour), and PersistLastRunVersion so the *next*
        //                        update shows What's New. Update check is skipped this launch.
        //   version bumped    -> "What's New" overlay, covering every release since the last run.
        //   no version change -> the normal ask-before-download update check.
        // The latter two run inside ShowWhatsNewOrCheckForUpdatesAsync, dispatched via Task.Run for
        // the same reason CheckForUpdatesOnStartupAsync always was (its own doc comment): keeping
        // NetSparkle's synchronous first slice off the UI thread so the FreezeWatchdog can't trip.
        bool welcomeOverlayOpened;
        using (var welcomeSettingsContext = PaperbunkrDb.CreateContext())
        {
            welcomeOverlayOpened = !welcomeSettingsContext.GetOrCreateAppSettings().WelcomeScreenShown;
        }

        if (welcomeOverlayOpened)
        {
            mainViewModel.OpenWelcomeOverlayCommand.Execute(ceInstallDetected);
            mainViewModel.PersistLastRunVersion();
        }
        else
        {
            _ = Task.Run(mainViewModel.ShowWhatsNewOrCheckForUpdatesAsync);
        }

        // App chrome (docs/superpowers/specs/2026-08-23-app-chrome-crash-reporter-and-tray-
        // design.md §3) - started once the UI thread is actually pumping, since the watchdog's
        // heartbeat ping needs a live Dispatcher to answer it.
        new FreezeWatchdogService().Start();

        // Plugin API v2 (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §2) -
        // discovers/precompiles plugins and fires the Startup hook. Runs after the main window
        // exists (adapters need MainWindow/MainViewModel.Reader to be real) and now after it is
        // shown, so plugin precompile janks a visible window rather than delaying its appearance.
        var pluginHost = new PluginHostService();
        pluginHost.Initialize(mainViewModel, mainWindow);
        mainViewModel.Plugin.AttachHost(pluginHost);
        mainViewModel.Library.AttachHost(pluginHost);
        mainViewModel.Books.AttachHost(pluginHost);
        mainViewModel.Smart.AttachHost(pluginHost);
        mainViewModel.IssueProperties.AttachHost(pluginHost);
        mainViewModel.BulkIssueProperties.AttachHost(pluginHost);
        mainViewModel.Detail.Tabs.AttachHost(pluginHost);
        mainViewModel.MangaDetail.Tabs.AttachHost(pluginHost);
        mainViewModel.QuickOpen.AttachHost(pluginHost);
        // ParseComicPath (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-
        // plan.md §7) - LibraryFolderScanner has no natural AttachHost point (see its own
        // PluginHost doc comment), so this is a settable static instead.
        LibraryFolderScanner.PluginHost = pluginHost;
        // Proactive scan alerts (docs/superpowers/specs/2026-09-05-plugin-grouped-review-and-
        // scan-alerts-design.md §4) - same "no natural attach point" reasoning as PluginHost above.
        LibraryFolderScanner.ScanAlertService = new PluginScanAlertService(pluginHost, mainViewModel.Activity);
        // DrawThumbnailOverlay (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-
        // hooks-plan.md §12) - AsyncPluginOverlayImage is a static attached-property helper,
        // same "no natural AttachHost point" reasoning as LibraryFolderScanner above.
        Views.AsyncPluginOverlayImage.PluginHost = pluginHost;
        // BookOpened/ReaderResized (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-
        // hooks-plan.md §1/§2) are plain event forwards, not AttachHost - ReaderScreenViewModel
        // never needs to call the host itself for these two.
        mainViewModel.Reader.IssueOpened += issue => _ = pluginHost.RunBookOpenedHookAsync(issue);
        mainViewModel.Reader.CanvasResized += (width, height) => _ = pluginHost.RunReaderResizedHookAsync(width, height);
        desktop.Exit += (_, _) => pluginHost.Shutdown();

        // Maintenance scheduler (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-
        // durability-design.md, Part 1) - seeds task state, runs a startup pass for anything
        // overdue, then a 15-minute in-session tick. Started after the plugin host so a plugin
        // can't miss an early scheduled job's hooks.
        mainViewModel.Scheduler.Start();
        desktop.Exit += (_, _) => mainViewModel.Scheduler.Stop();

        // Auto-backup shutdown trigger (spec §2) - the primary trigger, since it also catches
        // sessions left open all day that never restart. Synchronous and best-effort: a normal
        // checkpoint+file-copy is fast enough not to perceptibly delay exit, and
        // RunAutoBackupIfDue() swallows its own failures rather than blocking shutdown on one.
        desktop.Exit += (_, _) => new BackupService().RunAutoBackupIfDue();

        DiagnosticsService.LogMilestone("Startup complete.");
    }

    /// <summary>
    /// Shows <see cref="DatabaseRecoveryWindow"/> and acts on the user's choice (spec §3). Restore
    /// and Quit both terminate this process (Restore relaunches first) and never return; only
    /// Start Fresh returns, so the caller can fall through to the normal fresh-install flow that
    /// already runs when <c>HasAnySeries()</c> finds an empty/nonexistent database.
    /// </summary>
    private static bool HandleDatabaseRecovery(IClassicDesktopStyleApplicationLifetime desktop, string? detail)
    {
        var backupService = new BackupService();
        var (outcome, selectedBackupPath) = DatabaseRecoveryWindow.ShowModal(detail, backupService.GetAvailableBackups());

        switch (outcome)
        {
            case DatabaseRecoveryOutcome.Restore when selectedBackupPath is not null:
                DiagnosticsService.LogMilestone($"Restoring database from backup: {selectedBackupPath}");
                // The restored DB may carry different entity ids - defer a cover-cache purge to the
                // relaunched process (can't safely attic mid-relaunch).
                Services.Covers.CoverCacheMaintenance.DeferRebuildPurge();
                backupService.RestoreBackup(selectedBackupPath);
                RelaunchAndExit();
                return false;

            case DatabaseRecoveryOutcome.StartFresh:
                DiagnosticsService.LogMilestone("Starting fresh library - corrupt database renamed aside.");
                Services.Covers.CoverCacheMaintenance.DeferRebuildPurge();
                QuarantineCorruptDatabase();
                return true;

            default:
                DiagnosticsService.LogMilestone("User chose to quit after a database integrity failure.");
                Environment.Exit(0);
                return false;
        }
    }

    /// <summary>Same relaunch mechanism as <c>DiagnosticsService.ActOnCrashOutcome</c>'s Restart outcome - a new process, then this one exits.</summary>
    private static void RelaunchAndExit()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (exePath is not null)
            {
                System.Diagnostics.Process.Start(exePath);
            }
        }
        catch
        {
        }

        Environment.Exit(0);
    }

    /// <summary>
    /// Renames the corrupt database (and its WAL sidecars, if present) aside rather than deleting -
    /// never destroy the one artifact that might let someone hand-recover data from it later
    /// (spec §3). The normal fresh-install flow then creates a brand-new file at the original path.
    /// </summary>
    private static void QuarantineCorruptDatabase()
    {
        string dbPath = Paperbunkr.Data.PaperbunkrDbContext.GetDefaultDatabasePath();
        string suffix = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Move(path, $"{path}.corrupt-{suffix}");
            }
        }
    }
}