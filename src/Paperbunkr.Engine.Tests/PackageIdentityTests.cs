using System;
using System.IO;
using cYo.Projects.ComicRack.Engine;

namespace Paperbunkr.Engine.Tests;

/// <summary>
/// Implementation plan (docs/superpowers/specs/2026-09-12-plugin-management-screen-redesign-plan.md)
/// Step 1 verification: <see cref="PackageManager.Package.Key"/>/<see cref="PackageManager.Package.Name"/>
/// now read <c>plugin.xml</c>'s root attributes first, falling back to <c>package.ini</c>/a folder-name
/// heuristic only when the manifest doesn't supply them - and <see cref="PackageManager.Package.Key"/>
/// never falls back to the bare folder name (external review round 3's "dist" collision), only a
/// path hash.
/// </summary>
public sealed class PackageIdentityTests : IDisposable
{
    private readonly string _root;

    public PackageIdentityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pb-package-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string MakePluginDir(string folderName)
    {
        string dir = Path.Combine(_root, folderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Key_and_Name_come_from_plugin_xml_even_when_package_ini_disagrees()
    {
        string dir = MakePluginDir("some-folder");
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), """<Plugin key="x" name="Real Name" />""");
        File.WriteAllText(Path.Combine(dir, "package.ini"), "Name = Wrong Name From Ini");

        var package = PackageManager.Package.CreateFromPath(dir, pending: false);

        Assert.Equal("x", package.Key);
        Assert.Equal("Real Name", package.Name);
    }

    [Fact]
    public void Name_falls_back_to_package_ini_when_the_manifest_has_no_name_attribute()
    {
        string dir = MakePluginDir("some-other-folder");
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), """<Plugin key="x" />""");
        File.WriteAllText(Path.Combine(dir, "package.ini"), "Name = From Ini");

        var package = PackageManager.Package.CreateFromPath(dir, pending: false);

        Assert.Equal("x", package.Key);
        Assert.Equal("From Ini", package.Name);
    }

    [Fact]
    public void With_no_manifest_at_all_Key_falls_back_to_a_stable_non_empty_path_hash_and_Name_to_the_folder_heuristic()
    {
        string dir = MakePluginDir("Manifestless Folder");

        var first = PackageManager.Package.CreateFromPath(dir, pending: false);
        var second = PackageManager.Package.CreateFromPath(dir, pending: false);

        Assert.False(string.IsNullOrEmpty(first.Key));
        Assert.Equal(first.Key, second.Key); // stable across two Package instances for the same path
        Assert.Equal("Manifestless Folder", first.Name);
    }

    [Fact]
    public void Two_manifestless_folders_that_share_a_bare_name_get_different_keys()
    {
        // The external review round 3 scenario: two different plugin authors' "dist" output
        // folders, installed under different parents - Key must not collide just because the
        // bare folder name does.
        string parentA = Path.Combine(_root, "author-a");
        string parentB = Path.Combine(_root, "author-b");
        Directory.CreateDirectory(Path.Combine(parentA, "dist"));
        Directory.CreateDirectory(Path.Combine(parentB, "dist"));

        var packageA = PackageManager.Package.CreateFromPath(Path.Combine(parentA, "dist"), pending: false);
        var packageB = PackageManager.Package.CreateFromPath(Path.Combine(parentB, "dist"), pending: false);

        Assert.NotEqual(packageA.Key, packageB.Key);
    }
}
