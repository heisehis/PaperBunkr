using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
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
/// Plugin screen master-detail (docs/superpowers/specs/2026-09-12-plugin-management-screen-redesign-
/// design.md): a sidebar of installed packages plus a detail pane per selection, replacing the
/// previous flat Packages-panel-plus-hook-grouped-commands layout (docs/superpowers/specs/2026-08-24-
/// plugin-api-v2-design.md §6). Every test that constructs a <see cref="PluginScreenViewModel"/>
/// uses its internal test-seam constructor with a <see cref="PluginPackageService"/> pointed at an
/// isolated temp folder pair - the default (public) constructor's <see cref="PluginPackageService"/>
/// targets the real <c>%AppData%\Paperbunkr\plugins</c> location, which must never be touched by a
/// test. Needs <see cref="AvaloniaTestCollection"/> since <see cref="PluginPackageDetailViewModel.DeleteConfirm"/>
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
    public void Selecting_a_package_populates_its_detail_with_only_its_own_commands()
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

            File.WriteAllText(Path.Combine(pluginB, "plugin.xml"), """
                <Plugin key="plugin-b" name="Plugin B">
                  <Command hook="Startup" key="b.startup" name="B Startup" script="s.csx" />
                </Plugin>
                """);
            File.WriteAllText(Path.Combine(pluginB, "s.csx"), "return 2;");

            var host = new PluginHostService();
            host.Engine.Discover(root, MakeEnvironment());
            Assert.All(host.Engine.AllCommands, c => Assert.False(c.IsBroken));

            // The sidebar's Packages come from PluginPackageService reading real folders - point it
            // at the same root the engine just discovered from, not an unrelated isolated one.
            var vm = new PluginScreenViewModel(new NoOpFilePicker(), new FakeDialogService(), new PluginPackageService(root, MakeTempDir()));
            vm.AttachHost(host);

            Assert.Equal(2, vm.Packages.Count);
            Assert.Null(vm.SelectedPackageDetail);

            var rowA = Assert.Single(vm.Packages, p => p.Name == "Plugin A");
            rowA.SelectCommand.Execute(null);

            Assert.NotNull(vm.SelectedPackageDetail);
            Assert.Equal(2, vm.SelectedPackageDetail!.Commands.Count);
            Assert.Contains(vm.SelectedPackageDetail.Commands, c => c.Name == "A Startup");
            Assert.Contains(vm.SelectedPackageDetail.Commands, c => c.Name == "A Library");
            Assert.DoesNotContain(vm.SelectedPackageDetail.Commands, c => c.Name == "B Startup");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Refresh_WithNoHost_LeavesPackagesEmpty()
    {
        var vm = new PluginScreenViewModel(new NoOpFilePicker(), new FakeDialogService(), MakeIsolatedPackageService());
        vm.Refresh();
        Assert.Empty(vm.Packages);
        Assert.Null(vm.SelectedPackageDetail);
    }

    [Fact]
    public void FilterText_narrows_FilteredPackages_by_a_case_insensitive_name_substring()
    {
        string root = MakeTempDir();
        string pluginA = Path.Combine(root, "widget-pack");
        string pluginB = Path.Combine(root, "gizmo-pack");
        Directory.CreateDirectory(pluginA);
        Directory.CreateDirectory(pluginB);
        try
        {
            File.WriteAllText(Path.Combine(pluginA, "plugin.xml"), """<Plugin key="widget" name="Widget Pack"></Plugin>""");
            File.WriteAllText(Path.Combine(pluginB, "plugin.xml"), """<Plugin key="gizmo" name="Gizmo Pack"></Plugin>""");

            var host = new PluginHostService();
            host.Engine.Discover(root, MakeEnvironment());

            var vm = new PluginScreenViewModel(new NoOpFilePicker(), new FakeDialogService(), new PluginPackageService(root, MakeTempDir()));
            vm.AttachHost(host);
            Assert.Equal(2, vm.FilteredPackages.Count);

            vm.FilterText = "widget";
            Assert.Single(vm.FilteredPackages, p => p.Name == "Widget Pack");

            vm.FilterText = "PACK"; // case-insensitive, matches both
            Assert.Equal(2, vm.FilteredPackages.Count);

            vm.FilterText = "nothing-matches-this";
            Assert.Empty(vm.FilteredPackages);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
            var vm = new PluginScreenViewModel(new FakeFilePicker(zipPath), new FakeDialogService(), new PluginPackageService(root, staging));
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
            // plugin.xml's own name attribute is now authoritative over package.ini's (docs
            // §4.1) - this fixture's manifest says name="Zippy", not "Zippy Pack".
            Assert.Equal("Zippy", package.Name);
            Assert.True(File.Exists(Path.Combine(root, "Zippy", "plugin.xml")));

            // No restart needed (docs on PluginPackageService) - the newly installed command is
            // already visible once selected.
            package.SelectCommand.Execute(null);
            Assert.Single(vm.SelectedPackageDetail!.Commands, c => c.Name == "Zippy Startup");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(staging, recursive: true);
            File.Delete(zipPath);
        }
    }

    [Fact]
    public async Task InstallPackage_WithANativeTierZip_ShowsTrustNoticeAndStaysPendingUntilRestart()
    {
        // Implementation plan Phase 2 Step 2.3 - the Packages panel row must actually surface
        // IsNativeTier/IsPending through the real InstallPackage command, not just at the
        // PackageManager/PluginPackageService layer already covered by
        // PluginPackageServiceNativeTierTests.
        string root = MakeTempDir();
        string staging = MakeTempDir();
        string zipPath = Path.Combine(Path.GetTempPath(), $"native-pack-{Guid.NewGuid():N}.zip");
        try
        {
            BuildFlatZip(zipPath, new Dictionary<string, string>
            {
                ["plugin.xml"] = """
                    <Plugin key="native-fixture" name="Native Fixture" tier="Native" assembly="NativeFixture.dll">
                    </Plugin>
                    """,
                ["NativeFixture.dll"] = "not a real dll",
            });

            var host = new PluginHostService();
            host.InitializeForTests(MakeEnvironment());
            var vm = new PluginScreenViewModel(new FakeFilePicker(zipPath), new FakeDialogService(), new PluginPackageService(root, staging));
            vm.AttachHost(host);

            await vm.InstallPackageCommand.ExecuteAsync(null);

            var package = Assert.Single(vm.Packages);
            // plugin.xml's own name attribute is now authoritative (docs/superpowers/specs/2026-09-
            // 12-plugin-management-screen-redesign-design.md §4.1) - no package.ini needed here.
            Assert.Equal("Native Fixture", package.Name);
            Assert.True(package.IsNativeTier);
            Assert.True(package.IsPending);

            // Not yet copied into the final root - still sitting in the staging folder until a
            // restart applies it (v4 §4).
            Assert.False(Directory.Exists(Path.Combine(root, "Native Fixture")));
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
            var vm = new PluginScreenViewModel(new FakeFilePicker(badFile), new FakeDialogService(), new PluginPackageService(root, staging));

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
            });

            var host = new PluginHostService();
            host.InitializeForTests(MakeEnvironment());
            var vm = new PluginScreenViewModel(new FakeFilePicker(zipPath), new FakeDialogService(), new PluginPackageService(root, staging));
            vm.AttachHost(host);
            await vm.InstallPackageCommand.ExecuteAsync(null);
            var row = Assert.Single(vm.Packages);
            string installedPath = Path.Combine(root, "Removable");
            Assert.True(Directory.Exists(installedPath));

            row.SelectCommand.Execute(null);
            var detail = vm.SelectedPackageDetail!;

            detail.DeleteConfirm.TriggerCommand.Execute(null); // first click just arms
            Assert.True(detail.DeleteConfirm.IsArmed);
            Assert.Single(vm.Packages); // not removed yet

            detail.DeleteConfirm.TriggerCommand.Execute(null); // second click confirms

            // RemovePackage defers Refresh() via Dispatcher.UIThread.Post (see its own doc comment -
            // avoids detaching this same button mid-route). Headless tests have no running dispatcher
            // loop, so the queued job needs an explicit pump (same idiom already used elsewhere in
            // this test project, e.g. ReaderScreenViewModelTests).
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

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

    // ---- PluginPackageDetailViewModel: tri-state master toggle, Configure/LoadError/Reload -----

    /// <summary>
    /// <see cref="PluginHostService.InitializeForTests"/> discovers from the static
    /// <see cref="PluginPaths.RootDirectory"/>, not a parameter - matches the existing convention in
    /// this file's own install tests (temporarily redirect, discover, restore).
    /// </summary>
    private (PluginHostService host, PackageManager.Package package, List<Command> commands) MakeHostWithScriptCommands(string root, int commandCount)
    {
        string dir = Path.Combine(root, "widget");
        Directory.CreateDirectory(dir);
        string commandsXml = string.Join("\n", Enumerable.Range(1, commandCount)
            .Select(i => $"""<Command hook="Startup" key="widget.cmd{i}" name="Cmd {i}" script="s.csx" />"""));
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), $"""
            <Plugin key="widget" name="Widget">
            {commandsXml}
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "s.csx"), "return 1;");

        string originalRoot = PluginPaths.RootDirectory;
        PluginPaths.RootDirectory = root;
        try
        {
            var host = new PluginHostService();
            host.InitializeForTests(MakeEnvironment());
            var package = PackageManager.Package.CreateFromPath(dir, pending: false);
            var commands = host.Engine.AllCommands.Where(c => c.PluginKey == "widget").ToList();
            return (host, package, commands);
        }
        finally
        {
            PluginPaths.RootDirectory = originalRoot;
        }
    }

    [Fact]
    public void EnabledState_is_true_when_every_command_is_enabled()
    {
        string root = MakeTempDir();
        try
        {
            var (host, package, commands) = MakeHostWithScriptCommands(root, 2);
            var detail = new PluginPackageDetailViewModel(package, commands, host, new NoOpFilePicker(), null, false, () => { }, () => { });
            Assert.True(detail.EnabledState);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnabledState_is_false_when_every_command_is_disabled()
    {
        string root = MakeTempDir();
        try
        {
            var (host, package, commands) = MakeHostWithScriptCommands(root, 2);
            foreach (var c in commands)
            {
                host.SetCommandEnabled(c, false);
            }

            var detail = new PluginPackageDetailViewModel(package, commands, host, new NoOpFilePicker(), null, false, () => { }, () => { });
            Assert.False(detail.EnabledState);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnabledState_is_null_when_commands_are_mixed()
    {
        string root = MakeTempDir();
        try
        {
            var (host, package, commands) = MakeHostWithScriptCommands(root, 2);
            host.SetCommandEnabled(commands[0], false);

            var detail = new PluginPackageDetailViewModel(package, commands, host, new NoOpFilePicker(), null, false, () => { }, () => { });
            Assert.Null(detail.EnabledState);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>External review round 2's specific case: Avalonia's own three-state click cycle is
    /// true → null → false → true, so clicking from a mixed/indeterminate state must never rely on
    /// that cycle - this asserts the command-driven path actually enables everything, not disables
    /// it (docs/superpowers/specs/2026-09-12-plugin-management-screen-redesign-design.md §4.4).</summary>
    [Fact]
    public void MasterEnabledClick_from_a_mixed_state_enables_every_command()
    {
        string root = MakeTempDir();
        try
        {
            var (host, package, commands) = MakeHostWithScriptCommands(root, 2);
            host.SetCommandEnabled(commands[0], false);
            var detail = new PluginPackageDetailViewModel(package, commands, host, new NoOpFilePicker(), null, false, () => { }, () => { });
            Assert.Null(detail.EnabledState);

            detail.MasterEnabledClickCommand.Execute(null);

            Assert.All(commands, c => Assert.True(c.Enabled));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MasterEnabledClick_from_fully_enabled_disables_every_command()
    {
        string root = MakeTempDir();
        try
        {
            var (host, package, commands) = MakeHostWithScriptCommands(root, 2);
            var detail = new PluginPackageDetailViewModel(package, commands, host, new NoOpFilePicker(), null, false, () => { }, () => { });
            Assert.True(detail.EnabledState);

            detail.MasterEnabledClickCommand.Execute(null);

            Assert.All(commands, c => Assert.False(c.Enabled));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HasConfigure_and_LoadError_reflect_the_values_passed_in()
    {
        string root = MakeTempDir();
        try
        {
            var (host, package, commands) = MakeHostWithScriptCommands(root, 1);

            var withConfigureAndError = new PluginPackageDetailViewModel(package, commands, host, new NoOpFilePicker(), "boom", true, () => { }, () => { });
            Assert.True(withConfigureAndError.HasConfigure);
            Assert.True(withConfigureAndError.HasLoadError);
            Assert.Equal("boom", withConfigureAndError.LoadError);

            var withNeither = new PluginPackageDetailViewModel(package, commands, host, new NoOpFilePicker(), null, false, () => { }, () => { });
            Assert.False(withNeither.HasConfigure);
            Assert.False(withNeither.HasLoadError);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CopyErrorCommand_copies_the_exact_LoadError_text()
    {
        string root = MakeTempDir();
        try
        {
            var (host, package, commands) = MakeHostWithScriptCommands(root, 1);
            var clipboard = new RecordingFilePicker();
            var detail = new PluginPackageDetailViewModel(package, commands, host, clipboard, "something broke", false, () => { }, () => { });

            await detail.CopyErrorCommand.ExecuteAsync(null);

            Assert.Equal("something broke", clipboard.CopiedText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShowReload_is_true_for_script_tier_and_false_for_native_tier()
    {
        string root = MakeTempDir();
        try
        {
            var (scriptHost, scriptPackage, scriptCommands) = MakeHostWithScriptCommands(root, 1);
            var scriptDetail = new PluginPackageDetailViewModel(scriptPackage, scriptCommands, scriptHost, new NoOpFilePicker(), null, false, () => { }, () => { });
            Assert.True(scriptDetail.ShowReload);

            string nativeDir = Path.Combine(root, "native-widget");
            Directory.CreateDirectory(nativeDir);
            File.WriteAllText(Path.Combine(nativeDir, "plugin.xml"),
                """<Plugin key="native-widget" name="Native Widget" tier="Native" assembly="missing.dll"></Plugin>""");
            var nativePackage = PackageManager.Package.CreateFromPath(nativeDir, pending: false);
            var nativeHost = new PluginHostService();
            nativeHost.InitializeForTests(MakeEnvironment());
            var nativeDetail = new PluginPackageDetailViewModel(nativePackage, new List<Command>(), nativeHost, new NoOpFilePicker(), null, false, () => { }, () => { });
            Assert.False(nativeDetail.ShowReload);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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

    /// <summary>Always confirms - none of these tests install into a folder that already has a
    /// same-named package, so the overwrite-prompt branch never actually executes; this just needs
    /// to satisfy the constructor and not throw if it ever does.</summary>
    private sealed class FakeDialogService : IDialogService
    {
        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(0);
        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(true);
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

    private sealed class RecordingFilePicker : IFilePickerService
    {
        public string? CopiedText { get; private set; }
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) { CopiedText = text; return Task.CompletedTask; }
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
        public int GetOrCreateSeriesId(string seriesName) => 0;
        public Issue? AddNewBook(int seriesId, bool showDialog) => null;
        public byte[]? GetComicPublisherIcon(Issue issue) => null;
        public byte[]? GetComicImprintIcon(Issue issue) => null;
        public byte[]? GetComicAgeRatingIcon(Issue issue) => null;
        public byte[]? GetComicFormatIcon(Issue issue) => null;
        public IDictionary<string, string> GetComicFields() => new Dictionary<string, string>();
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
