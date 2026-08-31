using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Plugin screen: (1) grouping by hook/extension-point instead of by installed plugin, matching
/// ComicRackCE's real Preferences → Scripts tab (docs/superpowers/specs/2026-08-24-plugin-api-v2-
/// design.md §6, <c>_reference/ComicRackCE/ComicRack/Dialogs/PreferencesDialog.cs</c>'s
/// <c>FillScriptsList</c>); (2) the Packages panel's install/uninstall via
/// <see cref="PluginPackageService"/>. Every test that constructs a <see cref="PluginScreenViewModel"/>
/// uses its internal test-seam constructor with a <see cref="PluginPackageService"/> pointed at an
/// isolated temp folder pair - the default (public) constructor's <see cref="PluginPackageService"/>
/// targets the real <c>%AppData%\Paperbunkr\plugins</c> location, which must never be touched by a
/// test. Needs <see cref="AvaloniaTestCollection"/> since <see cref="PluginPackageRowViewModel.DeleteConfirm"/>
/// constructs a <c>DispatcherTimer</c> (same requirement as <see cref="TwoStepConfirmTests"/>), and a
/// temp-file <see cref="PaperbunkrDbContext.DatabasePathOverride"/> (same seam as
/// <see cref="PluginHostServiceTests"/>) since <see cref="PluginHostService.RediscoverPlugins"/>
/// reads <c>PluginCommandStates</c> from the real database otherwise.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class PluginScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public PluginScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pluginscreen_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch (IOException) { }
    }

    [Fact]
    public void Refresh_GroupsAcrossPlugins_ByHookLabel_AndReadsPackageFromEachCommandsOwnFolder()
    {
        string root = MakeTempDir();
        string pluginA = Path.Combine(root, "plugin-a");
        string pluginB = Path.Combine(root, "plugin-b");
        Directory.CreateDirectory(pluginA);
        Directory.CreateDirectory(pluginB);
        try
        {
            File.WriteAllText(Path.Combine(pluginA, "plugin.xml"), """
                <Plugin key="plugin-a" name="Plugin A">
                  <Command hook="Startup" key="a.startup" name="A Startup" script="s.csx" />
                  <Command hook="Library" key="a.library" name="A Library" script="s.csx" />
                </Plugin>
                """);
            File.WriteAllText(Path.Combine(pluginA, "s.csx"), "return 1;");
            File.WriteAllText(Path.Combine(pluginA, "package.ini"), "Name = Widget Pack");

            File.WriteAllText(Path.Combine(pluginB, "plugin.xml"), """
                <Plugin key="plugin-b" name="Plugin B">
                  <Command hook="Startup" key="b.startup" name="B Startup" script="s.csx" />
                </Plugin>
                """);
            File.WriteAllText(Path.Combine(pluginB, "s.csx"), "return 2;");
            // Deliberately no package.ini here - Package should fall back to CE's own "Other".

            var host = new PluginHostService();
            host.Engine.Discover(root, MakeEnvironment());
            Assert.All(host.Engine.AllCommands, c => Assert.False(c.IsBroken));

            var vm = new PluginScreenViewModel(new NoOpFilePicker(), MakeIsolatedPackageService());
            vm.AttachHost(host);

            Assert.True(vm.HasPlugins);
            Assert.Equal(2, vm.Groups.Count); // one group per hook label, not per plugin

            var startupGroup = Assert.Single(vm.Groups, g => g.Header == "Actions when Paperbunkr starts");
            Assert.Equal(2, startupGroup.Commands.Count); // A Startup and B Startup share one group despite different plugins

            var aStartup = Assert.Single(startupGroup.Commands, c => c.Name == "A Startup");
            Assert.Equal("Widget Pack", aStartup.Package);

            var bStartup = Assert.Single(startupGroup.Commands, c => c.Name == "B Startup");
            Assert.Equal("Other", bStartup.Package);

            var libraryGroup = Assert.Single(vm.Groups, g => g.Header == "Edit/Update Books Commands");
            Assert.Single(libraryGroup.Commands);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Refresh_WithNoHost_LeavesHasPluginsFalse()
    {
        var vm = new PluginScreenViewModel(new NoOpFilePicker(), MakeIsolatedPackageService());
        vm.Refresh();
        Assert.False(vm.HasPlugins);
        Assert.Empty(vm.Groups);
    }

    [Fact]
    public async Task InstallPackage_ExtractsTheZipAndListsIt_ThenRediscoveryShowsItsCommand()
    {
        string root = MakeTempDir();
        string staging = MakeTempDir();
        string zipPath = Path.Combine(Path.GetTempPath(), $"zippy-pack-{Guid.NewGuid():N}.zip");
        try
        {
            BuildFlatZip(zipPath, new Dictionary<string, string>
            {
                ["plugin.xml"] = """
                    <Plugin key="zippy" name="Zippy">
                      <Command hook="Startup" key="zippy.startup" name="Zippy Startup" script="s.csx" />
                    </Plugin>
                    """,
                ["s.csx"] = "return 1;",
                ["package.ini"] = "Name = Zippy Pack",
            });

            var host = new PluginHostService();
            host.InitializeForTests(MakeEnvironment()); // empty root so far - PluginPaths.RootDirectory isn't touched by this
            var vm = new PluginScreenViewModel(new FakeFilePicker(zipPath), new PluginPackageService(root, staging));
            vm.AttachHost(host);
            Assert.Empty(vm.Packages);

            // Redirect the shared PluginPaths.RootDirectory (what PluginHostService.RediscoverPlugins
            // actually rescans) to the same temp root the package service installs into - the
            // documented seam (PluginPaths.RootDirectory's own doc comment) for exactly this.
            string originalRoot = PluginPaths.RootDirectory;
            PluginPaths.RootDirectory = root;
            try
            {
                await vm.InstallPackageCommand.ExecuteAsync(null);
            }
            finally
            {
                PluginPaths.RootDirectory = originalRoot;
            }

            var package = Assert.Single(vm.Packages);
            Assert.Equal("Zippy Pack", package.Name);
            Assert.True(File.Exists(Path.Combine(root, "Zippy Pack", "plugin.xml")));

            // No restart needed (docs on PluginPackageService) - the newly installed command is
            // already visible in the grouped list.
            var startupGroup = Assert.Single(vm.Groups, g => g.Header == "Actions when Paperbunkr starts");
            Assert.Single(startupGroup.Commands, c => c.Name == "Zippy Startup");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(staging, recursive: true);
            File.Delete(zipPath);
        }
    }

    [Fact]
    public async Task InstallPackage_WithAnUnreadableZip_LeavesPackagesEmpty_AndDoesNotThrow()
    {
        string root = MakeTempDir();
        string staging = MakeTempDir();
        string badFile = Path.Combine(Path.GetTempPath(), $"not-a-zip-{Guid.NewGuid():N}.zip");
        File.WriteAllText(badFile, "definitely not a zip file");
        try
        {
            var vm = new PluginScreenViewModel(new FakeFilePicker(badFile), new PluginPackageService(root, staging));

            await vm.InstallPackageCommand.ExecuteAsync(null);

            Assert.Empty(vm.Packages);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(staging, recursive: true);
            File.Delete(badFile);
        }
    }

    [Fact]
    public async Task RemovePackage_ViaTwoStepConfirm_RequiresASecondTrigger_ThenDeletesTheFolder()
    {
        string root = MakeTempDir();
        string staging = MakeTempDir();
        string zipPath = Path.Combine(Path.GetTempPath(), $"removable-pack-{Guid.NewGuid():N}.zip");
        try
        {
            BuildFlatZip(zipPath, new Dictionary<string, string>
            {
                ["plugin.xml"] = """<Plugin key="removable" name="Removable"></Plugin>""",
                ["package.ini"] = "Name = Removable Pack",
            });

            var vm = new PluginScreenViewModel(new FakeFilePicker(zipPath), new PluginPackageService(root, staging));
            await vm.InstallPackageCommand.ExecuteAsync(null);
            var row = Assert.Single(vm.Packages);
            string installedPath = Path.Combine(root, "Removable Pack");
            Assert.True(Directory.Exists(installedPath));

            row.DeleteConfirm.TriggerCommand.Execute(null); // first click just arms
            Assert.True(row.DeleteConfirm.IsArmed);
            Assert.Single(vm.Packages); // not removed yet

            row.DeleteConfirm.TriggerCommand.Execute(null); // second click confirms

            Assert.Empty(vm.Packages);
            Assert.False(Directory.Exists(installedPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(staging, recursive: true);
            File.Delete(zipPath);
        }
    }

    private static string MakeTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"pb-pluginscreen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static PluginPackageService MakeIsolatedPackageService() => new(MakeTempDir(), MakeTempDir());

    private static void BuildFlatZip(string zipPath, Dictionary<string, string> filesByName)
    {
        using var stream = File.Create(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in filesByName)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    private static PaperbunkrPluginEnvironment MakeEnvironment() => new()
    {
        MainWindow = new StubHostWindow(),
        App = new StubApplication(),
        OpenBooks = new StubOpenBooks(),
        Browser = new StubBrowser(),
        ComicDisplay = new StubComicDisplay(),
        Metadata = new PaperbunkrMetadataGraph(),
        Rules = new PaperbunkrRulesEngine(),
        Writer = new PaperbunkrMetadataWriter(),
        ThemePlugin = new StubThemePlugin(),
    };

    private sealed class NoOpFilePicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        private readonly string _path;
        public FakeFilePicker(string path) => _path = path;
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(_path);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private sealed class StubHostWindow : IPluginHostWindow { public object Owner { get; } = new(); }

    private sealed class StubApplication : IApplication
    {
        public string ProductVersion => "test";
        public void Restart() { }
        public void ScanFolders() { }
        public IEnumerable<Issue> GetLibraryBooks() => Array.Empty<Issue>();
        public Issue? GetBook(int issueId) => null;
        public bool RemoveBook(Issue issue) => false;
        public bool SetCustomBookThumbnail(Issue issue, byte[] imageBytes) => false;
        public byte[]? GetComicPage(Issue issue, int page) => null;
        public byte[]? GetComicThumbnail(Issue issue) => null;
        public Task<string?> ReadInternetAsync(string url) => Task.FromResult<string?>(null);
        public int AskQuestion(string question, string buttonText, string optionText) => 0;
        public void ShowComicInfo(IEnumerable<Issue> books) { }
    }

    private sealed class StubOpenBooks : IOpenBooksManager
    {
        public bool Open(Issue issue, int page) => true;
        public bool OpenFile(string file, int page) => true;
        public bool IsOpen(Issue issue) => false;
    }

    private sealed class StubBrowser : IBrowser
    {
        public bool OpenNextComic() => true;
        public bool OpenPrevComic() => true;
        public bool OpenRandomComic() => true;
        public void SelectComics(IEnumerable<Issue> books) { }
    }

    private sealed class StubComicDisplay : IComicDisplay
    {
        public Issue? CurrentBook => null;
        public int CurrentPageIndex => 0;
        public int PageCount => 0;
        public event Action<int>? CurrentPageIndexChanged { add { } remove { } }
        public void NextPage() { }
        public void PreviousPage() { }
        public void GoToPage(int index) { }
    }

    private sealed class StubThemePlugin : IThemePlugin { public string CurrentSkinKey => "default"; }
}
