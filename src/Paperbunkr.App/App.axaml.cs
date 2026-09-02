using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;

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
            DiagnosticsService.LogMilestone("Checking database integrity...");

            // Corruption safeguard (docs/superpowers/specs/2026-08-29-db-corruption-safeguards-
            // design.md §3) - runs before HasAnySeries()/EnsureCreated() ever touch the live file,
            // so a genuinely corrupt database is caught before EF/migrations attempt to open it
            // and crash. CheckIntegrity() itself returns true (nothing to check) on a fresh install.
            if (!DatabaseIntegrityService.CheckIntegrity(out string? integrityDetail))
            {
                DiagnosticsService.LogMilestone($"Database integrity check failed: {integrityDetail}");
                if (!HandleDatabaseRecovery(desktop, integrityDetail))
                {
                    return;
                }
            }

            DiagnosticsService.LogMilestone("Checking for an existing library...");

            // No demo/placeholder data is ever seeded (see PaperbunkrDb.EnsureCreated) - HasAnySeries
            // applies pending migrations itself (see its own doc comment), so this is also the first
            // point a stuck/broken migration would surface. No longer used to decide whether to show
            // onboarding (docs/superpowers/specs/2026-08-31-first-run-onboarding-design.md) - that's
            // now gated on AppSettings.WelcomeScreenShown below, independent of library contents, so
            // a user who skips or imports zero comics never sees it re-trigger on a later launch.
            try
            {
                PaperbunkrDb.HasAnySeries();
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogCrash("Database migration/open (HasAnySeries)", ex, isTerminating: true);
                throw;
            }

            // Still detected the same way, now just badges the welcome screen's CE card instead of
            // driving an auto-launch decision.
            bool ceInstallDetected = File.Exists(MigrationViewModel.GetDefaultCePath());

            DiagnosticsService.LogMilestone("Applying pending database migrations...");
            try
            {
                PaperbunkrDb.EnsureCreated();
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogCrash("Database migration/open (EnsureCreated)", ex, isTerminating: true);
                throw;
            }

            // Auto-backup startup trigger (spec §2) - fire-and-forget on a background thread so a
            // checkpoint+file-copy never adds to startup latency. This is the fallback trigger; the
            // primary one fires on clean shutdown below. RunAutoBackupIfDue() is itself gated by
            // AutoBackupEnabled and the min-interval de-dupe guard, and swallows its own failures.
            System.Threading.Tasks.Task.Run(() => new BackupService().RunAutoBackupIfDue());

            // Publisher content-type sweep (docs/superpowers/specs/2026-08-30-publisher-content-
            // type-classification-design.md) - same fire-and-forget, non-blocking shape as the
            // auto-backup trigger above. RunContentTypeSweepIfDue() is itself gated by the 7-day
            // interval check and swallows its own failures.
            System.Threading.Tasks.Task.Run(() => new LibraryFolderScanner().RunContentTypeSweepIfDue());

            DiagnosticsService.LogMilestone("Database ready. Applying skin/theme...");
            new SkinService().ApplyPersistedSettings();

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
            var mainViewModel = new MainViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
            desktop.MainWindow = mainWindow;

            // App shell navigation history (docs/superpowers/specs/2026-08-30-app-shell-navigation-
            // history-design.md) - a CLI deep link takes priority over restoring the prior session's
            // last screen; on a fresh install (offerFirstRunMigration below) there's nothing to
            // restore yet, so RestoreLastScreen's own "no usable last screen" fallback to Home
            // covers that case too, no separate branch needed here.
            if (NavigationCliArgs.TryParseOpenArg(desktop.Args ?? Array.Empty<string>(), out var deepLinkTarget) && deepLinkTarget is not null)
            {
                mainViewModel.OpenDeepLink(deepLinkTarget);
            }
            else
            {
                mainViewModel.RestoreLastScreen();
            }

            bool welcomeOverlayOpened;
            using (var welcomeSettingsContext = PaperbunkrDb.CreateContext())
            {
                welcomeOverlayOpened = !welcomeSettingsContext.GetOrCreateAppSettings().WelcomeScreenShown;
                if (welcomeOverlayOpened)
                {
                    mainViewModel.OpenWelcomeOverlayCommand.Execute(ceInstallDetected);
                }
            }

            // Auto-update (docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md) -
            // skipped the same launch the welcome overlay opens, so the two first-look modals never
            // stack. Fire-and-forget: this only ever opens the ask-before-download prompt: no download
            // happens without the user clicking Download in it.
            //
            // Real bug, found via manual testing + FreezeWatchdog crash logs (%AppData%\Paperbunkr\
            // logs\): calling this directly here let its first `await` capture Avalonia's UI-thread
            // SynchronizationContext, so anything NetSparkle's SparkleUpdater.CheckForUpdatesQuietly()
            // does synchronously under the hood before yielding (DNS/HTTP setup, etc.) ran ON the UI
            // thread and could stall it past the watchdog's 10s threshold - reproduced consistently,
            // freeze landing within ~1s of "Startup complete." every launch. Task.Run moves the entire
            // call (including its first synchronous slice) onto a threadpool thread with no captured
            // UI SynchronizationContext, so nothing NetSparkle does internally can block the UI thread
            // regardless of how well-behaved its own async plumbing is. See
            // CheckForUpdatesOnStartupAsync's own doc comment for the matching fix on its UI-touching
            // tail (Update.Show/IsUpdateAvailableOverlayOpen), needed now that this method may run
            // entirely off the UI thread.
            if (!welcomeOverlayOpened)
            {
                _ = Task.Run(mainViewModel.CheckForUpdatesOnStartupAsync);
            }

            // App chrome (docs/superpowers/specs/2026-08-23-app-chrome-crash-reporter-and-tray-
            // design.md §3) - started once the UI thread is actually pumping, since the watchdog's
            // heartbeat ping needs a live Dispatcher to answer it.
            new FreezeWatchdogService().Start();

            // Plugin API v2 (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §2) -
            // discovers/precompiles plugins and fires the Startup hook. Runs after the main window
            // exists (adapters need MainWindow/MainViewModel.Reader to be real).
            var pluginHost = new PluginHostService();
            pluginHost.Initialize(mainViewModel, mainWindow);
            mainViewModel.Plugin.AttachHost(pluginHost);
            mainViewModel.Library.AttachHost(pluginHost);
            desktop.Exit += (_, _) => pluginHost.Shutdown();

            // Auto-backup shutdown trigger (spec §2) - the primary trigger, since it also catches
            // sessions left open all day that never restart. Synchronous and best-effort: a normal
            // checkpoint+file-copy is fast enough not to perceptibly delay exit, and
            // RunAutoBackupIfDue() swallows its own failures rather than blocking shutdown on one.
            desktop.Exit += (_, _) => new BackupService().RunAutoBackupIfDue();

            DiagnosticsService.LogMilestone("Startup complete.");
        }

        base.OnFrameworkInitializationCompleted();
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
                backupService.RestoreBackup(selectedBackupPath);
                RelaunchAndExit();
                return false;

            case DatabaseRecoveryOutcome.StartFresh:
                DiagnosticsService.LogMilestone("Starting fresh library - corrupt database renamed aside.");
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