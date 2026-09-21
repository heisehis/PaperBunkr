using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Plugin settings storage and the settings overlay's view-model (docs/superpowers/specs/2026-09-20-
/// plugin-api-4-1-design.md §6): what a plugin sees, what's persisted, secrets encrypted at rest, and what the
/// overlay shows, accepts and refuses. Real SQLite, a fake secret protector (the real DPAPI round-trip is
/// covered in Paperbunkr.Data.Tests), real manifests through the real engine.
/// </summary>
public sealed class PluginSettingsTests : IDisposable
{
    private const string Key = "sync-plugin";

    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly string _pluginsRoot;
    private readonly FakeProtector _protector = new();

    public PluginSettingsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pluginsettings_test_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Database.EnsureCreated();
        }

        _pluginsRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_pluginsettings_plugins_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_pluginsRoot);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_pluginsRoot)) Directory.Delete(_pluginsRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeProtector : ISecretProtector
    {
        public bool IsAvailable { get; set; } = true;

        public string Protect(string plaintext) =>
            IsAvailable ? "fake:" + new string(plaintext.Reverse().ToArray()) : throw new PlatformNotSupportedException();

        public string? TryUnprotect(string stored) =>
            IsAvailable && stored.StartsWith("fake:", StringComparison.Ordinal) ? new string(stored["fake:".Length..].Reverse().ToArray()) : null;

        public bool IsProtected(string stored) => stored.StartsWith("fake:", StringComparison.Ordinal) || stored.StartsWith("bad:", StringComparison.Ordinal);
    }

    private PaperbunkrDbContext NewContext() => new(_options);

    private const string DeclaresEverything = """
        <Settings>
          <Setting key="url" label="API URL" type="text" default="https://default.example"/>
          <Setting key="mode" label="Mode" type="choice" default="fast">
            <Choice value="fast" label="Fast"/>
            <Choice value="thorough" label="Thorough"/>
          </Setting>
          <Setting key="limit" label="Limit" type="number" default="10" min="1" max="100"/>
          <Setting key="enabled" label="Enabled" type="toggle" default="true"/>
          <Setting key="token" label="API token" type="secret"/>
        </Settings>
        """;

    /// <summary>Discovers a real plugin declaring <paramref name="settingsXml"/> and returns access wired to its schema, the way the host does.</summary>
    private PluginSettingsAccess AccessFor(string settingsXml = DeclaresEverything)
    {
        string dir = Path.Combine(_pluginsRoot, Key);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), $"""
            <Plugin key="{Key}" name="Sync Plugin">
              <Command hook="Startup" key="{Key}.startup" name="Startup" script="run.csx" />
              {settingsXml}
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "run.csx"), "return 1;");

        var engine = new PluginEngine();
        engine.Discover(_pluginsRoot, new TestPluginEnvironment());
        return new PluginSettingsAccess(NewContext, k => engine.SettingsSchemas.GetValueOrDefault(k), _protector);
    }

    private string? Stored(string key)
    {
        using var context = NewContext();
        return context.PluginSettingStates.Where(s => s.PluginKey == Key && s.Key == key).Select(s => s.Value).FirstOrDefault();
    }

    private void StoreRaw(string key, string value)
    {
        using var context = NewContext();
        context.PluginSettingStates.Add(new Paperbunkr.Data.Entities.PluginSettingState { PluginKey = Key, Key = key, Value = value });
        context.SaveChanges();
    }

    // ================================================================ what a plugin sees

    [Fact]
    public void An_undeclared_key_is_read_and_written_verbatim_exactly_as_before()
    {
        var access = AccessFor();

        Assert.Null(access.Get(Key, "anything"));
        access.Set(Key, "anything", "whatever, even 999 or maybe");

        Assert.Equal("whatever, even 999 or maybe", access.Get(Key, "anything"));
        Assert.Equal("whatever, even 999 or maybe", Stored("anything"));
    }

    [Fact]
    public void A_plugin_with_no_schema_at_all_behaves_exactly_as_before()
    {
        var access = new PluginSettingsAccess(NewContext, _ => null, _protector);

        access.Set(Key, "k", "v");

        Assert.Equal("v", access.Get(Key, "k"));
    }

    [Fact]
    public void An_unset_declared_key_reads_as_its_declared_default()
    {
        var access = AccessFor();

        Assert.Equal("https://default.example", access.Get(Key, "url"));
        Assert.Equal("fast", access.Get(Key, "mode"));
        Assert.Equal("10", access.Get(Key, "limit"));
        Assert.Equal("true", access.Get(Key, "enabled"));
        Assert.Null(access.Get(Key, "token"));   // a secret has no default
    }

    [Fact]
    public void A_valid_stored_value_is_returned_as_stored()
    {
        var access = AccessFor();
        access.Set(Key, "limit", "42");
        access.Set(Key, "mode", "thorough");

        Assert.Equal("42", access.Get(Key, "limit"));
        Assert.Equal("thorough", access.Get(Key, "mode"));
    }

    [Theory]
    [InlineData("limit", "999", "10")]            // out of range
    [InlineData("limit", "abc", "10")]            // not a number
    [InlineData("mode", "slow", "fast")]          // not an offered choice
    [InlineData("enabled", "maybe", "true")]      // not a boolean
    public void A_stored_value_that_violates_the_schema_reads_as_the_default_but_is_never_rewritten(string key, string bad, string expectedDefault)
    {
        var access = AccessFor();
        StoreRaw(key, bad);

        Assert.Equal(expectedDefault, access.Get(Key, key));

        // The read did not "repair" it: the raw string is still there for the UI to show.
        Assert.Equal(bad, Stored(key));
        Assert.Equal(bad, access.GetRaw(Key, key));
        Assert.Equal(expectedDefault, access.Get(Key, key));
        Assert.Equal(bad, Stored(key));
    }

    [Fact]
    public void A_plugin_can_write_a_schema_violating_value_but_never_reads_it_back()
    {
        var access = AccessFor();

        access.Set(Key, "limit", "5000");

        Assert.Equal("5000", Stored("limit"));
        Assert.Equal("10", access.Get(Key, "limit"));
    }

    // ================================================================ secrets

    [Fact]
    public void A_secret_is_encrypted_at_rest_and_decrypted_for_the_plugin()
    {
        var access = AccessFor();

        access.Set(Key, "token", "hunter2");

        string stored = Stored("token")!;
        Assert.NotEqual("hunter2", stored);
        Assert.DoesNotContain("hunter2", stored);
        Assert.True(_protector.IsProtected(stored));
        Assert.Equal("hunter2", access.Get(Key, "token"));
    }

    [Fact]
    public void An_empty_secret_is_stored_empty_not_encrypted()
    {
        var access = AccessFor();

        access.Set(Key, "token", "");

        Assert.Equal("", Stored("token"));
        Assert.Equal("", access.Get(Key, "token"));
    }

    [Fact]
    public void A_secret_that_will_not_decrypt_reads_as_unset_and_the_stored_value_is_left_alone()
    {
        var access = AccessFor();
        StoreRaw("token", "bad:cipher-from-another-account");

        Assert.Null(access.Get(Key, "token"));
        Assert.Equal("bad:cipher-from-another-account", Stored("token"));
    }

    [Fact]
    public void A_secret_stored_as_plain_text_before_the_schema_existed_is_encrypted_in_place_on_first_read()
    {
        var access = AccessFor();
        StoreRaw("token", "legacy-plain-key");

        Assert.Equal("legacy-plain-key", access.Get(Key, "token"));

        string after = Stored("token")!;
        Assert.True(_protector.IsProtected(after));
        Assert.DoesNotContain("legacy-plain-key", after);
        Assert.Equal("legacy-plain-key", access.Get(Key, "token"));   // and still readable
    }

    [Fact]
    public void Where_secrets_cannot_be_protected_writing_one_fails_closed_instead_of_storing_plain_text()
    {
        var access = AccessFor();
        _protector.IsAvailable = false;

        Assert.Throws<InvalidOperationException>(() => access.Set(Key, "token", "hunter2"));
        Assert.Null(Stored("token"));
    }

    [Fact]
    public void A_non_secret_is_never_encrypted()
    {
        var access = AccessFor();

        access.Set(Key, "url", "https://real.example");

        Assert.Equal("https://real.example", Stored("url"));
    }

    [Fact]
    public void One_plugins_settings_are_invisible_to_another()
    {
        var access = AccessFor();
        access.Set(Key, "url", "mine");

        Assert.Null(access.Get("some-other-plugin", "url"));
    }

    // ================================================================ what the overlay edits

    [Fact]
    public void GetForEditing_shows_an_invalid_stored_value_as_is_so_the_user_can_correct_it()
    {
        var access = AccessFor();
        StoreRaw("limit", "5000");
        var limit = access.SchemaFor(Key)!.Find("limit")!;

        Assert.Equal("5000", access.GetForEditing(Key, limit, out bool unreadable));
        Assert.False(unreadable);
    }

    [Fact]
    public void GetForEditing_shows_the_default_when_unset_and_empty_for_a_secret()
    {
        var access = AccessFor();

        Assert.Equal("10", access.GetForEditing(Key, access.SchemaFor(Key)!.Find("limit")!, out _));
        Assert.Equal("", access.GetForEditing(Key, access.SchemaFor(Key)!.Find("token")!, out _));
    }

    [Fact]
    public void GetForEditing_decrypts_a_secret_and_flags_one_that_cannot_be_read()
    {
        var access = AccessFor();
        var token = access.SchemaFor(Key)!.Find("token")!;

        access.Set(Key, "token", "hunter2");
        Assert.Equal("hunter2", access.GetForEditing(Key, token, out bool unreadable));
        Assert.False(unreadable);

        StoreRaw("other", "x");   // (unrelated row - just proving the lookup is per key)
        using (var context = NewContext())
        {
            context.PluginSettingStates.Single(s => s.Key == "token").Value = "bad:from-elsewhere";
            context.SaveChanges();
        }

        Assert.Equal("", access.GetForEditing(Key, token, out unreadable));
        Assert.True(unreadable);
    }

    // ================================================================ the real environment delegates to it

    [Fact]
    public void The_real_environment_reads_and_writes_through_the_schema_aware_access()
    {
        var access = AccessFor();
        var environment = new PaperbunkrPluginEnvironment
        {
            MainWindow = null!, App = null!, OpenBooks = null!, Browser = null!, ComicDisplay = null!,
            Metadata = null!, Rules = null!, Writer = null!, ThemePlugin = null!,
            ActivityService = new Paperbunkr.App.Services.ActivityService(dispatch: a => a(), recordRun: _ => { }),
            SettingsAccess = access,
            PluginKey = Key,
        };

        Assert.Equal("10", environment.GetSetting("limit"));       // default via the schema
        environment.SetSetting("token", "hunter2");
        Assert.NotEqual("hunter2", Stored("token"));               // encrypted via the schema
        Assert.Equal("hunter2", environment.GetSetting("token"));

        // A clone (one per command) shares the same access and resolves its own plugin key.
        var clone = (PaperbunkrPluginEnvironment)environment.Clone();
        Assert.Equal("hunter2", clone.GetSetting("token"));
    }

    [Fact]
    public void The_host_resolves_schemas_from_the_engines_current_discovery()
    {
        AccessFor();   // writes the plugin to disk
        var host = new Paperbunkr.App.Plugins.PluginHostService { ContextFactory = NewContext };
        host.Engine.Discover(_pluginsRoot, new TestPluginEnvironment());

        Assert.Equal("10", host.Settings.Get(Key, "limit"));

        // A re-discovery that no longer has the plugin drops the schema, and reads become verbatim again.
        Directory.Delete(Path.Combine(_pluginsRoot, Key), recursive: true);
        host.Engine.Discover(_pluginsRoot, new TestPluginEnvironment());
        Assert.Null(host.Settings.Get(Key, "limit"));
    }

    // ================================================================ the overlay's view-model

    private PluginSettingsSchemaViewModel ViewModel(PluginSettingsAccess access) =>
        new(Key, "Sync Plugin", access.SchemaFor(Key)!, access);

    private static PluginSettingRowViewModel Row(PluginSettingsSchemaViewModel vm, string key) => vm.Rows.Single(r => r.Key == key);

    [Fact]
    public void The_overlay_has_one_row_per_declared_setting_in_order_titled_with_the_plugin_name()
    {
        var vm = ViewModel(AccessFor());

        Assert.Equal("Sync Plugin settings", vm.Title);
        Assert.Equal(new[] { "url", "mode", "limit", "enabled", "token" }, vm.Rows.Select(r => r.Key));
        Assert.Equal("API URL", Row(vm, "url").Label);
    }

    [Fact]
    public void Each_row_knows_its_type_for_the_template()
    {
        var vm = ViewModel(AccessFor());

        Assert.True(Row(vm, "url").IsText);
        Assert.True(Row(vm, "mode").IsChoice);
        Assert.True(Row(vm, "limit").IsNumber);
        Assert.True(Row(vm, "enabled").IsToggle);
        Assert.True(Row(vm, "token").IsSecret);
        Assert.Equal(new[] { "Fast", "Thorough" }, Row(vm, "mode").ChoiceLabels);
    }

    [Fact]
    public void Rows_open_showing_the_default_or_the_stored_value()
    {
        var access = AccessFor();
        access.Set(Key, "limit", "42");
        var vm = ViewModel(access);

        Assert.Equal("https://default.example", Row(vm, "url").Text);
        Assert.Equal("Fast", Row(vm, "mode").Text);            // the default's label, not its value
        Assert.Equal("42", Row(vm, "limit").Text);
        Assert.True(Row(vm, "enabled").IsOn);
        Assert.Equal("", Row(vm, "token").Text);
    }

    [Fact]
    public void Opening_the_overlay_saves_nothing()
    {
        var access = AccessFor();

        ViewModel(access);

        using var context = NewContext();
        Assert.Empty(context.PluginSettingStates);
    }

    [Fact]
    public void A_valid_edit_is_saved_immediately()
    {
        var access = AccessFor();
        var vm = ViewModel(access);

        Row(vm, "url").Text = "https://new.example";
        Row(vm, "limit").Text = "55";

        Assert.Equal("https://new.example", Stored("url"));
        Assert.Equal("55", Stored("limit"));
        Assert.Null(Row(vm, "limit").Error);
        Assert.False(Row(vm, "limit").HasError);
    }

    [Fact]
    public void A_toggle_saves_true_and_false()
    {
        var access = AccessFor();
        var vm = ViewModel(access);

        Row(vm, "enabled").IsOn = false;
        Assert.Equal("false", Stored("enabled"));

        Row(vm, "enabled").IsOn = true;
        Assert.Equal("true", Stored("enabled"));
    }

    [Fact]
    public void A_choice_saves_its_value_not_its_label()
    {
        var access = AccessFor();
        var vm = ViewModel(access);

        Row(vm, "mode").Text = "Thorough";

        Assert.Equal("thorough", Stored("mode"));
    }

    [Fact]
    public void A_choice_label_that_is_not_offered_is_refused_and_not_saved()
    {
        var access = AccessFor();
        var vm = ViewModel(access);

        Row(vm, "mode").Text = "Slow";

        Assert.Null(Stored("mode"));
        Assert.Contains("listed options", Row(vm, "mode").Error);
        Assert.True(Row(vm, "mode").HasError);
    }

    [Theory]
    [InlineData("0", "at least 1")]
    [InlineData("101", "at most 100")]
    [InlineData("many", "Must be a number")]
    public void An_invalid_number_is_refused_with_an_inline_reason_and_the_old_value_is_kept(string typed, string expected)
    {
        var access = AccessFor();
        access.Set(Key, "limit", "20");
        var vm = ViewModel(access);

        Row(vm, "limit").Text = typed;

        Assert.Equal("20", Stored("limit"));
        Assert.Contains(expected, Row(vm, "limit").Error);
    }

    [Fact]
    public void Fixing_an_invalid_edit_clears_the_error_and_saves()
    {
        var access = AccessFor();
        var vm = ViewModel(access);
        Row(vm, "limit").Text = "9999";
        Assert.True(Row(vm, "limit").HasError);

        Row(vm, "limit").Text = "50";

        Assert.False(Row(vm, "limit").HasError);
        Assert.Equal("50", Stored("limit"));
    }

    [Fact]
    public void A_number_is_trimmed_before_it_is_saved()
    {
        var access = AccessFor();
        var vm = ViewModel(access);

        Row(vm, "limit").Text = "  7  ";

        Assert.Equal("7", Stored("limit"));
    }

    [Fact]
    public void A_secret_typed_in_the_overlay_is_encrypted_at_rest()
    {
        var access = AccessFor();
        var vm = ViewModel(access);

        Row(vm, "token").Text = "hunter2";

        Assert.NotEqual("hunter2", Stored("token"));
        Assert.Equal("hunter2", access.Get(Key, "token"));
    }

    [Fact]
    public void An_invalid_value_already_stored_is_shown_and_flagged_but_not_rewritten()
    {
        var access = AccessFor();
        StoreRaw("limit", "5000");

        var vm = ViewModel(access);

        Assert.Equal("5000", Row(vm, "limit").Text);            // shown as it is, so the user can fix it
        Assert.True(Row(vm, "limit").HasError);
        Assert.Contains("'5000' isn't valid", Row(vm, "limit").Error);
        Assert.Contains("default ('10')", Row(vm, "limit").Error);   // and says what the plugin is using meanwhile
        Assert.Equal("5000", Stored("limit"));                  // opening the overlay never repairs it
    }

    [Fact]
    public void A_valid_stored_value_opens_without_an_error()
    {
        var access = AccessFor();
        access.Set(Key, "limit", "20");

        Assert.False(Row(ViewModel(access), "limit").HasError);
    }

    [Fact]
    public void A_secret_that_cannot_be_read_back_is_flagged_for_re_entry()
    {
        var access = AccessFor();
        StoreRaw("token", "bad:from-another-account");

        var row = Row(ViewModel(access), "token");

        Assert.Equal("", row.Text);
        Assert.Contains("Enter it again", row.Error);
    }

    [Fact]
    public void Where_secrets_are_unavailable_the_secret_row_is_read_only_and_the_overlay_says_so()
    {
        var access = AccessFor();
        _protector.IsAvailable = false;

        var vm = ViewModel(access);

        Assert.False(Row(vm, "token").IsEnabled);
        Assert.True(vm.HasSecretsUnavailableNote);
        Assert.True(Row(vm, "url").IsEnabled);

        Row(vm, "token").Text = "hunter2";     // even if it somehow changes, nothing is stored in plain text
        Assert.Null(Stored("token"));
    }

    [Fact]
    public void There_is_no_unavailable_note_when_secrets_work_or_when_there_are_none()
    {
        Assert.False(ViewModel(AccessFor()).HasSecretsUnavailableNote);

        _protector.IsAvailable = false;
        var noSecrets = AccessFor("""<Settings><Setting key="url" type="text"/></Settings>""");
        Assert.False(ViewModel(noSecrets).HasSecretsUnavailableNote);
    }

    [Fact]
    public void A_number_row_exposes_wide_spinner_bounds_when_the_plugin_declared_none()
    {
        var access = AccessFor("""<Settings><Setting key="n" type="number"/><Setting key="m" type="number" min="3" max="9"/></Settings>""");
        var vm = ViewModel(access);

        Assert.True(Row(vm, "n").SpinnerMinimum < -1_000_000m);
        Assert.True(Row(vm, "n").SpinnerMaximum > 1_000_000m);
        Assert.Equal(3m, Row(vm, "m").SpinnerMinimum);
        Assert.Equal(9m, Row(vm, "m").SpinnerMaximum);
    }
}
