using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Paperbunkr.App.Services.Input;

/// <summary>Where the user's keymap overrides are loaded from and saved to.</summary>
public interface IKeymapStore
{
    /// <summary>Loads the saved config, or a fresh default one when there is none or it cannot be used. Never throws for bad content.</summary>
    KeymapConfig Load();

    /// <summary>Saves the config.</summary>
    void Save(KeymapConfig config);
}

/// <summary>Upgrades a stored keymap one schema version at a time; run in order by <see cref="KeymapMigrations"/>.</summary>
public interface IKeymapMigration
{
    /// <summary>The schema version this step reads.</summary>
    int FromVersion { get; }

    /// <summary>Rewrites <paramref name="root"/> in place from <see cref="FromVersion"/> to <c>FromVersion + 1</c>.</summary>
    void Apply(JsonObject root);
}

/// <summary>Runs the chain of <see cref="IKeymapMigration"/> steps over a raw keymap document.</summary>
public static class KeymapMigrations
{
    /// <summary>The built-in chain. Empty while only schema 1 exists.</summary>
    public static IReadOnlyList<IKeymapMigration> Default { get; } = [];

    /// <summary>
    /// Brings <paramref name="root"/> up to <see cref="KeymapConfig.CurrentSchemaVersion"/>. Returns false when the document is newer than this build understands (or has
    /// no usable version, or a step is missing), in which case it must not be partly read.
    /// </summary>
    public static bool TryUpgrade(JsonObject root, IReadOnlyList<IKeymapMigration> steps, int currentVersion)
    {
        int version = root["SchemaVersion"] is JsonValue v && v.TryGetValue(out int parsed) ? parsed : 1;
        if (version > currentVersion || version < 1)
        {
            return false;
        }

        while (version < currentVersion)
        {
            var step = steps.FirstOrDefault(s => s.FromVersion == version);
            if (step is null)
            {
                return false;
            }

            step.Apply(root);
            version++;
            root["SchemaVersion"] = version;
        }

        return true;
    }
}

/// <summary>
/// <see cref="IKeymapStore"/> backed by <c>keymap.json</c> next to the database. Writes go to a temporary file first and replace the target, so a crash mid-save
/// cannot leave a half-written keymap. A file that cannot be read (invalid JSON, a newer schema than this build, wrong shape) is renamed to <c>keymap.json.bad</c>
/// and defaults are used, so a bad file is kept for inspection but never silently overwritten.
/// </summary>
public sealed class JsonKeymapStore : IKeymapStore
{
    // Relaxed escaping so "Ctrl+WheelUp" is written as such, not as "Ctrl\u002BWheelUp": the file is meant to be read and hand-edited.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly IReadOnlyList<IKeymapMigration> _migrations;
    private readonly Action<string>? _log;
    private readonly int _currentSchemaVersion;

    /// <param name="path">Full path of the keymap file.</param>
    /// <param name="migrations">Schema upgrade steps; defaults to <see cref="KeymapMigrations.Default"/>.</param>
    /// <param name="log">Receives a one-line warning when a file is set aside.</param>
    /// <param name="currentSchemaVersion">The schema the migration chain upgrades to; always <see cref="KeymapConfig.CurrentSchemaVersion"/> outside tests.</param>
    public JsonKeymapStore(
        string path,
        IReadOnlyList<IKeymapMigration>? migrations = null,
        Action<string>? log = null,
        int currentSchemaVersion = KeymapConfig.CurrentSchemaVersion)
    {
        _path = path;
        _migrations = migrations ?? KeymapMigrations.Default;
        _log = log;
        _currentSchemaVersion = currentSchemaVersion;
    }

    /// <summary>The default location: <c>keymap.json</c> in the same folder as the database, so a database-path override moves it too.</summary>
    public static string DefaultPath() =>
        Path.Combine(Path.GetDirectoryName(Paperbunkr.Data.PaperbunkrDbContext.GetDefaultDatabasePath()) ?? ".", "keymap.json");

    public string FilePath => _path;

    public KeymapConfig Load()
    {
        if (!File.Exists(_path))
        {
            return new KeymapConfig();
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
            if (root is null || !KeymapMigrations.TryUpgrade(root, _migrations, _currentSchemaVersion))
            {
                return SetAside("it is not a keymap this version understands");
            }

            return root.Deserialize<KeymapConfig>(JsonOptions) is { } config ? Normalise(config) : SetAside("it is empty");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return SetAside(ex.Message);
        }
    }

    public void Save(KeymapConfig config)
    {
        string? directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, JsonOptions));
        if (File.Exists(_path))
        {
            File.Replace(temp, _path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, _path);
        }
    }

    private static KeymapConfig Normalise(KeymapConfig config)
    {
        config.Overrides ??= [];
        config.Tuning ??= new InputTuning();
        foreach (string key in config.Overrides.Where(p => p.Value is null).Select(p => p.Key).ToList())
        {
            config.Overrides[key] = [];
        }

        return config;
    }

    private KeymapConfig SetAside(string reason)
    {
        try
        {
            string bad = _path + ".bad";
            File.Move(_path, bad, overwrite: true);
            _log?.Invoke($"keymap.json was set aside as keymap.json.bad ({reason}); defaults are in use.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"keymap.json could not be used ({reason}) and could not be renamed: {ex.Message}");
        }

        return new KeymapConfig();
    }
}

/// <summary>An in-memory <see cref="IKeymapStore"/> for tests and for hosts that do not want persistence.</summary>
public sealed class MemoryKeymapStore : IKeymapStore
{
    private KeymapConfig _saved;

    public MemoryKeymapStore(KeymapConfig? initial = null)
    {
        _saved = initial ?? new KeymapConfig();
    }

    /// <summary>How many times <see cref="Save"/> has been called.</summary>
    public int SaveCount { get; private set; }

    public KeymapConfig Load() => Clone(_saved);

    public void Save(KeymapConfig config)
    {
        _saved = Clone(config);
        SaveCount++;
    }

    private static KeymapConfig Clone(KeymapConfig config) =>
        JsonSerializer.Deserialize<KeymapConfig>(JsonSerializer.Serialize(config))!;
}
