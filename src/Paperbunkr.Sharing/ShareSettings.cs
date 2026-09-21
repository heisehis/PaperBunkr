using System.Text.Json;

namespace Paperbunkr.Sharing;

/// <summary>What the host shares (spec §5). Vocabulary mirrors CE's <c>LibraryShareMode</c>: nothing, everything, or chosen lists.</summary>
public enum ShareMode
{
    None,
    All,
    Selected,
}

/// <summary>The host's sharing scope. In <see cref="ShareMode.Selected"/> only issues reachable from the listed lists are shared.</summary>
public sealed class ShareScope
{
    public ShareMode Mode { get; set; } = ShareMode.None;

    public List<int> ReadingListIds { get; set; } = new();

    public List<int> CollectionIds { get; set; } = new();

    /// <summary>Issue-target smart lists only in v1; series-target smart lists are not shareable yet.</summary>
    public List<int> SmartListIds { get; set; } = new();
}

/// <summary>
/// Host sharing configuration, kept in a small JSON file under the app data dir instead of the
/// database: it needs no migration (the project's migration chain has been a recurring source of
/// trouble), it travels with <c>PAPERBUNKR_DATA_DIR</c> isolation, and the password is only ever a
/// PBKDF2 hash. <see cref="InstanceId"/> is the host's stable identity (spec §7.1) - minted once here.
/// </summary>
public sealed class ShareSettings
{
    public bool Enabled { get; set; }

    public int Port { get; set; } = 7614;

    public string DisplayName { get; set; } = Environment.MachineName;

    public string InstanceId { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public ShareScope Scope { get; set; } = new();
}

public sealed class ShareSettingsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;

    public ShareSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    /// <summary>Reads the settings, creating defaults (with a fresh <see cref="ShareSettings.InstanceId"/>) on first use. A corrupt file is replaced by defaults but keeps nothing of the old identity.</summary>
    public ShareSettings Load()
    {
        ShareSettings? settings = null;
        try
        {
            if (File.Exists(_path))
            {
                settings = JsonSerializer.Deserialize<ShareSettings>(File.ReadAllText(_path), Json);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            settings = null;
        }

        bool created = settings is null;
        settings ??= new ShareSettings();
        settings.Scope ??= new ShareScope();

        if (string.IsNullOrWhiteSpace(settings.InstanceId))
        {
            settings.InstanceId = Guid.NewGuid().ToString("D");
            created = true;
        }

        if (created)
        {
            Save(settings);
        }

        return settings;
    }

    public void Save(ShareSettings settings)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write-then-replace so a crash mid-write can't leave a half-written settings file.
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, _path, overwrite: true);
    }
}
