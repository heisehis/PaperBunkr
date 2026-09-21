using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Events;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The per-plugin log files (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md §1): what a line
/// looks like, that the file is capped and rolled, that a hostile key can't escape the folder, that logging never
/// throws into the plugin, and that the host writes a plugin's own failures into its log.
/// </summary>
public sealed class PluginLogFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_pluginlogs_{Guid.NewGuid():N}");
    private readonly PluginLogFiles _files;

    public PluginLogFilesTests()
    {
        _files = new PluginLogFiles(() => Path.Combine(_root, "plugins"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Read(string key) => File.ReadAllText(_files.PathFor(key));

    private static string[] Lines(string text) => text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void A_line_has_a_utc_timestamp_a_level_and_the_message()
    {
        new PluginLogger(_files, "my-plugin").Info("hello");

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z \[INF\] hello$", Read("my-plugin").TrimEnd());
    }

    [Fact]
    public void Each_level_gets_its_own_tag_and_lines_append_in_order()
    {
        var log = new PluginLogger(_files, "p");

        log.Debug("a");
        log.Info("b");
        log.Warn("c");
        log.Error("d");

        Assert.Equal(
            new[] { "[DBG] a", "[INF] b", "[WRN] c", "[ERR] d" },
            Lines(Read("p")).Select(l => l[l.IndexOf('[')..]));
    }

    [Fact]
    public void An_exceptions_full_text_is_written_indented_under_its_message()
    {
        new PluginLogger(_files, "p").Error("it broke", new InvalidOperationException("kaboom"));

        string text = Read("p");
        Assert.Contains("[ERR] it broke", text);
        Assert.Contains("    System.InvalidOperationException: kaboom", text);
    }

    [Fact]
    public void A_multi_line_message_stays_one_entry_with_indented_continuations()
    {
        new PluginLogger(_files, "p").Info("first\nsecond\r\nthird");

        var lines = Lines(Read("p"));
        Assert.Equal(3, lines.Length);
        Assert.EndsWith("first", lines[0]);
        Assert.Equal("    second", lines[1]);
        Assert.Equal("    third", lines[2]);
    }

    [Fact]
    public void Plugins_log_to_their_own_files()
    {
        new PluginLogger(_files, "one").Info("from one");
        new PluginLogger(_files, "two").Info("from two");

        Assert.DoesNotContain("from two", Read("one"));
        Assert.Contains("from two", Read("two"));
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData(@"..\..\evil")]
    [InlineData("a/b\\c:d*e?f")]
    [InlineData("")]
    [InlineData("...")]
    public void A_hostile_or_odd_key_stays_inside_the_log_folder(string key)
    {
        string path = _files.PathFor(key);

        Assert.Equal(Path.Combine(_root, "plugins"), Path.GetDirectoryName(path));
        new PluginLogger(_files, key).Info("still works");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_very_long_key_is_shortened_to_a_usable_filename()
    {
        Assert.True(Path.GetFileName(_files.PathFor(new string('k', 500))).Length <= 104);
    }

    [Fact]
    public void The_file_rolls_to_a_single_previous_file_when_it_reaches_the_cap()
    {
        string path = _files.PathFor("p");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string('x', (int)PluginLogFiles.MaxBytes));

        new PluginLogger(_files, "p").Info("after the roll");

        string rolled = Path.Combine(_root, "plugins", "p.1.log");
        Assert.True(File.Exists(rolled));
        Assert.Equal(PluginLogFiles.MaxBytes, new FileInfo(rolled).Length);
        Assert.Contains("after the roll", File.ReadAllText(path));
        Assert.True(new FileInfo(path).Length < 1000);
    }

    [Fact]
    public void Rolling_again_replaces_the_previous_file_rather_than_keeping_a_third()
    {
        string path = _files.PathFor("p");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string('a', (int)PluginLogFiles.MaxBytes));
        new PluginLogger(_files, "p").Info("one");                                   // rolls: the a's move to p.1.log
        File.AppendAllText(path, new string('b', (int)PluginLogFiles.MaxBytes));
        new PluginLogger(_files, "p").Info("two");                                   // rolls again, replacing p.1.log

        Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, "plugins")).Length);
        string previous = File.ReadAllText(Path.Combine(_root, "plugins", "p.1.log"));
        Assert.Contains("bbbb", previous);
        Assert.DoesNotContain("aaaa", previous);
    }

    [Fact]
    public void A_file_just_under_the_cap_is_not_rolled()
    {
        string path = _files.PathFor("p");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string('x', (int)PluginLogFiles.MaxBytes - 1000));

        new PluginLogger(_files, "p").Info("x");

        Assert.False(File.Exists(Path.Combine(_root, "plugins", "p.1.log")));
    }

    [Fact]
    public async Task Many_threads_logging_at_once_lose_no_lines()
    {
        var log = new PluginLogger(_files, "busy");

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                log.Info($"thread {t} line {i}");
            }
        })));

        var lines = Lines(Read("busy"));
        Assert.Equal(400, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"\[INF\] thread \d line \d+$", l));
    }

    [Fact]
    public void Logging_never_throws_even_when_the_folder_cannot_be_used()
    {
        // A file where the folder should be: CreateDirectory and AppendAllText both fail.
        string blocker = Path.Combine(_root, "blocked");
        Directory.CreateDirectory(_root);
        File.WriteAllText(blocker, "i am a file");
        var broken = new PluginLogFiles(() => Path.Combine(blocker, "plugins"));

        var log = new PluginLogger(broken, "p");
        log.Info("x");
        log.Error("y", new Exception("z"));   // must not throw
    }

    [Fact]
    public void The_folder_is_created_on_first_use()
    {
        Assert.False(Directory.Exists(Path.Combine(_root, "plugins")));

        new PluginLogger(_files, "p").Info("x");

        Assert.True(Directory.Exists(Path.Combine(_root, "plugins")));
    }

    [Fact]
    public void The_default_location_follows_the_diagnostics_log_folder()
    {
        string? before = DiagnosticsService.LogDirectoryOverride;
        DiagnosticsService.LogDirectoryOverride = Path.Combine(_root, "diag");
        try
        {
            Assert.Equal(Path.Combine(_root, "diag", "plugins"), PluginLogFiles.Default.Directory);
        }
        finally
        {
            DiagnosticsService.LogDirectoryOverride = before;
        }
    }

    // ---- the real environment ----

    [Fact]
    public void The_real_environment_logs_as_the_plugin_that_owns_each_clone()
    {
        var baseEnvironment = new PaperbunkrPluginEnvironment
        {
            MainWindow = null!, App = null!, OpenBooks = null!, Browser = null!, ComicDisplay = null!,
            Metadata = null!, Rules = null!, Writer = null!, ThemePlugin = null!,
            ActivityService = new ActivityService(dispatch: a => a(), recordRun: _ => { }),
            LogFiles = _files,
        };
        var a = (PaperbunkrPluginEnvironment)baseEnvironment.Clone();
        a.PluginKey = "alpha";
        var b = (PaperbunkrPluginEnvironment)baseEnvironment.Clone();
        b.PluginKey = "beta";

        a.Log.Info("from alpha");
        b.Log.Info("from beta");

        Assert.Contains("from alpha", Read("alpha"));
        Assert.DoesNotContain("from beta", Read("alpha"));
        Assert.Contains("from beta", Read("beta"));
    }

    // ---- the host writes a plugin's failures into its own log ----

    [Fact]
    public async Task A_failing_plugin_command_run_directly_is_logged_in_its_own_log()
    {
        string installed = Path.Combine(_root, "installed");
        string plugin = Path.Combine(installed, "flaky");
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(plugin, "plugin.xml"), """
            <Plugin key="flaky" name="Flaky">
              <Command hook="CreateBookList" key="flaky.list" name="Flaky list" script="run.csx" />
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(plugin, "run.csx"), "throw new System.InvalidOperationException(\"kaboom\");");
        var host = new PluginHostService { LogFiles = _files };
        host.Engine.Discover(installed, new TestPluginEnvironment());
        var command = host.Engine.AllCommands.Single();

        var result = await host.RunCommandAsync(command, new CreateBookListHookGlobals { Environment = command.Environment! });

        Assert.False(result.Success);
        string log = Read("flaky");
        Assert.Contains("[ERR]", log);
        Assert.Contains("\"Flaky list\" (CreateBookList) failed: kaboom", log);
    }

    [Fact]
    public async Task A_domain_hook_that_keeps_failing_is_logged_once_in_its_own_log()
    {
        string installed = Path.Combine(_root, "installed2");
        string plugin = Path.Combine(installed, "flaky2");
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(plugin, "plugin.xml"), """
            <Plugin key="flaky2" name="Flaky Two">
              <Command hook="ReadingListChanged" key="flaky2.cmd" name="Mirror" script="run.csx" />
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(plugin, "run.csx"), "throw new System.InvalidOperationException(\"nope\");");
        var host = new PluginHostService { LogFiles = _files };
        var events = new LibraryEvents();
        host.Engine.Discover(installed, new TestPluginEnvironment());
        host.AttachDomainEvents(null, events);

        for (int i = 0; i < 3; i++)
        {
            events.Raise(new ReadingListChangedEvent(1, "x", ReadingListChangeKind.Added, new[] { 1 }, Array.Empty<int>()));
        }

        Assert.True(await host.DomainHooks.WaitForIdleAsync(TimeSpan.FromSeconds(15)));
        host.DetachDomainEvents();
        string log = Read("flaky2");
        Assert.Contains("[ERR]", log);
        Assert.Equal(1, log.Split("nope").Length - 1);   // reported once per session, not once per event
    }

    // ---- the Plugin screen's "Open log folder" ----

    private PluginPackageDetailViewModel Detail(string name, PluginHostService host, Action<string> openFolder)
    {
        string installed = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(installed, "p"));
        File.WriteAllText(Path.Combine(installed, "p", "plugin.xml"), """<Plugin key="p" name="P"></Plugin>""");
        var package = new PluginPackageService(installed, Path.Combine(_root, name + "-staging")).GetPackages().Single();
        return new PluginPackageDetailViewModel(
            package, Array.Empty<Command>(), host, new NoFilePicker(),
            loadError: null, hasConfigure: false, onRemoved: () => { }, onRefreshRequested: () => { },
            openFolder: openFolder);
    }

    [Fact]
    public void Open_log_folder_creates_the_folder_and_opens_it()
    {
        string? opened = null;
        var detail = Detail("installed3", new PluginHostService { LogFiles = _files }, p => opened = p);

        detail.OpenLogFolderCommand.Execute(null);

        Assert.Equal(Path.Combine(_root, "plugins"), opened);
        Assert.True(Directory.Exists(opened));
    }

    [Fact]
    public void Open_log_folder_survives_the_folder_failing_to_open()
    {
        var detail = Detail("installed4", new PluginHostService { LogFiles = _files }, _ => throw new InvalidOperationException("explorer is unavailable"));

        detail.OpenLogFolderCommand.Execute(null);   // must not throw
    }

    private sealed class NoFilePicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }
}
