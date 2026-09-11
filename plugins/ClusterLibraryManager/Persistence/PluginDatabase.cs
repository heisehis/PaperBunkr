using LiteDB;

namespace ClusterLibraryManager.Persistence;

/// <summary>
/// The plugin's own single LiteDB file (design doc §10) - one shared connection for everything this
/// plugin persists locally (ComicVine match memory, organizer profiles, the undo log, simple scalar
/// settings), isolated from the core `paperbunkr.db` schema and migration history entirely. One
/// instance per plugin session, not one per concern: multiple <see cref="LiteDatabase"/> instances
/// against the same file from one process risk lock conflicts, so every store in this plugin takes a
/// <see cref="PluginDatabase"/> in its constructor rather than opening its own file.
/// </summary>
public sealed class PluginDatabase : IDisposable
{
    private readonly LiteDatabase _db;

    public PluginDatabase(string databasePath)
    {
        string? dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _db = new LiteDatabase(databasePath);
    }

    public ILiteCollection<T> GetCollection<T>(string name) => _db.GetCollection<T>(name);

    public void Dispose() => _db.Dispose();
}
