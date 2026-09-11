using ClusterLibraryManager.Persistence;
using LiteDB;

namespace ClusterLibraryManager.Organizing;

/// <summary>One real Move recorded for possible undo (design doc §8) - lightweight first pass, not
/// CE's full <c>UndoMover</c>/<c>undo.dat</c> machinery. Only Move is undoable (a Copy leaves the
/// original untouched; there's nothing to reverse); Simulate never reaches this at all since it
/// performs no real I/O.</summary>
public sealed class UndoLogEntry
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public string OldPath { get; set; } = string.Empty;
    public string NewPath { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
}

/// <summary>
/// LiteDB-backed undo log (design doc §8). Write ordering is the actual fix for the LiteDB/core-
/// SQLite dual-write concern raised in review (design doc §6): the core file move + `Issue.FilePath`
/// update happen first and are authoritative in <see cref="LibraryOrganizerService"/>; this class's
/// <see cref="Record"/> call happens strictly after, wrapped in its own try/catch by the caller - a
/// failure here degrades only this one item's undo coverage, never the already-committed core state.
/// </summary>
public sealed class UndoLog
{
    private const string CollectionName = "undo_log";
    private readonly PluginDatabase _database;

    public UndoLog(PluginDatabase database)
    {
        _database = database;
    }

    public void Record(int batchId, string oldPath, string newPath)
    {
        Collection().Insert(new UndoLogEntry
        {
            BatchId = batchId,
            OldPath = oldPath,
            NewPath = newPath,
            TimestampUtc = DateTime.UtcNow,
        });
    }

    /// <summary>The most recent batch's entries, most-recently-moved first - what "Undo last
    /// organize" reverses.</summary>
    public IReadOnlyList<UndoLogEntry> GetLastBatch()
    {
        ILiteCollection<UndoLogEntry> collection = Collection();
        UndoLogEntry? latest = collection.Query().OrderByDescending(e => e.Id).FirstOrDefault();
        if (latest is null)
        {
            return Array.Empty<UndoLogEntry>();
        }

        return collection.Query()
            .Where(e => e.BatchId == latest.BatchId)
            .OrderByDescending(e => e.Id)
            .ToList();
    }

    /// <summary>Removes a batch's entries once it's been undone (or once the caller has otherwise
    /// decided they no longer apply) - prevents "Undo last organize" from re-offering an already-
    /// reversed batch.</summary>
    public void DeleteBatch(int batchId) => Collection().DeleteMany(e => e.BatchId == batchId);

    public static int NewBatchId() => Environment.TickCount;

    private ILiteCollection<UndoLogEntry> Collection() => _database.GetCollection<UndoLogEntry>(CollectionName);
}
