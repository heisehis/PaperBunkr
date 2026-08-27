using System;
using System.IO;
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
            DiagnosticsService.LogMilestone("Checking for an existing library...");

            // No demo/placeholder data is ever seeded (see PaperbunkrDb.EnsureCreated) - checked
            // only to decide whether to auto-open the migration overlay on a fresh install with a
            // detected CE library (docs/superpowers/specs/2026-08-06-migration-ux-design.md §B).
            // HasAnySeries applies pending migrations itself (see its own doc comment), so this is
            // also the first point a stuck/broken migration would surface.
            bool isFreshInstall;
            try
            {
                isFreshInstall = !PaperbunkrDb.HasAnySeries();
            }
            catch (Exception ex)
            {
                DiagnosticsService.LogCrash("Database migration/open (HasAnySeries)", ex, isTerminating: true);
                throw;
            }

            bool defaultCePathFound = File.Exists(MigrationViewModel.GetDefaultCePath());
            bool offerFirstRunMigration = isFreshInstall && defaultCePathFound;

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

            if (offerFirstRunMigration)
            {
                mainViewModel.OpenMigrationOverlayCommand.Execute(null);
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

            DiagnosticsService.LogMilestone("Startup complete.");
        }

        base.OnFrameworkInitializationCompleted();
    }
}