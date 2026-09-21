using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins.Tests;

/// <summary>
/// The <c>&lt;Settings&gt;</c> schema (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §6): parsing
/// and validating the declaration, validating a value, and resolving what a plugin should see. The pure
/// logic is tested directly; parsing goes through the real XML manifest reader, and blocking through a real
/// <see cref="PluginEngine"/>.
/// </summary>
public sealed class PluginSettingsSchemaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "paperbunkr-settings-schema-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Test double: "encrypts" by tagging, so a test can tell ciphertext from plain text and simulate a value that won't decrypt.</summary>
    private sealed class FakeProtector : ISecretProtector
    {
        public bool IsAvailable { get; set; } = true;

        public string Protect(string plaintext) =>
            IsAvailable ? "fake:" + new string(plaintext.Reverse().ToArray()) : throw new PlatformNotSupportedException();

        public string? TryUnprotect(string stored) =>
            IsAvailable && stored.StartsWith("fake:", StringComparison.Ordinal) ? new string(stored["fake:".Length..].Reverse().ToArray()) : null;

        public bool IsProtected(string stored) => stored.StartsWith("fake:", StringComparison.Ordinal) || stored.StartsWith("bad:", StringComparison.Ordinal);
    }

    private PluginManifest Manifest(string settingsXml)
    {
        Directory.CreateDirectory(_root);
        string file = Path.Combine(_root, $"m-{Guid.NewGuid():N}.xml");
        File.WriteAllText(file, $"""
            <Plugin key="p" name="P">
              {settingsXml}
            </Plugin>
            """);
        return XmlPluginInitializer.ReadManifest(file) ?? throw new InvalidOperationException("manifest didn't parse");
    }

    private static PluginSettingsSchema ParseOk(PluginManifest manifest)
    {
        PluginSettingsSchema? schema = PluginSettingsSchema.Parse(manifest, out string? error);
        Assert.Null(error);
        return Assert.IsType<PluginSettingsSchema>(schema);
    }

    private string? ParseError(string settingsXml)
    {
        PluginSettingsSchema? schema = PluginSettingsSchema.Parse(Manifest(settingsXml), out string? error);
        Assert.Null(schema);
        return error;
    }

    // ---- Parsing a well-formed declaration ----

    [Fact]
    public void A_full_declaration_parses_every_type_with_its_attributes()
    {
        var schema = ParseOk(Manifest("""
            <Settings>
              <Setting key="api-url" label="API URL" type="text" default="https://example.com" description="Where to sync"/>
              <Setting key="mode" label="Mode" type="choice" default="fast">
                <Choice value="fast" label="Fast"/>
                <Choice value="thorough" label="Thorough"/>
              </Setting>
              <Setting key="limit" label="Limit" type="number" default="10" min="1" max="100"/>
              <Setting key="enabled" label="Enabled" type="toggle" default="true"/>
              <Setting key="token" label="API token" type="secret"/>
            </Settings>
            """));

        Assert.Equal(new[] { "api-url", "mode", "limit", "enabled", "token" }, schema.Definitions.Select(d => d.Key));

        var url = schema.Find("api-url")!;
        Assert.Equal(("API URL", PluginSettingType.Text, "https://example.com", "Where to sync"), (url.Label, url.Type, url.Default, url.Description));

        var mode = schema.Find("mode")!;
        Assert.Equal(PluginSettingType.Choice, mode.Type);
        Assert.Equal(new[] { ("fast", "Fast"), ("thorough", "Thorough") }, mode.Choices.Select(c => (c.Value, c.Label)));

        var limit = schema.Find("limit")!;
        Assert.Equal((PluginSettingType.Number, "10", 1m, 100m), (limit.Type, limit.Default, limit.Min, limit.Max));

        Assert.Equal(PluginSettingType.Toggle, schema.Find("enabled")!.Type);
        Assert.Equal(PluginSettingType.Secret, schema.Find("token")!.Type);
        Assert.Null(schema.Find("token")!.Default);
        Assert.Null(schema.Find("nope"));
    }

    [Fact]
    public void The_label_defaults_to_the_key_and_a_choice_label_to_its_value()
    {
        var schema = ParseOk(Manifest("""
            <Settings>
              <Setting key="depth" type="text"/>
              <Setting key="mode" type="choice"><Choice value="a"/></Setting>
            </Settings>
            """));

        Assert.Equal("depth", schema.Find("depth")!.Label);
        Assert.Equal("a", schema.Find("mode")!.Choices.Single().Label);
    }

    [Theory]
    [InlineData("TOGGLE")]
    [InlineData("Toggle")]
    [InlineData("toggle")]
    public void The_type_is_case_insensitive(string type)
    {
        var schema = ParseOk(Manifest($"""<Settings><Setting key="k" type="{type}"/></Settings>"""));
        Assert.Equal(PluginSettingType.Toggle, schema.Find("k")!.Type);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    public void Locked_parses_true_or_false(string attribute, bool expected)
    {
        var schema = ParseOk(Manifest($"""<Settings><Setting key="k" type="text" locked="{attribute}"/></Settings>"""));
        Assert.Equal(expected, schema.Find("k")!.Locked);
    }

    [Fact]
    public void Locked_defaults_to_false_when_not_declared()
    {
        var schema = ParseOk(Manifest("""<Settings><Setting key="k" type="text"/></Settings>"""));
        Assert.False(schema.Find("k")!.Locked);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public void Locked_with_anything_but_true_or_false_is_a_schema_error(string attribute)
    {
        string? error = ParseError($"""<Settings><Setting key="k" type="text" locked="{attribute}"/></Settings>""");
        Assert.Contains("locked", error);
        Assert.Contains("k", error);
    }

    [Fact]
    public void A_manifest_with_no_settings_yields_no_schema_and_no_error()
    {
        Assert.Null(PluginSettingsSchema.Parse(Manifest(""), out string? error));
        Assert.Null(error);
    }

    [Fact]
    public void An_empty_settings_element_yields_no_schema_and_no_error()
    {
        Assert.Null(PluginSettingsSchema.Parse(Manifest("<Settings/>"), out string? error));
        Assert.Null(error);
    }

    // ---- Malformed declarations are reported, not ignored ----

    [Theory]
    [InlineData("""<Settings><Setting type="text"/></Settings>""", "has no key")]
    [InlineData("""<Settings><Setting key="a" type="text"/><Setting key="a" type="text"/></Settings>""", "declared more than once")]
    [InlineData("""<Settings><Setting key="a" type="colour"/></Settings>""", "unknown type 'colour'")]
    [InlineData("""<Settings><Setting key="a"/></Settings>""", "unknown type")]
    [InlineData("""<Settings><Setting key="a" type="choice"/></Settings>""", "at least one <Choice>")]
    [InlineData("""<Settings><Setting key="a" type="choice" default="z"><Choice value="a"/></Setting></Settings>""", "isn't one of the choices")]
    [InlineData("""<Settings><Setting key="a" type="choice"><Choice value="a"/><Choice value="a"/></Setting></Settings>""", "same value")]
    [InlineData("""<Settings><Setting key="a" type="choice"><Choice label="x"/></Setting></Settings>""", "has no value")]
    [InlineData("""<Settings><Setting key="a" type="text"><Choice value="a"/></Setting></Settings>""", "only valid on a choice")]
    [InlineData("""<Settings><Setting key="a" type="text" min="1"/></Settings>""", "only valid on a number")]
    [InlineData("""<Settings><Setting key="a" type="number" min="x"/></Settings>""", "min 'x' isn't a number")]
    [InlineData("""<Settings><Setting key="a" type="number" max="y"/></Settings>""", "max 'y' isn't a number")]
    [InlineData("""<Settings><Setting key="a" type="number" min="5" max="1"/></Settings>""", "greater than max")]
    [InlineData("""<Settings><Setting key="a" type="number" default="abc"/></Settings>""", "isn't a number")]
    [InlineData("""<Settings><Setting key="a" type="number" min="1" max="10" default="11"/></Settings>""", "outside the min/max range")]
    [InlineData("""<Settings><Setting key="a" type="toggle" default="maybe"/></Settings>""", "isn't true or false")]
    [InlineData("""<Settings><Setting key="a" type="secret" default="hunter2"/></Settings>""", "can't have a default")]
    public void A_malformed_declaration_is_an_error_naming_the_setting(string settingsXml, string expected)
    {
        string? error = ParseError(settingsXml);

        Assert.NotNull(error);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void The_error_names_the_offending_key()
    {
        string? error = ParseError("""<Settings><Setting key="good" type="text"/><Setting key="bad" type="nope"/></Settings>""");

        Assert.StartsWith("setting 'bad':", error);
    }

    // ---- Validating a value ----

    private static PluginSettingDefinition Def(PluginSettingType type, string? def = null, decimal? min = null, decimal? max = null, params string[] choices) =>
        new("k", "K", type, def, null, min, max, choices.Select(c => new PluginSettingChoice(c, c)).ToList());

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("TRUE")]
    [InlineData("False")]
    public void A_toggle_accepts_true_and_false_in_any_case(string value) =>
        Assert.Null(PluginSettingsSchema.Validate(Def(PluginSettingType.Toggle), value));

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public void A_toggle_rejects_anything_else(string value) =>
        Assert.NotNull(PluginSettingsSchema.Validate(Def(PluginSettingType.Toggle), value));

    [Theory]
    [InlineData("5")]
    [InlineData("1")]
    [InlineData("10")]
    [InlineData("7.5")]
    [InlineData(" 3 ")]
    public void A_number_within_range_is_valid(string value) =>
        Assert.Null(PluginSettingsSchema.Validate(Def(PluginSettingType.Number, min: 1, max: 10), value));

    [Theory]
    [InlineData("0", "at least 1")]
    [InlineData("11", "at most 10")]
    [InlineData("-5", "at least 1")]
    [InlineData("abc", "Must be a number")]
    [InlineData("", "Must be a number")]
    [InlineData("1,5", "Must be a number")]
    public void A_number_outside_range_or_not_numeric_is_invalid_with_a_reason(string value, string expected)
    {
        string? problem = PluginSettingsSchema.Validate(Def(PluginSettingType.Number, min: 1, max: 10), value);

        Assert.NotNull(problem);
        Assert.Contains(expected, problem);
    }

    [Fact]
    public void A_number_with_no_bounds_accepts_any_number()
    {
        Assert.Null(PluginSettingsSchema.Validate(Def(PluginSettingType.Number), "-999999"));
        Assert.Null(PluginSettingsSchema.Validate(Def(PluginSettingType.Number), "123456789"));
    }

    [Fact]
    public void A_choice_must_be_one_of_its_values_case_sensitively()
    {
        var def = Def(PluginSettingType.Choice, choices: new[] { "fast", "thorough" });

        Assert.Null(PluginSettingsSchema.Validate(def, "fast"));
        Assert.Contains("fast, thorough", PluginSettingsSchema.Validate(def, "slow"));
        Assert.NotNull(PluginSettingsSchema.Validate(def, "FAST"));
    }

    [Theory]
    [InlineData(PluginSettingType.Text)]
    [InlineData(PluginSettingType.Secret)]
    public void Text_and_secret_accept_anything_including_empty(PluginSettingType type)
    {
        Assert.Null(PluginSettingsSchema.Validate(Def(type), ""));
        Assert.Null(PluginSettingsSchema.Validate(Def(type), "anything at all"));
    }

    [Fact]
    public void Null_is_always_valid_because_it_just_means_unset()
    {
        foreach (PluginSettingType type in Enum.GetValues<PluginSettingType>())
        {
            Assert.Null(PluginSettingsSchema.Validate(Def(type, choices: new[] { "a" }), null));
        }
    }

    // ---- Resolving what a plugin sees ----

    [Fact]
    public void An_unset_value_resolves_to_the_declared_default()
    {
        var def = Def(PluginSettingType.Number, "10", 1, 100);

        Assert.Equal("10", PluginSettingsSchema.Resolve(def, null, new FakeProtector()));
    }

    [Fact]
    public void An_unset_value_with_no_default_resolves_to_null()
    {
        Assert.Null(PluginSettingsSchema.Resolve(Def(PluginSettingType.Text), null, new FakeProtector()));
    }

    [Fact]
    public void A_valid_stored_value_resolves_to_itself()
    {
        Assert.Equal("42", PluginSettingsSchema.Resolve(Def(PluginSettingType.Number, "10", 1, 100), "42", new FakeProtector()));
        Assert.Equal("", PluginSettingsSchema.Resolve(Def(PluginSettingType.Text, "dflt"), "", new FakeProtector()));
    }

    [Theory]
    [InlineData(PluginSettingType.Number, "999")]     // out of range
    [InlineData(PluginSettingType.Number, "abc")]     // not a number
    [InlineData(PluginSettingType.Toggle, "maybe")]
    public void A_stored_value_that_violates_the_schema_resolves_to_the_default(PluginSettingType type, string stored)
    {
        var def = type == PluginSettingType.Number
            ? Def(type, "10", 1, 100)
            : Def(type, "true");

        Assert.Equal(def.Default, PluginSettingsSchema.Resolve(def, stored, new FakeProtector()));
    }

    [Fact]
    public void A_choice_that_is_no_longer_offered_resolves_to_the_default()
    {
        var def = Def(PluginSettingType.Choice, "fast", choices: new[] { "fast", "thorough" });

        Assert.Equal("fast", PluginSettingsSchema.Resolve(def, "removed-option", new FakeProtector()));
    }

    [Fact]
    public void A_protected_secret_resolves_to_its_plain_text()
    {
        var protector = new FakeProtector();
        string stored = protector.Protect("hunter2");

        Assert.NotEqual("hunter2", stored);
        Assert.Equal("hunter2", PluginSettingsSchema.Resolve(Def(PluginSettingType.Secret), stored, protector));
    }

    [Fact]
    public void A_secret_that_will_not_decrypt_resolves_to_unset_never_throwing()
    {
        // "bad:" is recognised as protected but the fake refuses to decrypt it - a value from another user/machine.
        Assert.Null(PluginSettingsSchema.Resolve(Def(PluginSettingType.Secret), "bad:xyz", new FakeProtector()));
    }

    [Fact]
    public void A_secret_stored_as_plain_text_before_the_schema_existed_is_still_its_own_value()
    {
        Assert.Equal("old-plain-key", PluginSettingsSchema.Resolve(Def(PluginSettingType.Secret), "old-plain-key", new FakeProtector()));
    }

    // ---- The engine: a bad schema blocks the plugin, a good one is exposed ----

    private void WritePlugin(string key, string settingsXml, string extraCommands = "")
    {
        string dir = Path.Combine(_root, key);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), $"""
            <Plugin key="{key}" name="{key} plugin">
              <Command hook="Startup" key="{key}.startup" name="Startup" script="run.csx" />
              {extraCommands}
              {settingsXml}
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "run.csx"), "return \"ok\";");
    }

    private PluginEngine Discover()
    {
        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());
        return engine;
    }

    [Fact]
    public void A_valid_schema_is_exposed_by_plugin_key_and_the_plugin_still_loads()
    {
        WritePlugin("good", """<Settings><Setting key="mode" type="text" default="x"/></Settings>""");

        PluginEngine engine = Discover();

        Assert.Equal("mode", engine.SettingsSchemas["good"].Definitions.Single().Key);
        Assert.False(Assert.Single(engine.AllCommands).IsBroken);
        Assert.Null(engine.PackageApiInfo["good"].BlockedReason);
    }

    [Fact]
    public void A_plugin_with_no_settings_has_no_schema_entry()
    {
        WritePlugin("plain", "");
        WritePlugin("empty", "<Settings/>");

        PluginEngine engine = Discover();

        Assert.Empty(engine.SettingsSchemas);
        Assert.Equal(2, engine.AllCommands.Count);
    }

    [Fact]
    public void A_malformed_schema_blocks_the_plugin_with_the_reason_and_registers_no_commands()
    {
        WritePlugin("broken", """<Settings><Setting key="a" type="colour"/></Settings>""");
        WritePlugin("fine", "");

        PluginEngine engine = Discover();

        Assert.DoesNotContain(engine.AllCommands, c => c.PluginKey == "broken");
        Assert.Contains(engine.AllCommands, c => c.PluginKey == "fine");
        string reason = engine.PackageApiInfo["broken"].BlockedReason!;
        Assert.StartsWith("invalid <Settings>:", reason);
        Assert.Contains("unknown type 'colour'", reason);
        Assert.False(engine.SettingsSchemas.ContainsKey("broken"));
    }

    [Fact]
    public void A_schema_together_with_a_ConfigScript_command_is_blocked_because_settings_would_be_defined_twice()
    {
        WritePlugin(
            "twice",
            """<Settings><Setting key="a" type="text"/></Settings>""",
            """<Command hook="ConfigScript" key="twice.startup" name="Configure" script="run.csx" />""");

        PluginEngine engine = Discover();

        Assert.Empty(engine.AllCommands);
        string reason = engine.PackageApiInfo["twice"].BlockedReason!;
        Assert.Contains("both <Settings> and a ConfigScript command", reason);
        Assert.False(engine.SettingsSchemas.ContainsKey("twice"));
    }

    [Fact]
    public void A_ConfigScript_command_without_a_schema_is_unaffected()
    {
        WritePlugin("legacy", "", """<Command hook="ConfigScript" key="legacy.startup" name="Configure" script="run.csx" />""");

        PluginEngine engine = Discover();

        Assert.Null(engine.PackageApiInfo["legacy"].BlockedReason);
        Assert.NotEmpty(engine.AllCommands);
    }

    [Fact]
    public void A_requiresApi_block_takes_precedence_and_the_schema_is_not_even_read()
    {
        Directory.CreateDirectory(Path.Combine(_root, "future"));
        File.WriteAllText(Path.Combine(_root, "future", "plugin.xml"), """
            <Plugin key="future" name="Future" requiresApi="9.0">
              <Command hook="Startup" key="future.startup" name="Startup" script="run.csx" />
              <Settings><Setting key="a" type="colour"/></Settings>
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(_root, "future", "run.csx"), "return 1;");

        PluginEngine engine = Discover();

        Assert.Contains("major version mismatch", engine.PackageApiInfo["future"].BlockedReason);
    }

    [Fact]
    public void Schemas_are_cleared_by_the_next_discovery()
    {
        WritePlugin("good", """<Settings><Setting key="a" type="text"/></Settings>""");
        var engine = new PluginEngine();
        engine.Discover(_root, new FakePluginEnvironment());
        Assert.Single(engine.SettingsSchemas);

        Directory.Delete(Path.Combine(_root, "good"), recursive: true);
        engine.Discover(_root, new FakePluginEnvironment());

        Assert.Empty(engine.SettingsSchemas);
    }

    [Fact]
    public void RejectPackage_removes_commands_and_schema_and_records_the_reason()
    {
        WritePlugin("victim", """<Settings><Setting key="a" type="text"/></Settings>""");
        WritePlugin("bystander", "");
        PluginEngine engine = Discover();

        engine.RejectPackage("victim", "two settings definitions");

        Assert.DoesNotContain(engine.AllCommands, c => c.PluginKey == "victim");
        Assert.Contains(engine.AllCommands, c => c.PluginKey == "bystander");
        Assert.False(engine.SettingsSchemas.ContainsKey("victim"));
        Assert.Equal("two settings definitions", engine.PackageApiInfo["victim"].BlockedReason);
    }

    [Fact]
    public void RejectPackage_for_an_unknown_key_is_a_no_op()
    {
        WritePlugin("only", "");
        PluginEngine engine = Discover();

        engine.RejectPackage("nobody", "irrelevant");

        Assert.Single(engine.AllCommands);
    }

    [Fact]
    public async Task A_schema_plugin_still_runs_its_commands_normally()
    {
        WritePlugin("good", """<Settings><Setting key="a" type="text"/></Settings>""");
        PluginEngine engine = Discover();

        var results = await engine.InvokeAsync(PluginHooks.Startup, env => new StartupHookGlobals { Environment = env });

        Assert.Equal("ok", Assert.Single(results).ReturnValue);
    }
}
