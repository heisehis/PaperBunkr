using System;
using System.IO;
using System.Text.Json;

namespace Paperbunkr.App.Services.Covers;

/// <summary>
/// The small JSON sidecar that tracks cover-cache identity and health
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 2).
/// Lives next to the caches at <c>%AppData%\Paperbunkr\cover-cache-state.json</c>. No database
/// involvement - all of the root-fix's new state is here.
///
/// <list type="bullet">
/// <item><c>SchemaVersion</c> - bumped to 2 once <see cref="CoverCacheUpgrade"/> has flattened the
/// legacy <c>{id}-{hash}.jpg</c> files to <c>{id}.jpg</c>.</item>
/// <item><c>Generation</c> - a GUID reissued whenever the library is rebuilt (id reassignment); the
/// in-memory caches are cleared and the disk caches attic'd at the same time.</item>
/// <item><c>IssueCount</c> / <c>BookCount</c> - the library sizes recorded at the last successful
/// full cover pass; the every-launch reconcile compares against them to spot an unannounced
/// rebuild (a large drop) it wasn't wired for.</item>
/// <item><c>RebuildPending</c> - set by a path that can't attic in-process (DB restore just before
/// relaunch); honoured on the next startup reconcile.</item>
/// </list>
///
/// Best-effort: every read falls back to <see cref="Default"/> on a missing/corrupt file, every
/// write swallows IO failures (a stale sidecar just means one redundant pass later).
/// </summary>
public sealed record CoverCacheState
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; }

    public string Generation { get; init; } = string.Empty;

    public int IssueCount { get; init; }

    public int BookCount { get; init; }

    public bool RebuildPending { get; init; }

    public static CoverCacheState Default => new();

    /// <summary>Mutable so tests can point the sidecar at a temp folder - never set outside a test's own setup/teardown.</summary>
    public static string FilePath { get; set; } = BuildDefaultPath();

    private static string BuildDefaultPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Paperbunkr", "cover-cache-state.json");
    }

    public static CoverCacheState Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return Default;
            }

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<CoverCacheState>(json) ?? Default;
        }
        catch (Exception)
        {
            return Default;
        }
    }

    public void Write()
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // A sidecar write failure is non-fatal - the cache itself is unaffected.
        }
    }

    /// <summary>Record current library sizes after a successful full cover pass and stamp the schema version.</summary>
    public static void RecordCounts(int issueCount, int bookCount) =>
        WithSchema(s => s with { IssueCount = issueCount, BookCount = bookCount });

    /// <summary>Record just the comic count (the comic cover pass) without disturbing the book count.</summary>
    public static void RecordIssueCount(int issueCount) => WithSchema(s => s with { IssueCount = issueCount });

    /// <summary>Record just the book count (the book cover pass) without disturbing the comic count.</summary>
    public static void RecordBookCount(int bookCount) => WithSchema(s => s with { BookCount = bookCount });

    private static void WithSchema(Func<CoverCacheState, CoverCacheState> mutate)
    {
        var current = Read();
        var next = mutate(current) with
        {
            SchemaVersion = CurrentSchemaVersion,
            Generation = string.IsNullOrEmpty(current.Generation) ? Guid.NewGuid().ToString("N") : current.Generation,
        };
        next.Write();
    }

    /// <summary>Reissue the generation GUID and clear <see cref="RebuildPending"/> - called as part of a rebuild purge.</summary>
    public static void NewGeneration()
    {
        var current = Read();
        (current with
        {
            SchemaVersion = CurrentSchemaVersion,
            Generation = Guid.NewGuid().ToString("N"),
            RebuildPending = false,
        }).Write();
    }

    /// <summary>Defer a rebuild purge to the next startup (used by the DB-restore path, which can't attic mid-relaunch).</summary>
    public static void MarkRebuildPending()
    {
        (Read() with { RebuildPending = true }).Write();
    }

    /// <summary>
    /// True when the current library is less than half the size recorded at the last full cover
    /// pass - the heuristic signal for a rebuild/restore that no explicit hook caught. Never fires
    /// on a first run (no recorded counts) or when the library legitimately shrank a little.
    /// </summary>
    public static bool LooksLikeUnannouncedRebuild(int currentIssueCount, int currentBookCount)
    {
        var s = Read();
        if (s.IssueCount == 0 && s.BookCount == 0)
        {
            return false;
        }

        bool issuesCollapsed = s.IssueCount > 0 && currentIssueCount * 2 < s.IssueCount;
        bool booksCollapsed = s.BookCount > 0 && currentBookCount * 2 < s.BookCount;
        return issuesCollapsed || booksCollapsed;
    }
}
