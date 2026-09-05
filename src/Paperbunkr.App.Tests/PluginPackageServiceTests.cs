using Paperbunkr.App.Plugins;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Installs the real, shipped <c>sample-plugins/DuplicateFinder.zip</c> (repo root, not test
/// fixture data - see that project's own doc comment) through the exact same
/// <see cref="PluginPackageService"/> path a user hits from the Plugins screen's "Install
/// Package…" button, then discovers and invokes it via a real <see cref="PluginEngine"/>. This is
/// deliberately end-to-end through the *packaging* layer -
/// <c>Paperbunkr.Plugins.Tests.DuplicateFinderPluginTests</c> already covers the hook logic itself
/// against the loose source files, but never exercises <see cref="PluginPackageService.Install"/>
/// or <c>PackageManager.UnzipFile</c> at all. Proves the thing a real user would actually download
/// and install is not broken, not just its source.
/// </summary>
public sealed class PluginPackageServiceTests : IDisposable
{
    private readonly string _rootDirectory;
    private readonly string _stagingDirectory;
    private readonly string _zipPath;

    public PluginPackageServiceTests()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_plugin_package_test_{Guid.NewGuid():N}");
        _rootDirectory = Path.Combine(testRoot, "plugins");
        _stagingDirectory = Path.Combine(testRoot, "plugin-staging");
        _zipPath = Path.Combine(AppContext.BaseDirectory, "SamplePlugins", "DuplicateFinder.zip");
    }

    public void Dispose()
    {
        try
        {
            string testRoot = Path.GetDirectoryName(_rootDirectory)!;
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void The_shipped_zip_exists_and_is_flat()
    {
        // Package.UnzipFile flattens every entry to the zip root's file name (PluginPackageService's
        // own doc comment) - a zip with a subfolder would silently produce a broken/nested install.
        Assert.True(File.Exists(_zipPath), $"Expected the built sample-plugins/DuplicateFinder.zip at {_zipPath} - did the csproj copy rule run?");

        using var archive = System.IO.Compression.ZipFile.OpenRead(_zipPath);
        Assert.All(archive.Entries, e => Assert.DoesNotContain('/', e.FullName));
        Assert.Contains(archive.Entries, e => e.Name == "plugin.xml");
    }

    [Fact]
    public void Install_unpacks_the_package_and_PluginEngine_discovers_all_three_commands_cleanly()
    {
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);

        bool installed = service.Install(_zipPath);

        Assert.True(installed);
        var package = Assert.Single(service.GetPackages());
        Assert.Equal("Duplicate Finder", package.Name);
        Assert.True(package.Installed);

        var engine = new PluginEngine();
        engine.Discover(_rootDirectory, new TestPluginEnvironment());

        Assert.Equal(3, engine.AllCommands.Count);
        Assert.All(engine.AllCommands, c => Assert.False(c.IsBroken));
        Assert.Contains(engine.AllCommands, c => c.Hook == PluginHooks.Startup);
        Assert.Contains(engine.AllCommands, c => c.Hook == PluginHooks.Library);
        Assert.Contains(engine.AllCommands, c => c.Hook == PluginHooks.CreateBookList);
    }

    [Fact]
    public async Task The_installed_package_finds_a_real_duplicate_via_the_Library_hook()
    {
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);
        service.Install(_zipPath);

        var app = new TestPluginApplication
        {
            Library = new List<Issue>
            {
                new() { Id = 1, SeriesId = 10, Number = "1" },
                new() { Id = 2, SeriesId = 10, Number = "1" }, // duplicate of #1
            },
        };
        var engine = new PluginEngine();
        engine.Discover(_rootDirectory, new TestPluginEnvironment(app));

        var results = await engine.InvokeAsync(PluginHooks.Library, env => new BooksHookGlobals { Environment = env, Books = new List<Issue> { app.Library[0] } });

        var result = Assert.Single(results);
        Assert.True(result.Success);
        var duplicates = Assert.IsAssignableFrom<List<string>>(result.ReturnValue);
        Assert.Single(duplicates);
    }

    [Fact]
    public void Uninstall_removes_the_package()
    {
        var service = new PluginPackageService(_rootDirectory, _stagingDirectory);
        service.Install(_zipPath);
        var package = Assert.Single(service.GetPackages());

        service.Uninstall(package);

        Assert.Empty(service.GetPackages());
    }
}
