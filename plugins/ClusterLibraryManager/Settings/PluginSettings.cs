using ClusterLibraryManager.Persistence;
using LiteDB;

namespace ClusterLibraryManager.Settings;

/// <summary>
/// The plugin's simple scalar settings (design doc §10 - live in the plugin's own database now that
/// this is a full-trust native plugin with its own storage, not `IPluginConfig`). A single singleton
/// document (always <see cref="Id"/> = 1), not a collection - there's exactly one active
/// configuration of these, separate from the (potentially many) <c>OrganizerProfile</c> rows.
/// </summary>
public sealed class PluginSettings
{
    public int Id { get; set; } = 1;

    public string? ApiKey { get; set; }

    /// <summary>CE's `ow_existing_b` (design doc §4) - overwrite fields that already have a value.</summary>
    public bool OverwriteExisting { get; set; } = true;

    /// <summary>CE's `ignore_blanks_b` - don't overwrite an existing value with a blank one.</summary>
    public bool IgnoreBlankValues { get; set; }

    /// <summary>CE's `autochoose_series_b` (design doc §4) - apply the top-scored match without
    /// showing the review dialog.</summary>
    public bool AutoChooseTopMatch { get; set; }

    public HashSet<ScrapeField> EnabledScrapeFields { get; set; } = new(Enum.GetValues<ScrapeField>());

    /// <summary>Self-contained automation (design doc §9 - Paperbunkr's Scheduled Tasks catalog is
    /// closed/non-extensible, verified; this plugin runs its own in-process timer instead).</summary>
    public bool AutomaticScrapeEnabled { get; set; }

    public int AutomaticScrapeIntervalHours { get; set; } = 24;

    public bool AutomaticOrganizeEnabled { get; set; }

    public int AutomaticOrganizeIntervalHours { get; set; } = 24;

    /// <summary>Which <c>OrganizerProfile</c> the automatic-organize timer uses - null means
    /// automation stays off regardless of <see cref="AutomaticOrganizeEnabled"/> until one is picked.</summary>
    public int? AutomaticOrganizeProfileId { get; set; }
}

/// <summary>LiteDB-backed load/save for the one <see cref="PluginSettings"/> document.</summary>
public sealed class PluginSettingsStore
{
    private const string CollectionName = "settings";
    private readonly PluginDatabase _database;

    public PluginSettingsStore(PluginDatabase database)
    {
        _database = database;
    }

    public PluginSettings Load() => Collection().FindById(1) ?? new PluginSettings();

    public void Save(PluginSettings settings)
    {
        settings.Id = 1;
        Collection().Upsert(settings);
    }

    private ILiteCollection<PluginSettings> Collection() => _database.GetCollection<PluginSettings>(CollectionName);
}
