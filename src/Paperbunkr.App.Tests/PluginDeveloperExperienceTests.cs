using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Plugin API 4.5 slice A (docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §A1-A3): the folder watcher's
/// quiet-period batching, the host's dev-mode reload and its problem reporting, the log tail, and every scaffold template - the
/// script ones are discovered and run through the real <see cref="PluginEngine"/>, so a template that doesn't compile fails here.
/// </summary>
public sealed class PluginDeveloperExperienceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_plugindev_{Guid.NewGuid():N}");
    private readonly string _plugins;

    public PluginDeveloperExperienceTests()
    {
        _plugins = Path.Combine(_root, "plugins");
        Directory.CreateDirectory(_plugins);
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

    private static readonly string AppDir = AppContext.BaseDirectory;

    // ---- watcher ----

    [Theory]
    [InlineData(@"C:\p\a\plugin.xml", true)]
    [InlineData(@"C:\p\a\main.csx", true)]
    [InlineData(@"C:\p\a\main.PY", true)]
    [InlineData(@"C:\p\a\notes.txt", false)]
    [InlineData(@"C:\p\a\Module.cs", false)]
    public void Only_script_and_manifest_files_trigger_a_reload(string path, bool relevant) =>
        Assert.Equal(relevant, PluginFolderWatcher.IsRelevant(path));

    [Fact]
    public void A_burst_of_saves_becomes_one_batch_after_the_quiet_period()
    {
        var batches = new List<IReadOnlyList<string>>();
        using var fired = new ManualResetEventSlim();
        Directory.CreateDirectory(Path.Combine(_plugins, "a"));
        using var watcher = new PluginFolderWatcher(_plugins, TimeSpan.FromMilliseconds(300), paths =>
        {
            lock (batches) batches.Add(paths);
            fired.Set();
        });

        File.WriteAllText(Path.Combine(_plugins, "a", "main.csx"), "return 1;");
        File.WriteAllText(Path.Combine(_plugins, "a", "main.csx"), "return 2;");
        File.WriteAllText(Path.Combine(_plugins, "a", "plugin.xml"), "<Plugin/>");
        File.WriteAllText(Path.Combine(_plugins, "a", "ignored.txt"), "x");

        Assert.True(fired.Wait(TimeSpan.FromSeconds(10)));
        Thread.Sleep(600);  // nothing else should arrive
        lock (batches)
        {
            var batch = Assert.Single(batches);
            Assert.Contains(batch, p => p.EndsWith("main.csx", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(batch, p => p.EndsWith("plugin.xml", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(batch, p => p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void A_disposed_watcher_reports_nothing()
    {
        int calls = 0;
        var watcher = new PluginFolderWatcher(_plugins, TimeSpan.FromMilliseconds(100), _ => Interlocked.Increment(ref calls));
        watcher.Dispose();

        File.WriteAllText(Path.Combine(_plugins, "late.csx"), "return 1;");
        Thread.Sleep(500);

        Assert.Equal(0, calls);
    }

    // ---- scaffolder ----

    [Theory]
    [InlineData("My Sync Tool", "my-sync-tool")]
    [InlineData("  Hello__World!! ", "hello-world")]
    [InlineData("123 go", "123-go")]
    [InlineData("!!!", "")]
    public void The_key_is_derived_from_the_name(string name, string key) => Assert.Equal(key, PluginScaffolder.KeyFor(name));

    [Theory]
    [InlineData(PluginTemplate.ScriptCSharp)]
    [InlineData(PluginTemplate.ScriptPython)]
    public async Task A_script_template_discovers_runs_and_logs(PluginTemplate template)
    {
        var result = PluginScaffolder.Create(_plugins, "Starter", "starter", template, AppDir);
        Assert.True(result.Succeeded, result.Error);

        var env = new TestPluginEnvironment();
        var engine = new PluginEngine();
        engine.Discover(_plugins, env);

        Assert.Null(engine.PackageApiInfo["starter"].BlockedReason);
        Assert.True(engine.SettingsSchemas.ContainsKey("starter"));
        Command command = Assert.Single(engine.AllCommands);
        Assert.False(command.IsBroken, command.CompileError);
        Assert.Equal(PluginHooks.Library, command.Hook);

        var books = new List<Issue> { new() { Id = 1 }, new() { Id = 2 } };
        object? value = await command.InvokeAsync(new BooksHookGlobals { Environment = command.Environment!, Books = books });

        Assert.Equal("Hello! 2 book(s) selected.", value?.ToString());
    }

    [Fact]
    public void The_native_template_writes_a_manifest_project_module_and_readme()
    {
        var result = PluginScaffolder.Create(_plugins, "My Native Thing", "my-native-thing", PluginTemplate.Native, AppDir);

        Assert.True(result.Succeeded, result.Error);
        string folder = result.Folder!;
        string manifest = File.ReadAllText(Path.Combine(folder, "plugin.xml"));
        Assert.Contains("tier=\"Native\"", manifest);
        Assert.Contains("assembly=\"MyNativeThing.dll\"", manifest);
        string project = File.ReadAllText(Path.Combine(folder, "MyNativeThing.csproj"));
        Assert.Contains(Path.Combine(AppDir, "Paperbunkr.Plugins.Abstractions.dll"), project);
        Assert.Contains("<Private>false</Private>", project);
        Assert.Contains("RegisterCommands(INativeCommandRegistrar registrar)", File.ReadAllText(Path.Combine(folder, "Module.cs")));
        Assert.Contains("dotnet build", File.ReadAllText(Path.Combine(folder, "README.txt")));
    }

    [Fact]
    public void An_existing_folder_or_an_empty_name_is_refused()
    {
        Assert.True(PluginScaffolder.Create(_plugins, "Twice", "twice", PluginTemplate.ScriptCSharp, AppDir).Succeeded);

        var again = PluginScaffolder.Create(_plugins, "Twice", "twice", PluginTemplate.ScriptPython, AppDir);
        Assert.False(again.Succeeded);
        Assert.Contains("already exists", again.Error);

        Assert.False(PluginScaffolder.Create(_plugins, "  ", "x", PluginTemplate.ScriptCSharp, AppDir).Succeeded);
        Assert.False(PluginScaffolder.Create(_plugins, "Name", "!!", PluginTemplate.ScriptCSharp, AppDir).Succeeded);
    }

    // ---- host reload ----

    private PluginHostService Host(TestPluginEnvironment env, ActivityService activity)
    {
        string db = Path.Combine(_root, "host.db");
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Paperbunkr.Data.PaperbunkrDbContext>()
            .UseSqlite($"Data Source={db}").Options;
        using (var context = new Paperbunkr.Data.PaperbunkrDbContext(options))
        {
            context.Database.EnsureCreated();
        }

        var host = new PluginHostService
        {
            PluginsRoot = () => _plugins,
            PostToUi = a => a(),
            LogFiles = new PluginLogFiles(() => Path.Combine(_root, "logs")),
            ActivityForAlerts = activity,
            ContextFactory = () => new Paperbunkr.Data.PaperbunkrDbContext(options),
        };
        host.InitializeForTests(env);
        return host;
    }

    private static ActivityService Activity() => new(dispatch: a => a(), recordRun: _ => { });

    [Fact]
    public void A_reload_after_a_broken_edit_raises_one_alert_and_a_fix_is_silent()
    {

        PluginScaffolder.Create(_plugins, "Edit Me", "edit-me", PluginTemplate.ScriptCSharp, AppDir);
        var activity = Activity();
        var host = Host(new TestPluginEnvironment(), activity);
        var reloaded = new List<IReadOnlyList<string>>();
        host.PluginsReloaded += keys => reloaded.Add(keys);
        string script = Path.Combine(_plugins, "edit-me", "main.csx");

        File.WriteAllText(script, "this is not C# ;;;");
        host.ReloadChanged(new[] { script });

        var alert = Assert.Single(activity.Alerts);
        Assert.Equal("Edit Me didn't reload", alert.Title);
        Assert.Equal("plugin:edit-me:reload", alert.DedupeKey);
        Assert.Equal(new[] { "edit-me" }, reloaded.Single());

        File.WriteAllText(script, "return \"fixed\";");
        host.ReloadChanged(new[] { script });

        Assert.Single(activity.Alerts);
        Assert.False(host.Engine.AllCommands.Single(c => c.PluginKey == "edit-me").IsBroken);
        Assert.Contains("Reloaded after a file change.", File.ReadAllText(host.LogFiles.PathFor("edit-me")));
    }

    [Fact]
    public void A_new_plugin_folder_is_picked_up_by_a_reload()
    {

        var host = Host(new TestPluginEnvironment(), Activity());
        Assert.Empty(host.Engine.AllCommands);

        var created = PluginScaffolder.Create(_plugins, "Fresh", "fresh", PluginTemplate.ScriptPython, AppDir);
        host.ReloadChanged(new[] { Path.Combine(created.Folder!, "plugin.xml") });

        Assert.Equal("fresh", Assert.Single(host.Engine.AllCommands).PluginKey);
    }

    [Fact]
    public void Turning_developer_mode_on_and_off_starts_and_stops_watching()
    {

        var host = Host(new TestPluginEnvironment(), Activity());
        host.ReloadQuietPeriod = TimeSpan.FromMilliseconds(200);
        using var reloaded = new ManualResetEventSlim();
        host.PluginsReloaded += _ => reloaded.Set();

        host.DeveloperMode = true;
        PluginScaffolder.Create(_plugins, "Watched", "watched", PluginTemplate.ScriptCSharp, AppDir);
        Assert.True(reloaded.Wait(TimeSpan.FromSeconds(10)));

        host.DeveloperMode = false;
        reloaded.Reset();
        File.WriteAllText(Path.Combine(_plugins, "watched", "main.csx"), "return 3;");
        Assert.False(reloaded.Wait(TimeSpan.FromMilliseconds(800)));
    }

    // ---- log tail ----

    [Fact]
    public void The_log_tail_keeps_the_last_lines_and_clear_empties_the_live_file()
    {
        var files = new PluginLogFiles(() => Path.Combine(_root, "logs"));
        Assert.Empty(files.ReadTail("p", 5));
        for (int i = 1; i <= 8; i++)
        {
            files.Write("p", "INF", $"line {i}");
        }

        string[] tail = files.ReadTail("p", 3);
        Assert.Equal(3, tail.Length);
        Assert.EndsWith("line 6", tail[0]);
        Assert.EndsWith("line 8", tail[2]);

        files.Clear("p");
        Assert.Empty(files.ReadTail("p", 3));
    }

    [Fact]
    public void Developer_mode_survives_a_restart_and_defaults_to_off()
    {
        string saved = PluginDeveloperSettings.FilePath;
        PluginDeveloperSettings.FilePath = Path.Combine(_root, "plugin-developer.json");
        try
        {
            Assert.False(PluginDeveloperSettings.LoadDeveloperMode());
            PluginDeveloperSettings.SaveDeveloperMode(true);
            Assert.True(PluginDeveloperSettings.LoadDeveloperMode());
            File.WriteAllText(PluginDeveloperSettings.FilePath, "{ not json");
            Assert.False(PluginDeveloperSettings.LoadDeveloperMode());
        }
        finally
        {
            PluginDeveloperSettings.FilePath = saved;
        }
    }
}
