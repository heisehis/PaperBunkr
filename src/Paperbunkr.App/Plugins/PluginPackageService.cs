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
/// ComicRackCE's own <c>ComicRack.Engine.PackageManager</c>). CE stages an install/uninstall and
/// requires a later <c>Commit()</c> - historically gated behind an app restart
/// (<c>PreferencesDialog.NeedsRestart</c>) - because CE's Python engine and WinForms shell hold
/// process-wide state a live reload could conflict with.
///
/// For a `Script`-tier package (still the default, and everything this comment described before
/// Plugin API v4 existed), that concern doesn't apply: <c>PluginEngine.Discover</c> is a cheap,
/// side-effect-free rescan of Roslyn script *text* with no loaded-assembly/file-lock concerns, so
/// this service commits synchronously and the caller (<see cref="PluginScreenViewModel"/>)
/// re-discovers immediately - no restart, no "pending" state visible in the UI.
///
/// A `Native`-tier package (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md §4)
/// is exactly the case CE's own restart gate existed for - a loaded, locked
/// <c>AssemblyLoadContext</c> assembly - so <see cref="Install"/>/<see cref="Uninstall"/> defer to a
/// pending state for it instead, applied only via <see cref="ApplyPendingChanges"/> at the next app
/// launch.
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

    /// <summary>
    /// Installs (or overwrites, if a same-named package already exists) <paramref name="zipFile"/>.
    /// A `Script`-tier package still commits immediately, exactly as before this method learned about
    /// tiers at all. A `Native`-tier package stays pending instead (docs/superpowers/specs/2026-09-11-
    /// plugin-api-v4-native-tier-design.md §4, implementation plan Phase 2 Step 2.2, resolving grilling
    /// Q22=B) - a loaded native assembly has exactly the process-wide-state/file-lock concern CE's own
    /// Python engine had, which is why CE gated installs behind a restart in the first place (see this
    /// class's own doc comment); Script-tier commands never had that problem since they're just
    /// re-parsed Roslyn text, so their immediate-commit behavior is unaffected. Returns false for an
    /// unreadable/invalid zip either way.
    /// </summary>
    public bool Install(string zipFile)
    {
        if (!Manager.Install(zipFile))
        {
            return false;
        }

        if (IsPendingNativeTier(zipFile))
        {
            return true;
        }

        Manager.Commit();
        return true;
    }

    /// <summary>
    /// Removes a package. A `Script`-tier package commits immediately, as before. A `Native`-tier
    /// package is only marked for removal (CE's own `.remove`-sentinel/`PendingInstall`-folder-delete
    /// mechanism, already exactly what <see cref="PackageManager.Uninstall"/> does) and takes effect
    /// at the next <see cref="ApplyPendingChanges"/> call (app restart) instead, for the same reason
    /// <see cref="Install"/> defers a Native-tier commit.
    /// </summary>
    public void Uninstall(PackageManager.Package package)
    {
        Manager.Uninstall(package);
        if (!package.IsNativeTier)
        {
            Manager.Commit();
        }
    }

    /// <summary>Applies whatever stayed pending after a Native-tier install/uninstall (v4 §4's
    /// restart-to-apply model) - call once at app startup, before <c>PluginEngine.Discover</c>. A
    /// no-op when nothing is pending, which is the common case: every Script-tier package already
    /// self-committed at install time, so this only ever has real work to do right after a Native-tier
    /// change from the previous session.</summary>
    public void ApplyPendingChanges() => Manager.Commit();

    private bool IsPendingNativeTier(string zipFile)
    {
        string? name = PackageManager.Package.GetName(zipFile);
        return Manager.GetPackages().Any(p =>
            p.Name == name && p.PackageType == PackageManager.PackageType.PendingInstall && p.IsNativeTier);
    }
}
