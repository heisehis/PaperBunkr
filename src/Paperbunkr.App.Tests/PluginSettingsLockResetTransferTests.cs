using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Locked settings, reset to defaults, and export/import (docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md
/// sections 4-6): the access layer, the transfer format, and the overlay view-model on top. Real SQLite, real manifests through the
/// real engine, a fake secret protector and a scripted file picker. In the Avalonia collection because the two-step confirms use a
/// <c>DispatcherTimer</c>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class PluginSettingsLockResetTransferTests : IDisposable
{
    private const string Key = "sync-plugin";

    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly string _pluginsRoot;
    private readonly string _files;

    public PluginSettingsLockResetTransferTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_settingslock_test_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        _pluginsRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_settingslock_plugins_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_pluginsRoot);
        _files = Path.Combine(Path.GetTempPath(), $"paperbunkr_settingslock_files_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_files);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_pluginsRoot)) Directory.Delete(_pluginsRoot, recursive: true);
            if (Directory.Exists(_files)) Directory.Delete(_files, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeProtector : ISecretProtector
    {
        public bool IsAvailable => true;
        public string Protect(string plaintext) => "fake:" + plaintext;
        public string? TryUnprotect(string stored) => stored.StartsWith("fake:", StringComparison.Ordinal) ? stored["fake:".Length..] : null;
        public bool IsProtected(string stored) => stored.StartsWith("fake:", StringComparison.Ordinal);
    }

    private sealed class ScriptedFilePicker : IFilePickerService
    {
        public string? OpenPath { get; set; }
        public string? SavePath { get; set; }
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult(OpenPath);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult(SavePath);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private const string Declares = """
        <Settings>
          <Setting key="server" label="Server" type="text" default="https://default.example" locked="true"/>
          <Setting key="mode" label="Mode" type="choice" default="fast">
            <Choice value="fast" label="Fast"/>
            <Choice value="thorough" label="Thorough"/>
          </Setting>
          <Setting key="limit" label="Limit" type="number" default="10" min="1" max="100"/>
          <Setting key="token" label="API token" type="secret"/>
        </Settings>
        """;

    private (PluginSettingsAccess Access, PluginSettingsSchema Schema) Build()
    {
        string dir = Path.Combine(_pluginsRoot, Key);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), $"""
            <Plugin key="{Key}" name="Sync Plugin">
              <Command hook="Startup" key="{Key}.startup" name="Startup" script="run.csx" />
              {Declares}
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "run.csx"), "return 1;");
        var engine = new PluginEngine();
        engine.Discover(_pluginsRoot, new TestPluginEnvironment());
        var access = new PluginSettingsAccess(() => new PaperbunkrDbContext(_options), k => engine.SettingsSchemas.GetValueOrDefault(k), new FakeProtector());
        return (access, engine.SettingsSchemas[Key]);
    }

    private static PluginSettingRowViewModel Row(PluginSettingsSchemaViewModel vm, string key) => vm.Rows.Single(r => r.Key == key);

    // ================================================================ locked

    [Fact]
    public void A_locked_setting_with_no_stored_value_is_still_editable()
    {
        var (access, schema) = Build();
        var vm = new PluginSettingsSchemaViewModel(Key, "Sync Plugin", schema, access);

        var row = Row(vm, "server");
        Assert.False(row.IsLocked);
        Assert.True(row.IsEnabled);

        row.Text = "https://mine.example";     // first-time setup

        Assert.Equal("https://mine.example", access.Get(Key, "server"));
        Assert.False(row.IsLocked);            // saving didn't lock it mid-session
    }

    [Fact]
    public void A_locked_setting_with_a_stored_value_opens_locked_and_disabled()
    {
        var (access, schema) = Build();
        access.Set(Key, "server", "https://mine.example");

        var row = Row(new PluginSettingsSchemaViewModel(Key, "Sync Plugin", schema, access), "server");

        Assert.True(row.IsLocked);
        Assert.False(row.IsEnabled);
        Assert.False(row.CanReset);
    }

    [Fact]
    public void A_setting_that_isnt_declared_locked_is_never_locked()
    {
        var (access, schema) = Build();
        access.Set(Key, "limit", "20");

        var row = Row(new PluginSettingsSchemaViewModel(Key, "Sync Plugin", schema, access), "limit");

        Assert.False(row.IsLocked);
        Assert.True(row.IsEnabled);
    }

    [Fact]
    public void Unlocking_takes_two_steps_and_then_allows_editing()
    {
        var (access, schema) = Build();
        access.Set(Key, "server", "https://mine.example");
        var row = Row(new PluginSettingsSchemaViewModel(Key, "Sync Plugin", schema, access), "server");

        row.Unlock.TriggerCommand.Execute(null);
        Assert.True(row.IsLocked);            // armed, not yet unlocked
        Assert.True(row.Unlock.IsArmed);

        row.Unlock.TriggerCommand.Execute(null);
        Assert.False(row.IsLocked);
        Assert.True(row.IsEnabled);

        row.Text = "https://other.example";
        Assert.Equal("https://other.example", access.Get(Key, "server"));
    }

    [Fact]
    public void The_plugins_own_SetSetting_is_unaffected_by_a_lock()
    {
        var (access, _) = Build();
        access.Set(Key, "server", "https://first.example");

        access.Set(Key, "server", "https://second.example");

        Assert.Equal("https://second.example", access.Get(Key, "server"));
    }

    // ================================================================ reset

    [Fact]
    public void Reset_removes_the_stored_value_so_reads_fall_back_to_the_default()
    {
        var (access, _) = Build();
        access.Set(Key, "limit", "50");

        Assert.True(access.Reset(Key, "limit"));

        Assert.Null(access.GetRaw(Key, "limit"));
        Assert.Equal("10", access.Get(Key, "limit"));
        Assert.False(access.Reset(Key, "limit"));   // nothing left to reset
    }

    [Fact]
    public void Row_reset_returns_the_editor_to_the_default_in_place()
    {
        var (access, schema) = Build();
        var vm = new PluginSettingsSchemaViewModel(Key, "Sync Plugin", schema, access);
        var row = Row(vm, "limit");
        row.Text = "50";
        Assert.True(row.CanReset);

        row.ResetCommand.Execute(null);

        Assert.Equal("10", row.Text);
        Assert.False(row.CanReset);
        Assert.Null(access.GetRaw(Key, "limit"));
    }

    [Fact]
    public void Resetting_a_secret_clears_it()
    {
        var (access, schema) = Build();
        access.Set(Key, "token", "hunter2");
        var row = Row(new PluginSettingsSchemaViewModel(Key, "Sync Plugin", schema, access), "token");

        row.ResetCommand.Execute(null);

        Assert.Equal(string.Empty, row.Text);
        Assert.Null(access.GetRaw(Key, "token"));
    }

    [Fact]
    public void Reset_all_resets_unlocked_settings_and_skips_locked_ones_saying_so()
    {
        var (access, schema) = Build();
        access.Set(Key, "server", "https://mine.example");   // locked
        access.Set(Key, "mode", "thorough");
        access.Set(Key, "limit", "50");
        var vm = new PluginSettingsSchemaViewModel(Key, "Sync Plugin", schema, access);

        vm.ResetAll.TriggerCommand.Execute(null);
        Assert.NotNull(access.GetRaw(Key, "mode"));            // first click only arms it
        vm.ResetAll.TriggerCommand.Execute(null);

        Assert.Null(access.GetRaw(Key, "mode"));
        Assert.Null(access.GetRaw(Key, "limit"));
        Assert.Equal("https://mine.example", access.GetRaw(Key, "server"));
        Assert.Contains("Reset 2 settings", vm.Status);
        Assert.Contains("1 locked setting was skipped", vm.Status);
    }

    // ================================================================ export / import

    [Fact]
    public void Export_writes_the_documented_shape_and_leaves_secrets_out()
    {
        var (access, schema) = Build();
        access.Set(Key, "mode", "thorough");
        access.Set(Key, "limit", "50");
        access.Set(Key, "token", "hunter2");
        access.Set(Key, "extra", "undeclared value");

        string json = PluginSettingsTransfer.Export(access, Key, "Sync Plugin", schema, new DateTime(2026, 9, 20, 13, 45, 1, DateTimeKind.Utc));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("paperbunkr-plugin-settings", root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(Key, root.GetProperty("plugin").GetString());
        Assert.Equal("Sync Plugin", root.GetProperty("pluginName").GetString());
        Assert.Equal("4.2", root.GetProperty("apiVersion").GetString());
        Assert.Equal("2026-09-20T13:45:01Z", root.GetProperty("exportedUtc").GetString());
        Assert.Equal(new[] { "token" }, root.GetProperty("secretsExcluded").EnumerateArray().Select(e => e.GetString()));
        var settings = root.GetProperty("settings");
        Assert.Equal(new[] { "extra", "limit", "mode" }, settings.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("hunter2", json);
    }

    private static string FileFor(string plugin, string settingsJson, string format = "paperbunkr-plugin-settings", int version = 1) =>
        $$"""{"format":"{{format}}","version":{{version}},"plugin":"{{plugin}}","settings":{{settingsJson}}}""";

    [Fact]
    public void Import_round_trips_an_export_into_a_fresh_store()
    {
        var (access, schema) = Build();
        access.Set(Key, "mode", "thorough");
        access.Set(Key, "limit", "50");
        access.Set(Key, "extra", "kept");
        string json = PluginSettingsTransfer.Export(access, Key, "Sync Plugin", schema);
        access.Reset(Key, "mode");
        access.Reset(Key, "limit");
        access.Reset(Key, "extra");

        var result = PluginSettingsTransfer.Import(access, Key, schema, json);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Imported);
        Assert.Empty(result.Skipped);
        Assert.Equal("thorough", access.Get(Key, "mode"));
        Assert.Equal("50", access.Get(Key, "limit"));
        Assert.Equal("kept", access.Get(Key, "extra"));
    }

    [Fact]
    public void Import_skips_invalid_values_secrets_and_locked_settings_with_a_value()
    {
        var (access, schema) = Build();
        access.Set(Key, "server", "https://mine.example");   // locked with a value
        string json = FileFor(Key, """{"server":"https://evil.example","mode":"warp","limit":"500","token":"stolen","extra":"ok"}""");

        var result = PluginSettingsTransfer.Import(access, Key, schema, json);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Imported);                     // only "extra"
        Assert.Equal(4, result.Skipped.Count);
        Assert.Equal("https://mine.example", access.Get(Key, "server"));
        Assert.Equal("fast", access.Get(Key, "mode"));        // invalid choice -> not written -> default
        Assert.Equal("10", access.Get(Key, "limit"));
        Assert.Null(access.GetRaw(Key, "token"));
        Assert.Equal("ok", access.Get(Key, "extra"));
    }

    [Fact]
    public void Import_writes_a_locked_setting_that_has_no_value_yet()
    {
        var (access, schema) = Build();

        var result = PluginSettingsTransfer.Import(access, Key, schema, FileFor(Key, """{"server":"https://mine.example"}"""));

        Assert.Equal(1, result.Imported);
        Assert.Equal("https://mine.example", access.Get(Key, "server"));
    }

    [Theory]
    [InlineData("not json at all", "valid JSON")]
    [InlineData("""{"format":"something-else","version":1,"plugin":"sync-plugin","settings":{}}""", "isn't a Paperbunkr plugin settings file")]
    [InlineData("""{"format":"paperbunkr-plugin-settings","version":2,"plugin":"sync-plugin","settings":{}}""", "different version")]
    [InlineData("""{"format":"paperbunkr-plugin-settings","version":1,"plugin":"other-plugin","settings":{"mode":"thorough"}}""", "'other-plugin'")]
    [InlineData("""{"format":"paperbunkr-plugin-settings","version":1,"plugin":"sync-plugin"}""", "no settings")]
    public void Import_refuses_a_bad_file_and_writes_nothing(string json, string expectedInError)
    {
        var (access, schema) = Build();

        var result = PluginSettingsTransfer.Import(access, Key, schema, json);

        Assert.False(result.Succeeded);
        Assert.Contains(expectedInError, result.Error);
        Assert.Empty(access.StoredEntries(Key));
    }

    // ---- through the overlay view-model

    [Fact]
    public async Task The_overlay_exports_to_the_picked_path_and_reports_the_left_out_secret()
    {
        var (access, schema) = Build();
        access.Set(Key, "mode", "thorough");
        string target = Path.Combine(_files, "out.json");
        var vm = new PluginSettingsSchemaViewModel(Key, "Sync Plugin", schema, access, new ScriptedFilePicker { SavePath = target });

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Contains("\"mode\": \"thorough\"", File.ReadAllText(target));
        Assert.Contains("1 secret setting was left out", vm.Status);
    }

    [Fact]
    public async Task A_cancelled_export_does_nothing()
    {
        var (access, schema) = Build();
        var vm = new PluginSettingsSchemaViewModel(Key, "Sync Plugin", schema, access, new ScriptedFilePicker());

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Null(vm.Status);
    }

    [Fact]
    public async Task The_overlay_imports_and_refreshes_its_rows_in_place()
    {
        var (access, schema) = Build();
        string source = Path.Combine(_files, "in.json");
        File.WriteAllText(source, FileFor(Key, """{"mode":"thorough","limit":"500"}"""));
        var vm = new PluginSettingsSchemaViewModel(Key, "Sync Plugin", schema, access, new ScriptedFilePicker { OpenPath = source });
        var rowsBefore = vm.Rows.ToList();

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(rowsBefore, vm.Rows);                        // same row objects - nothing was cleared and rebuilt
        Assert.Equal("Thorough", Row(vm, "mode").Text);
        Assert.Equal("10", Row(vm, "limit").Text);                // the invalid value was skipped
        Assert.Contains("Imported 1 setting; skipped 1", vm.Status);
    }

    [Fact]
    public async Task An_unreadable_import_file_is_reported_not_thrown()
    {
        var (access, schema) = Build();
        var vm = new PluginSettingsSchemaViewModel(Key, "Sync Plugin", schema, access, new ScriptedFilePicker { OpenPath = Path.Combine(_files, "missing.json") });

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Contains("Couldn't read the file", vm.Status);
    }
}
