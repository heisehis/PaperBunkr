using ClusterLibraryManager.Persistence;
using LiteDB;

namespace ClusterLibraryManager.ComicVine;

/// <summary>One remembered "this volume was chosen for a book with this search key" fact, backing
/// <see cref="MatchScoreCalculator"/>'s priorscore term (design doc §4/§10, grilling Q12) - the
/// equivalent of CE's flat <c>SERIES_FILE</c>, which nothing in Paperbunkr had before this plugin.</summary>
public sealed class ComicVineMatchMemoryEntry
{
    public int Id { get; set; }
    public string SearchKey { get; set; } = string.Empty;
    public int ChosenVolumeId { get; set; }
}

/// <summary>LiteDB-backed store for <see cref="ComicVineMatchMemoryEntry"/> rows, in the plugin's own
/// <see cref="PluginDatabase"/>.</summary>
public sealed class ComicVineMatchMemory
{
    private const string CollectionName = "comicvine_match_memory";
    private readonly PluginDatabase _database;

    public ComicVineMatchMemory(PluginDatabase database)
    {
        _database = database;
    }

    /// <summary>True if <paramref name="volumeId"/> was previously chosen for a book normalized to
    /// the same <paramref name="searchKey"/>.</summary>
    public bool WasChosen(string searchKey, int volumeId) =>
        Collection().Exists(e => e.SearchKey == searchKey && e.ChosenVolumeId == volumeId);

    /// <summary>Records that <paramref name="volumeId"/> was chosen for a book normalized to
    /// <paramref name="searchKey"/> - idempotent, does not duplicate an existing identical record.</summary>
    public void RecordChoice(string searchKey, int volumeId)
    {
        var collection = Collection();
        if (!collection.Exists(e => e.SearchKey == searchKey && e.ChosenVolumeId == volumeId))
        {
            collection.Insert(new ComicVineMatchMemoryEntry { SearchKey = searchKey, ChosenVolumeId = volumeId });
        }
    }

    /// <summary>Normalizes a series name into a stable lookup key (case-insensitive, whitespace-
    /// collapsed) so trivial formatting differences between two parses of "the same" series don't
    /// miss an otherwise-real match.</summary>
    public static string NormalizeSearchKey(string seriesName) =>
        string.Join(' ', seriesName.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private ILiteCollection<ComicVineMatchMemoryEntry> Collection() => _database.GetCollection<ComicVineMatchMemoryEntry>(CollectionName);
}
