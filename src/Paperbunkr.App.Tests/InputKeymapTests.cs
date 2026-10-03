using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Input;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Tests;

/// <summary>The action catalog, the resolved keymap, the default keymap and the JSON store (docs/superpowers/specs/2026-10-03-input-service-design.md §5.1-§5.4, §7).</summary>
public class InputKeymapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_keymap_test_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static InputBinding Key_(Key key, KeyModifiers modifiers = KeyModifiers.None) => InputBinding.ForKey(key, modifiers);

    private static InputActionInfo Info(string id, InputScope scope, InputContext context, params InputBinding[] defaults) =>
        new(id, "Test", id, scope, context, defaults);

    private static InputKeymap Keymap(KeymapConfig? config = null, params InputActionInfo[] infos)
    {
        var catalog = new InputActionCatalog();
        foreach (var info in infos)
        {
            catalog.Register(info);
        }

        return new InputKeymap(catalog, config ?? new KeymapConfig());
    }

    // ----- Catalog -----

    [Fact]
    public void CoreCatalog_RegistersEveryCoreAction_WithUniqueIds()
    {
        var catalog = InputActionCatalog.CreateWithCoreActions();

        Assert.Equal(InputActions.Core.Count, catalog.All.Count);
        Assert.Equal(catalog.All.Count, catalog.All.Select(i => i.Id).Distinct().Count());
        Assert.All(catalog.All, info => Assert.Same(info, catalog.Find(info.Action)));
    }

    [Fact]
    public void CoreCatalog_HasAnInfoForEveryActionIdConstant()
    {
        var catalog = InputActionCatalog.CreateWithCoreActions();
        var constants = typeof(InputActionIds).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);

        foreach (string id in constants)
        {
            Assert.NotNull(catalog.Find(id));
        }
    }

    [Fact]
    public void Catalog_RejectsADuplicateId_AndAFormerIdCollision()
    {
        var catalog = new InputActionCatalog();
        catalog.Register(new InputActionInfo("A.New", "g", "l", InputScope.Global, InputContext.Always, [], FormerIds: ["A.Old"]));

        Assert.Throws<ArgumentException>(() => catalog.Register(Info("A.New", InputScope.Global, InputContext.Always)));
        Assert.Throws<ArgumentException>(() => catalog.Register(Info("A.Old", InputScope.Global, InputContext.Always)));
        Assert.Throws<ArgumentException>(() => catalog.Register(new InputActionInfo("B.New", "g", "l", InputScope.Global, InputContext.Always, [], FormerIds: ["A.Old"])));
        Assert.Throws<ArgumentException>(() => catalog.Register(new InputActionInfo("C.New", "g", "l", InputScope.Global, InputContext.Always, [], FormerIds: ["A.New"])));
        Assert.Null(catalog.Find(default));
        Assert.Null(catalog.Find("Nope.Nothing"));
    }

    // ----- Defaults -----

    [Fact]
    public void DefaultKeymap_HasNoSameScopeOverlappingConflicts()
    {
        var catalog = InputActionCatalog.CreateWithCoreActions();
        var keymap = new InputKeymap(catalog, new KeymapConfig());

        foreach (var info in catalog.All)
        {
            foreach (var binding in keymap.GetBindings(info.Action))
            {
                Assert.True(
                    keymap.FindConflicts(info.Action, binding).Count == 0,
                    $"{info.Id} shares '{binding}' with {string.Join(", ", keymap.FindConflicts(info.Action, binding).Select(c => c.Other))}");
            }
        }
    }

    [Fact]
    public void DefaultKeymap_CarriesTheSpecifiedDefaults()
    {
        var keymap = new InputKeymap(InputActionCatalog.CreateWithCoreActions(), new KeymapConfig());

        Assert.Contains(Key_(Key.Left), keymap.GetBindings(InputActionIds.PageTurnLeft));
        Assert.Contains(Key_(Key.PageDown), keymap.GetBindings(InputActionIds.NextPage));
        Assert.Contains(Key_(Key.Space), keymap.GetBindings(InputActionIds.NextPage));
        Assert.Contains(Key_(Key.MediaNextTrack), keymap.GetBindings(InputActionIds.NextPage));
        Assert.Contains(InputBinding.ForWheel(WheelDirection.Up, KeyModifiers.Control), keymap.GetBindings(InputActionIds.ZoomIn));
        Assert.Contains(InputBinding.ForWheel(WheelDirection.Down, KeyModifiers.Control), keymap.GetBindings(InputActionIds.ZoomOut));
        Assert.Contains(Key_(Key.F11), keymap.GetBindings(InputActionIds.ToggleFullscreen));
        Assert.Contains(Key_(Key.F), keymap.GetBindings(InputActionIds.ToggleFullscreen));
        Assert.Contains(InputBinding.ForMouseButton(MouseButton.XButton1), keymap.GetBindings(InputActionIds.NavigateBack));
        Assert.Contains(InputBinding.ForMouseButton(MouseButton.XButton2), keymap.GetBindings(InputActionIds.NavigateForward));
        Assert.Contains(Key_(Key.Escape), keymap.GetBindings(InputActionIds.CloseCurrentView));
        Assert.Contains(Key_(Key.F, KeyModifiers.Control), keymap.GetBindings(InputActionIds.FocusSearch));
        Assert.Contains(Key_(Key.F5), keymap.GetBindings(InputActionIds.RefreshLibrary));
        Assert.Empty(keymap.GetBindings(InputActionIds.ToggleDoublePageMode));
    }

    // ----- Overrides -----

    [Fact]
    public void Override_ReplacesTheWholeDefaultList_AndUpdatesTheIndex()
    {
        var keymap = Keymap(null, Info("T.One", InputScope.Reader, InputContext.Always, Key_(Key.A), Key_(Key.B)));

        keymap.Set("T.One", [Key_(Key.C)]);

        Assert.Equal([Key_(Key.C)], keymap.GetBindings("T.One"));
        Assert.Empty(keymap.Resolve(Key_(Key.A)));
        Assert.Equal(["T.One"], keymap.Resolve(Key_(Key.C)).Select(a => a.Id));
        Assert.True(keymap.IsCustomised("T.One"));
    }

    [Fact]
    public void EmptyOverride_UnbindsTheAction()
    {
        var keymap = Keymap(null, Info("T.One", InputScope.Reader, InputContext.Always, Key_(Key.A)));

        keymap.Set("T.One", []);

        Assert.Empty(keymap.GetBindings("T.One"));
        Assert.Empty(keymap.Resolve(Key_(Key.A)));
    }

    [Fact]
    public void SettingTheDefaults_RemovesTheOverride_SoFutureDefaultsStillFlow()
    {
        var keymap = Keymap(null, Info("T.One", InputScope.Reader, InputContext.Always, Key_(Key.A)));
        keymap.Set("T.One", [Key_(Key.B)]);

        keymap.Set("T.One", [Key_(Key.A)]);

        Assert.False(keymap.IsCustomised("T.One"));
        Assert.DoesNotContain("T.One", keymap.Config.Overrides.Keys);
    }

    [Fact]
    public void ResetAndResetAll_RestoreDefaults_AndRaiseChanged()
    {
        var keymap = Keymap(null, Info("T.One", InputScope.Reader, InputContext.Always, Key_(Key.A)), Info("T.Two", InputScope.Reader, InputContext.Always, Key_(Key.B)));
        int changes = 0;
        keymap.Changed += (_, _) => changes++;
        keymap.Set("T.One", [Key_(Key.X)]);
        keymap.Set("T.Two", [Key_(Key.Y)]);

        keymap.Reset("T.One");
        Assert.Equal([Key_(Key.A)], keymap.GetBindings("T.One"));
        Assert.Equal([Key_(Key.Y)], keymap.GetBindings("T.Two"));

        keymap.ResetAll();
        Assert.Equal([Key_(Key.B)], keymap.GetBindings("T.Two"));
        Assert.Equal(4, changes);

        keymap.Reset("T.One");
        keymap.ResetAll();
        Assert.Equal(4, changes);
    }

    [Fact]
    public void Set_DropsDuplicatesAndUndefinedBindings_AndRejectsUnknownActions()
    {
        var keymap = Keymap(null, Info("T.One", InputScope.Reader, InputContext.Always));

        keymap.Set("T.One", [Key_(Key.A), Key_(Key.A), default]);

        Assert.Equal([Key_(Key.A)], keymap.GetBindings("T.One"));
        Assert.Throws<ArgumentException>(() => keymap.Set("T.Missing", [Key_(Key.A)]));
    }

    [Fact]
    public void AnUnparseableEntry_IsSkipped_AndAllUnparseableFallsBackToDefaults()
    {
        var config = new KeymapConfig();
        config.Overrides["T.One"] = ["Ctrl+Banana", "Ctrl+Q", "garbage"];
        config.Overrides["T.Two"] = ["garbage", "more garbage"];
        var keymap = Keymap(config, Info("T.One", InputScope.Reader, InputContext.Always, Key_(Key.A)), Info("T.Two", InputScope.Reader, InputContext.Always, Key_(Key.B)));

        Assert.Equal([Key_(Key.Q, KeyModifiers.Control)], keymap.GetBindings("T.One"));
        Assert.Equal([Key_(Key.B)], keymap.GetBindings("T.Two"));
    }

    [Fact]
    public void OverridesForUnknownActions_AreKeptUntouched()
    {
        var config = new KeymapConfig();
        config.Overrides["Plugin.Gone"] = ["Ctrl+G"];
        var keymap = Keymap(config, Info("T.One", InputScope.Reader, InputContext.Always, Key_(Key.A)));

        keymap.Set("T.One", [Key_(Key.B)]);

        Assert.Equal(["Ctrl+G"], keymap.Config.Overrides["Plugin.Gone"]);
    }

    // ----- Renames -----

    [Fact]
    public void AnOverrideUnderAFormerId_IsReKeyedToTheCurrentId()
    {
        var config = new KeymapConfig();
        config.Overrides["Old.Name"] = ["Ctrl+Q"];
        var catalog = new InputActionCatalog();
        catalog.Register(new InputActionInfo("New.Name", "g", "l", InputScope.Reader, InputContext.Always, [Key_(Key.A)], FormerIds: ["Old.Name"]));

        var keymap = new InputKeymap(catalog, config);

        Assert.True(keymap.Rekeyed);
        Assert.Equal([Key_(Key.Q, KeyModifiers.Control)], keymap.GetBindings("New.Name"));
        Assert.DoesNotContain("Old.Name", config.Overrides.Keys);
    }

    [Fact]
    public void WhenBothIdsHaveOverrides_TheNewIdsOneWins()
    {
        var config = new KeymapConfig();
        config.Overrides["Old.Name"] = ["Ctrl+Q"];
        config.Overrides["New.Name"] = ["Ctrl+W"];
        var catalog = new InputActionCatalog();
        catalog.Register(new InputActionInfo("New.Name", "g", "l", InputScope.Reader, InputContext.Always, [], FormerIds: ["Old.Name"]));

        var keymap = new InputKeymap(catalog, config);

        Assert.Equal([Key_(Key.W, KeyModifiers.Control)], keymap.GetBindings("New.Name"));
        Assert.DoesNotContain("Old.Name", config.Overrides.Keys);
    }

    // ----- Conflicts -----

    [Fact]
    public void Conflicts_NeedTheSameScopeAndOverlappingContexts()
    {
        var keymap = Keymap(
            null,
            Info("T.PagedLeft", InputScope.Reader, InputContext.PagedUnzoomed, Key_(Key.Left)),
            Info("T.ZoomedLeft", InputScope.Reader, InputContext.PagedZoomed, Key_(Key.Left)),
            Info("T.ScrollLeft", InputScope.Reader, InputContext.Continuous, Key_(Key.Left)),
            Info("T.AlwaysX", InputScope.Reader, InputContext.Always, Key_(Key.X)),
            Info("T.PagedX", InputScope.Reader, InputContext.Paged, Key_(Key.X)),
            Info("T.GlobalX", InputScope.Global, InputContext.Always, Key_(Key.X)));

        // Disjoint contexts may share a key: only one is ever active.
        Assert.Empty(keymap.FindConflicts("T.PagedLeft", Key_(Key.Left)));

        // Always overlaps everything in its own scope, but a different scope is layering, not a conflict.
        var conflicts = keymap.FindConflicts("T.AlwaysX", Key_(Key.X));
        Assert.Equal(["T.PagedX"], conflicts.Select(c => c.Other.Id));
        Assert.Empty(keymap.FindConflicts("T.GlobalX", Key_(Key.X)));
    }

    [Fact]
    public void Conflicts_ForAnUnboundOrUnknownBinding_AreEmpty()
    {
        var keymap = Keymap(null, Info("T.One", InputScope.Reader, InputContext.Always, Key_(Key.A)));

        Assert.Empty(keymap.FindConflicts("T.One", Key_(Key.Z)));
        Assert.Empty(keymap.FindConflicts("T.Missing", Key_(Key.A)));
    }

    // ----- Store -----

    private string KeymapPath() => Path.Combine(_dir, "keymap.json");

    [Fact]
    public void Store_MissingFile_GivesDefaults()
    {
        var config = new JsonKeymapStore(KeymapPath()).Load();

        Assert.Empty(config.Overrides);
        Assert.Equal(KeymapConfig.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Equal(1.5, config.Tuning.SwipeThreshold);
    }

    [Fact]
    public void Store_RoundTripsOverridesTuningAndFlags()
    {
        var store = new JsonKeymapStore(KeymapPath());
        var config = new KeymapConfig { LegacyBindingsImported = true };
        config.Overrides["Reader.ZoomIn"] = ["Ctrl+WheelUp", "Z"];
        config.Overrides["Reader.ToggleChrome"] = [];
        config.Tuning.SwipeThreshold = 2.5;

        store.Save(config);
        var loaded = store.Load();

        Assert.True(loaded.LegacyBindingsImported);
        Assert.Equal(["Ctrl+WheelUp", "Z"], loaded.Overrides["Reader.ZoomIn"]);
        Assert.Empty(loaded.Overrides["Reader.ToggleChrome"]);
        Assert.Equal(2.5, loaded.Tuning.SwipeThreshold);
    }

    [Fact]
    public void Store_SaveReplacesAnExistingFile_LeavingNoTempFile()
    {
        var store = new JsonKeymapStore(KeymapPath());
        store.Save(new KeymapConfig());
        var second = new KeymapConfig();
        second.Overrides["A"] = ["Ctrl+A"];

        store.Save(second);

        Assert.Equal(["Ctrl+A"], store.Load().Overrides["A"]);
        Assert.Equal([KeymapPath()], Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"hello\"")]
    [InlineData("{\"SchemaVersion\": 99, \"Overrides\": {}}")]
    [InlineData("{\"SchemaVersion\": 0}")]
    public void Store_AFileItCannotUse_IsSetAsideAsBad_AndDefaultsAreUsed(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(KeymapPath(), content);
        var messages = new List<string>();

        var config = new JsonKeymapStore(KeymapPath(), log: messages.Add).Load();

        Assert.Empty(config.Overrides);
        Assert.False(File.Exists(KeymapPath()));
        Assert.Equal(content, File.ReadAllText(KeymapPath() + ".bad"));
        Assert.Single(messages);
    }

    [Fact]
    public void Store_ANullOverrideValue_IsTreatedAsUnbound()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(KeymapPath(), "{\"SchemaVersion\":1,\"Overrides\":{\"A\":null}}");

        var config = new JsonKeymapStore(KeymapPath()).Load();

        Assert.Empty(config.Overrides["A"]);
    }

    private sealed class RenameFieldMigration : IKeymapMigration
    {
        public int FromVersion => 1;

        public void Apply(JsonObject root)
        {
            // v1 called the map "Bindings"; v2 calls it "Overrides".
            if (root["Bindings"] is JsonNode bindings)
            {
                root.Remove("Bindings");
                root["Overrides"] = bindings;
            }
        }
    }

    [Fact]
    public void Migrations_UpgradeOldDocuments_OneVersionAtATime()
    {
        var root = JsonNode.Parse("{\"SchemaVersion\":1,\"Bindings\":{\"A\":[\"Ctrl+A\"]}}")!.AsObject();

        bool ok = KeymapMigrations.TryUpgrade(root, [new RenameFieldMigration()], currentVersion: 2);

        Assert.True(ok);
        Assert.Equal(2, root["SchemaVersion"]!.GetValue<int>());
        Assert.Equal("Ctrl+A", root["Overrides"]!["A"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Migrations_FailWhenAStepIsMissing_OrTheDocumentIsNewer()
    {
        Assert.False(KeymapMigrations.TryUpgrade(JsonNode.Parse("{\"SchemaVersion\":1}")!.AsObject(), [], currentVersion: 2));
        Assert.False(KeymapMigrations.TryUpgrade(JsonNode.Parse("{\"SchemaVersion\":3}")!.AsObject(), [], currentVersion: 2));
        Assert.True(KeymapMigrations.TryUpgrade(JsonNode.Parse("{}")!.AsObject(), [], currentVersion: 1));
    }

    [Fact]
    public void Store_UsesTheMigrationChainOnLoad()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(KeymapPath(), "{\"SchemaVersion\":1,\"Bindings\":{\"A\":[\"Ctrl+A\"]}}");

        var config = new JsonKeymapStore(KeymapPath(), [new RenameFieldMigration()], currentSchemaVersion: 2).Load();

        Assert.Equal(2, config.SchemaVersion);
        Assert.Equal(["Ctrl+A"], config.Overrides["A"]);
        Assert.True(File.Exists(KeymapPath()));
    }

    [Fact]
    public void MemoryStore_IsolatesWhatItSavedFromLaterEdits()
    {
        var store = new MemoryKeymapStore();
        var config = new KeymapConfig();
        config.Overrides["A"] = ["Ctrl+A"];
        store.Save(config);

        config.Overrides["A"].Add("Ctrl+B");

        Assert.Equal(["Ctrl+A"], store.Load().Overrides["A"]);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void TheFile_WritesBindingsAsPlainText_NotUnicodeEscapes()
    {
        var store = new JsonKeymapStore(KeymapPath());
        var config = new KeymapConfig();
        config.Overrides["Reader.ZoomIn"] = ["Ctrl+WheelUp"];
        store.Save(config);

        string text = File.ReadAllText(KeymapPath());

        Assert.Contains("Ctrl+WheelUp", text);
        Assert.DoesNotContain("\\u002B", text);
    }

    [Fact]
    public void Json_UsesReadablePropertyNames()
    {
        var store = new JsonKeymapStore(KeymapPath());
        var config = new KeymapConfig();
        config.Overrides["Reader.ZoomIn"] = ["Z"];
        store.Save(config);

        using var doc = JsonDocument.Parse(File.ReadAllText(KeymapPath()));

        Assert.Equal(1, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("Z", doc.RootElement.GetProperty("Overrides").GetProperty("Reader.ZoomIn")[0].GetString());
    }
}
