using System.Collections.Generic;
using System.Linq;
using cYo.Projects.ComicRack.Engine;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Plugin package install/uninstall. A package is a zip containing a plugin's <c>plugin.xml</c> +
/// <c>.csx</c> script(s) (+ optional <c>package.ini</c>/icon) - matching ComicRackCE's own "Script
/// Archive|*.zip" install format (<c>_reference/ComicRackCE/ComicRack/Dialogs/PreferencesDialog.cs</c>
/// <c>btInstallPackage_Click</c>'s file filter) rather than inventing a Paperbunkr-specific
/// extension. <c>Package.UnzipFile</c> flattens every entry to the zip root's file name, so the zip
/// itself must be flat (no subfolders) - exactly the shape our own sample plugins already have.
///
/// Wraps <see cref="PackageManager"/> (Paperbunkr.Engine, a verbatim, previously-unwired port of
/// ComicRackCE's own <c>ComicRack.Engine.PackageManager</c>) with one deliberate simplification: CE
/// stages an install/uninstall and requires a later <c>Commit()</c> - historically gated behind an
/// app restart (<c>PreferencesDialog.NeedsRestart</c>) - because CE's Python engine and WinForms
/// shell hold process-wide state a live reload could conflict with. Paperbunkr's
/// <c>PluginEngine.Discover</c> is a cheap, side-effect-free rescan of Roslyn script *text* with no
/// loaded-assembly/file-lock concerns (already re-run on demand by tests), so this service commits
/// synchronously and the caller (<see cref="PluginScreenViewModel"/>) re-discovers immediately after
/// - no restart, no "pending" state ever visible in the UI.
/// </summary>
public sealed class PluginPackageService
{
    private readonly string _rootDirectory;
    private readonly string _stagingDirectory;
    private PackageManager? _manager;

    public PluginPackageService() : this(PluginPaths.RootDirectory, PluginPaths.StagingDirectory)
    {
    }

    /// <summary>Test seam - points install/uninstall at an isolated folder pair instead of the real %AppData% plugin locations.</summary>
    public PluginPackageService(string rootDirectory, string stagingDirectory)
    {
        _rootDirectory = rootDirectory;
        _stagingDirectory = stagingDirectory;
    }

    /// <summary>
    /// Deferred: <see cref="PackageManager"/>'s constructor eagerly creates both of its directories
    /// on disk (its <c>PackagePath</c>/<c>PendingPackagePath</c> setters). Every
    /// <see cref="PluginScreenViewModel"/> construction builds one of these services, including in
    /// ViewModel tests that never actually list/install a package - lazy so those never touch the
    /// real <c>%AppData%\Paperbunkr\plugins</c> folder just by existing.
    /// </summary>
    private PackageManager Manager => _manager ??= new PackageManager(_rootDirectory, _stagingDirectory, commit: true);

    public IReadOnlyList<PackageManager.Package> GetPackages() =>
        Manager.GetPackages().OrderBy(p => p.Name).ToList();

    /// <summary>True when a package with the same (package.ini-or-filename-derived) name is already installed - the signal for the caller to confirm an overwrite first, matching CE's own "A Script Package with the same name already exists!" prompt.</summary>
    public bool PackageFileExists(string zipFile) => Manager.PackageFileExists(zipFile);

    /// <summary>Installs (or overwrites, if a same-named package already exists) <paramref name="zipFile"/> and commits immediately. Returns false for an unreadable/invalid zip.</summary>
    public bool Install(string zipFile)
    {
        if (!Manager.Install(zipFile))
        {
            return false;
        }

        Manager.Commit();
        return true;
    }

    /// <summary>Removes an installed package's folder and commits immediately.</summary>
    public void Uninstall(PackageManager.Package package)
    {
        Manager.Uninstall(package);
        Manager.Commit();
    }
}
