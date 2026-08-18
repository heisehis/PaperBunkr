using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;

namespace Paperbunkr.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
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

            DiagnosticsService.LogMilestone("Building main window...");
            var mainViewModel = new MainViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            if (offerFirstRunMigration)
            {
                mainViewModel.OpenMigrationOverlayCommand.Execute(null);
            }

            DiagnosticsService.LogMilestone("Startup complete.");
        }

        base.OnFrameworkInitializationCompleted();
    }
}