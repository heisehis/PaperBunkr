using System.IO.Compression;
using cYo.Projects.ComicRack.Engine;
using Paperbunkr.App.Plugins;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Verifies the tier-conditional packaging fix (docs/superpowers/specs/2026-09-11-plugin-api-v4-
/// native-tier-design.md §4, implementation plan Phase 2 Step 2.1): a `Native`-tier package must keep
/// its subfolder structure through BOTH extraction (<c>PackageManager.Package.UnzipFile</c>) AND
/// commit (<c>PackageManager.CommitInstallPackage</c>) - fixing only one of the two would still
/// silently break a native dependency (e.g. SQLite's <c>e_sqlite3.dll</c> under
/// `runtimes/&lt;rid&gt;/native/`) resolvable via <c>AssemblyDependencyResolver</c>. A `Script`-tier
/// package (the existing, default behavior - <see cref="PluginPackageServiceTests"/> already proves
/// this end to end against a real shipped plugin) must stay exactly as flattened as before.
/// </summary>
public sealed class PluginPackageServiceNativeTierTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _rootDirectory;
    private readonly string _stagingDirectory;

    public PluginPackageServiceNativeTierTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_native_tier_test_{Guid.NewGuid():N}");
        _rootDirectory = Path.Combine(_testRoot, "plugins");
        _stagingDirectory = Path.Combine(_testRoot, "plugin-staging");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private string BuildZip(string tierAttribute, IReadOnlyDictionary<string, string> entries, string? version = null)
    {
        string zipPath = Path.Combine(_testRoot, $"{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(_testRoot);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        string versionAttribute = version is null ? string.Empty : $" version=\"{version}\"";
        string manifest =
            $"""
             <Plugin key="fixture" name="Fixture"{tierAttribute}{versionAttribute}>
             </Plugin>
             """;
        WriteEntry(archive, "plugin.xml", manifest);

        foreach (var (path, content) in entries)
        {
            WriteEntry(archive, path, content);
        }

        return zipPath;
    }

    private static void WriteEntry(ZipArchive archive, string entryPath, string content)
    {
        var entry = archive.CreateEntry(entryPath);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    [Fact]
    public void Script_tier_package_stays_flattened_through_install_and_commit()
    {
        string zipPath = BuildZip(
            tierAttribute: string.Empty,
            entries: new Dictionary<string, string>
            {
                ["startup.csx"] = "return \"ok\";",
                ["runtimes/win-x64/native/fake.dll"] = "not a real dll",
            });

        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);
        bool installed = service.Install(zipPath);

        Assert.True(installed);
        var package = Assert.Single(service.GetPackages());
        string installedDir = Path.Combine(_rootDirectory, package.Name);

        // Flattened: the nested entry's file collides down to the bare filename at the root,
        // exactly as PackageManager.Package.UnzipFile's default (preserveStructure: false) behavior
        // always has - unaffected by the Native-tier fix.
        Assert.True(File.Exists(Path.Combine(installedDir, "fake.dll")));
        Assert.False(Directory.Exists(Path.Combine(installedDir, "runtimes")));
    }

    [Fact]
    public void Native_tier_package_preserves_subfolder_structure_through_install_and_commit()
    {
        string zipPath = BuildZip(
            tierAttribute: " tier=\"Native\" assembly=\"MyPlugin.dll\"",
            entries: new Dictionary<string, string>
            {
                ["MyPlugin.dll"] = "not a real dll",
                ["runtimes/win-x64/native/e_sqlite3.dll"] = "not a real native dll",
            });

        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);
        bool installed = service.Install(zipPath);
        Assert.True(installed);

        // Native-tier installs stay pending until ApplyPendingChanges (Step 2.2, verified separately
        // by Native_tier_install_stays_pending_until_ApplyPendingChanges) - apply here since this
        // test's actual concern is structure preservation through BOTH extraction and commit, not the
        // staging behavior itself.
        service.ApplyPendingChanges();

        var package = Assert.Single(service.GetPackages());
        Assert.True(package.Installed);
        string installedDir = Path.Combine(_rootDirectory, package.Name);

        // Preserved: this is exactly the path AssemblyDependencyResolver.ResolveUnmanagedDllToPath
        // looks up via the plugin's own .deps.json - flattening this would silently break native
        // dependency resolution at plugin load time.
        string nativeDllPath = Path.Combine(installedDir, "runtimes", "win-x64", "native", "e_sqlite3.dll");
        Assert.True(File.Exists(nativeDllPath), $"Expected preserved nested path at {nativeDllPath}");
        Assert.True(File.Exists(Path.Combine(installedDir, "MyPlugin.dll")));
    }

    [Fact]
    public void Script_tier_install_commits_immediately_no_restart_needed()
    {
        string zipPath = BuildZip(tierAttribute: string.Empty, entries: new Dictionary<string, string> { ["startup.csx"] = "return 1;" });
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);

        service.Install(zipPath);

        var package = Assert.Single(service.GetPackages());
        Assert.True(package.Installed);
        Assert.Equal(PackageManager.PackageType.Installed, package.PackageType);
    }

    [Fact]
    public void Native_tier_install_stays_pending_until_ApplyPendingChanges_is_called()
    {
        string zipPath = BuildZip(
            tierAttribute: " tier=\"Native\" assembly=\"MyPlugin.dll\"",
            entries: new Dictionary<string, string> { ["MyPlugin.dll"] = "not a real dll" });
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);

        service.Install(zipPath);

        var pending = Assert.Single(service.GetPackages());
        Assert.Equal(PackageManager.PackageType.PendingInstall, pending.PackageType);
        // "Installed" per the Package model's own definition means "will be usable after Commit" -
        // it's the Plugin screen's job (Step 2.3) to show a distinct "restart to finish installing"
        // state rather than treating this the same as an immediately-usable Script-tier install.
        Assert.True(pending.Installed);

        service.ApplyPendingChanges();

        var applied = Assert.Single(service.GetPackages());
        Assert.Equal(PackageManager.PackageType.Installed, applied.PackageType);
        string installedDllPath = Path.Combine(_rootDirectory, applied.Name, "MyPlugin.dll");
        Assert.True(File.Exists(installedDllPath));
    }

    [Fact]
    public void Native_tier_uninstall_of_an_installed_package_stays_pending_until_ApplyPendingChanges()
    {
        string zipPath = BuildZip(
            tierAttribute: " tier=\"Native\" assembly=\"MyPlugin.dll\"",
            entries: new Dictionary<string, string> { ["MyPlugin.dll"] = "not a real dll" });
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);
        service.Install(zipPath);
        service.ApplyPendingChanges();
        var installed = Assert.Single(service.GetPackages());

        service.Uninstall(installed);

        // PendingRemove, not Installed - Package.CreateFromPath correctly distinguishes "marked via
        // .remove sentinel but the folder still physically exists" from a plain Installed package.
        // The folder itself isn't touched yet; only Commit()'s CommitUninstallPackage step deletes it.
        var stillPresent = Assert.Single(service.GetPackages());
        Assert.Equal(PackageManager.PackageType.PendingRemove, stillPresent.PackageType);
        Assert.True(Directory.Exists(Path.Combine(_rootDirectory, installed.Name)));

        service.ApplyPendingChanges();

        Assert.Empty(service.GetPackages());
    }

    [Fact]
    public void Package_Version_reads_plugin_xmls_own_version_attribute()
    {
        string zipPath = BuildZip(
            tierAttribute: " tier=\"Native\" assembly=\"MyPlugin.dll\"",
            entries: new Dictionary<string, string> { ["MyPlugin.dll"] = "not a real dll" },
            version: "1.2.3");
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);
        service.Install(zipPath);
        service.ApplyPendingChanges();

        var installed = Assert.Single(service.GetPackages());
        Assert.Equal("1.2.3", installed.Version);
    }

    [Fact]
    public void Installing_a_newer_same_name_native_package_updates_cleanly_with_no_manual_remove_call()
    {
        // The realistic case (a plugin's own display name is stable across its own version bumps):
        // both the old and new package resolve to the same final install path by construction, so
        // committing the update is a plain in-place overwrite - no separate PendingRemove of the old
        // one is needed or created for this case, only for the same-key-different-name (rename) case
        // covered by the next test.
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);
        service.Install(BuildZip(
            tierAttribute: " tier=\"Native\" assembly=\"MyPlugin.dll\"",
            entries: new Dictionary<string, string> { ["MyPlugin.dll"] = "v1" },
            version: "1.0.0"));
        service.ApplyPendingChanges();
        var original = Assert.Single(service.GetPackages());
        Assert.Equal(PackageManager.PackageType.Installed, original.PackageType);

        // Re-install the same key/name at a newer version - no manual Uninstall/Remove call in
        // between, matching the whole point of this feature: one Install call is enough.
        service.Install(BuildZip(
            tierAttribute: " tier=\"Native\" assembly=\"MyPlugin.dll\"",
            entries: new Dictionary<string, string> { ["MyPlugin.dll"] = "v2" },
            version: "1.1.0"));

        var afterInstall = service.GetPackages();
        Assert.Equal(2, afterInstall.Count); // old Installed + new PendingInstall, until restart commits
        Assert.Contains(afterInstall, p => p.PackageType == PackageManager.PackageType.Installed && p.Version == "1.0.0");
        Assert.Contains(afterInstall, p => p.PackageType == PackageManager.PackageType.PendingInstall && p.Version == "1.1.0");

        service.ApplyPendingChanges();

        var afterCommit = Assert.Single(service.GetPackages());
        Assert.Equal(PackageManager.PackageType.Installed, afterCommit.PackageType);
        Assert.Equal("1.1.0", afterCommit.Version);
        string dllPath = Path.Combine(_rootDirectory, afterCommit.Name, "MyPlugin.dll");
        Assert.Equal("v2", File.ReadAllText(dllPath));
    }

    [Fact]
    public void Installing_a_newer_version_under_a_different_display_name_auto_stages_the_old_one_for_removal()
    {
        // A same-key package renamed between versions would otherwise resolve to a different final
        // install path, silently leaving the old folder behind forever with no way for the user to
        // notice - PackageManager.Install auto-marks it for removal (same ".remove" mechanism a
        // manual Uninstall uses) as soon as it detects the same key already Installed elsewhere.
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);
        service.Install(BuildZip(
            tierAttribute: " tier=\"Native\" assembly=\"MyPlugin.dll\"",
            entries: new Dictionary<string, string> { ["MyPlugin.dll"] = "v1" },
            version: "1.0.0"));
        service.ApplyPendingChanges();

        string renamedZip = Path.Combine(_testRoot, $"{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(renamedZip, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "plugin.xml", """<Plugin key="fixture" name="Fixture Renamed" tier="Native" assembly="MyPlugin.dll" version="2.0.0"></Plugin>""");
            WriteEntry(archive, "MyPlugin.dll", "v2");
        }

        service.Install(renamedZip);

        var afterInstall = service.GetPackages();
        Assert.Equal(2, afterInstall.Count);
        Assert.Contains(afterInstall, p => p.PackageType == PackageManager.PackageType.PendingRemove && p.Name == "Fixture");
        Assert.Contains(afterInstall, p => p.PackageType == PackageManager.PackageType.PendingInstall && p.Name == "Fixture Renamed");

        service.ApplyPendingChanges();

        var afterCommit = Assert.Single(service.GetPackages());
        Assert.Equal("Fixture Renamed", afterCommit.Name);
        Assert.Equal("2.0.0", afterCommit.Version);
    }
}
