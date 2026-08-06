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
            // No demo/placeholder data is ever seeded (see PaperbunkrDb.EnsureCreated) - checked
            // only to decide whether to auto-open the migration overlay on a fresh install with a
            // detected CE library (docs/superpowers/specs/2026-08-06-migration-ux-design.md §B).
            bool isFreshInstall = !PaperbunkrDb.HasAnySeries();
            bool defaultCePathFound = File.Exists(MigrationViewModel.GetDefaultCePath());
            bool offerFirstRunMigration = isFreshInstall && defaultCePathFound;

            PaperbunkrDb.EnsureCreated();

            var mainViewModel = new MainViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            if (offerFirstRunMigration)
            {
                mainViewModel.OpenMigrationOverlayCommand.Execute(null);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}