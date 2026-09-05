using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Paperbunkr.Data;
using Timer = System.Timers.Timer;

namespace Paperbunkr.App.Services;

/// <summary>
/// Live per-folder watching (docs/superpowers/specs/2026-08-23-live-folder-watch-scanning-design.md)
/// - CE parity for <c>WatchedFolder.Watch</c> plus a deliberate Paperbunkr enhancement beyond CE
/// (confirmed with the user rather than assumed - see the spec's Context section for what CE's own
/// <c>FileSystemWatcher</c> actually did, which was much narrower). One <see cref="FileSystemWatcher"/>
/// per <c>WatchedFolder</c> row with <c>Watch = true</c>.
///
/// <c>Renamed</c> is handled immediately per-event (§3: "CE parity") - a single atomic path-sync
/// write, nothing to batch. <c>Created</c>/<c>Deleted</c> are debounced into one flush per quiet
/// period, so a bulk copy/delete produces one import pass and one toast rather than one per file,
/// and so a <c>Created</c> event (which fires before a large file finishes writing) has a chance to
/// settle before <see cref="LibraryFolderScanner"/> tries to open it.
/// </summary>
public class LiveFolderWatchService : IDisposable
{
    private const int FileReadyMaxAttempts = 5;

    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly LibraryFolderScanner _scanner;
    private readonly Action<string, string> _showToast;
    private readonly Action _onLibraryChanged;
    private readonly Action<int> _onFilesMissing;
    private readonly Action<IReadOnlyList<int>> _onFilesImported;
    private readonly TimeSpan _debounceWindow;
    private readonly TimeSpan _fileReadyRetryDelay;
    private readonly object _pendingLock = new();
    private readonly Dictionary<string, WatcherChangeTypes> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly Timer _debounceTimer;
    private readonly List<FileSystemWatcher> _watchers = new();
    private bool _disposed;

    public LiveFolderWatchService(Action<string, string> showToast, Action onLibraryChanged, Action<int>? onFilesMissing = null, Action<IReadOnlyList<int>>? onFilesImported = null)
        : this(PaperbunkrDb.CreateContext, new LibraryFolderScanner(), showToast, onLibraryChanged, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(500), onFilesMissing, onFilesImported)
    {
    }

    /// <summary>Test-only seam - shorter debounce/retry windows so tests don't have to sleep for seconds per case.</summary>
    internal LiveFolderWatchService(
        Func<PaperbunkrDbContext> contextFactory,
        LibraryFolderScanner scanner,
        Action<string, string> showToast,
        Action onLibraryChanged,
        TimeSpan debounceWindow,
        TimeSpan fileReadyRetryDelay,
        Action<int>? onFilesMissing = null,
        Action<IReadOnlyList<int>>? onFilesImported = null)
    {
        _contextFactory = contextFactory;
        _scanner = scanner;
        _showToast = showToast;
        _onLibraryChanged = onLibraryChanged;
        _onFilesMissing = onFilesMissing ?? (_ => { });
        _onFilesImported = onFilesImported ?? (_ => { });
        _debounceWindow = debounceWindow;
        _fileReadyRetryDelay = fileReadyRetryDelay;

        _debounceTimer = new Timer(_debounceWindow.TotalMilliseconds) { AutoReset = false };
        _debounceTimer.Elapsed += OnDebounceElapsed;
    }

    /// <summary>Builds watchers from the current DB state. Called once at app startup.</summary>
    public void Start() => Reload();

    /// <summary>
    /// Tears down and rebuilds every watcher from the current DB state - called after any
    /// add/remove/Watch-toggle on <c>WatchedFolders</c> in Preferences.
    /// </summary>
    public void Reload()
    {
        StopWatchers();

        using var context = _contextFactory();
        foreach (var folder in context.WatchedFolders.Where(w => w.Watch).Select(w => w.Path).ToList())
        {
            TryAddWatcher(folder);
        }
    }

    private void TryAddWatcher(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };
            watcher.Created += OnCreatedOrDeleted;
            watcher.Deleted += OnCreatedOrDeleted;
            watcher.Renamed += OnRenamed;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Folder inaccessible (deleted, network share dropped) between being watched and this
            // running - same try/catch-and-skip contract as CE's WatchFolder.UpdateWatcher.
        }
    }

    private void StopWatchers()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
            }
        }

        _watchers.Clear();
    }

    private void OnCreatedOrDeleted(object? sender, FileSystemEventArgs e)
    {
        // Ignore the app's own metadata write-back churn (a .pbwrite-*.tmp copy + File.Replace) -
        // without this, the replace reads as a Deleted event and the issue gets flagged missing.
        if (FileWriteBackCoordinator.IsSuppressed(e.FullPath))
        {
            return;
        }

        lock (_pendingLock)
        {
            _pending[e.FullPath] = e.ChangeType;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    /// <summary>
    /// CE parity (<c>ComicDatabase.watcherRenamedNotification</c>, ported as-is) plus a Paperbunkr
    /// addition CE didn't need: clearing <c>FileIsMissing</c>, since CE had no persisted missing
    /// flag for a rename to accidentally leave stale.
    /// </summary>
    private void OnRenamed(object? sender, RenamedEventArgs e)
    {
        // Metadata write-back's File.Replace renames the real file aside to a ~RF*.TMP backup and
        // renames its own .pbwrite-*.tmp into place. Following either of those renames would repoint
        // the issue's FilePath at a file that's deleted milliseconds later (the "comic went missing
        // after an edit" bug). Neither endpoint being a real library path = nothing to sync.
        if (FileWriteBackCoordinator.IsSuppressed(e.OldFullPath) || FileWriteBackCoordinator.IsSuppressed(e.FullPath))
        {
            return;
        }

        try
        {
            using var context = _contextFactory();
            bool changed = false;

            if (Directory.Exists(e.FullPath))
            {
                var affected = context.Issues
                    .Where(i => i.FilePath != null)
                    .ToList()
                    .Where(i => i.FilePath!.StartsWith(e.OldFullPath, StringComparison.OrdinalIgnoreCase));

                foreach (var issue in affected)
                {
                    issue.FilePath = e.FullPath + issue.FilePath!.Substring(e.OldFullPath.Length);
                    issue.FileIsMissing = false;
                    changed = true;
                }
            }
            else
            {
                var issue = context.Issues.FirstOrDefault(i => i.FilePath == e.OldFullPath);
                if (issue is not null)
                {
                    issue.FilePath = e.FullPath;
                    issue.FileIsMissing = false;
                    changed = true;
                }
            }

            if (changed)
            {
                context.SaveChanges();
                _onLibraryChanged();
            }
        }
        catch (PathTooLongException)
        {
        }
    }

    private void OnDebounceElapsed(object? sender, ElapsedEventArgs e) => _ = FlushAsync();

    private async Task FlushAsync()
    {
        Dictionary<string, WatcherChangeTypes> batch;
        lock (_pendingLock)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            batch = new Dictionary<string, WatcherChangeTypes>(_pending, StringComparer.OrdinalIgnoreCase);
            _pending.Clear();
        }

        await _flushGate.WaitAsync();
        try
        {
            var createdPaths = batch.Where(kv => kv.Value == WatcherChangeTypes.Created).Select(kv => kv.Key).ToList();
            var deletedPaths = batch.Where(kv => kv.Value == WatcherChangeTypes.Deleted).Select(kv => kv.Key).ToList();

            if (createdPaths.Count > 0)
            {
                await ImportCreatedFilesAsync(createdPaths);
            }

            if (deletedPaths.Count > 0)
            {
                MarkMissing(deletedPaths);
                _onLibraryChanged();
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task ImportCreatedFilesAsync(List<string> createdPaths)
    {
        var readyFiles = await WaitForFilesReadyAsync(createdPaths);
        if (readyFiles.Count == 0)
        {
            return;
        }

        var result = await _scanner.ImportNewFilesAsync(readyFiles, new Progress<(int Done, int Total)>());
        if (result.IssuesAdded == 0)
        {
            return;
        }

        // Same idempotent, presence-based pass Scan Now runs after import - safe to call broadly,
        // it only ever fills in issues still missing a cached cover.
        await new CoverThumbnailService(_contextFactory).GenerateAllAsync(new Progress<(int Done, int Total)>());

        _showToast(
            "New comics found",
            $"{result.IssuesAdded} new issue{(result.IssuesAdded == 1 ? "" : "s")} added across {result.SeriesTouched} series.");
        _onFilesImported(result.AddedIssueIds);
        _onLibraryChanged();
    }

    /// <summary>
    /// <c>Created</c> fires the instant the OS creates the file handle, often before a large file
    /// finishes writing - retries opening each path exclusively over a short bounded window before
    /// giving up on it for this flush (same "one bad file doesn't stop the batch" contract
    /// <see cref="LibraryFolderScanner"/> already has). A file still locked after this window isn't
    /// lost: the next manual Scan Now, or the next write to it (re-triggering <c>Created</c>), picks
    /// it up.
    /// </summary>
    private async Task<List<string>> WaitForFilesReadyAsync(List<string> paths)
    {
        var ready = new List<string>();
        foreach (var path in paths)
        {
            for (int attempt = 0; attempt < FileReadyMaxAttempts; attempt++)
            {
                if (!File.Exists(path))
                {
                    break;
                }

                try
                {
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    ready.Add(path);
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(_fileReadyRetryDelay);
                }
            }
        }

        return ready;
    }

    /// <summary>
    /// Closes a real, pre-existing gap found while designing this feature: nothing in the App layer
    /// ever set <c>Issue.FileIsMissing</c> based on a real-time disk check before this - only CE
    /// migration import and reading-list placeholder creation ever set it, so a natively-scanned
    /// issue whose file got deleted outside a relink flow was never flagged missing at all.
    /// <see cref="_onFilesMissing"/> fires once per flush (already batched with everything else in
    /// this method) so the caller can surface one Activity Center alert per bulk delete rather than
    /// one per file, pointing at the Needs Review "Missing Files" section where a file can be
    /// relinked, removed, or dismissed.
    /// </summary>
    private void MarkMissing(List<string> deletedPaths)
    {
        var pathSet = new HashSet<string>(deletedPaths, StringComparer.OrdinalIgnoreCase);

        using var context = _contextFactory();
        var issues = context.Issues
            .Where(i => i.FilePath != null && !i.FileIsMissing)
            .ToList()
            .Where(i => pathSet.Contains(i.FilePath!))
            // A Deleted event whose path is present again by flush time was an atomic
            // replace-in-place (our own write-back, an external editor's save, an AV scanner),
            // not a real deletion - never flag those missing.
            .Where(i => !File.Exists(i.FilePath!) && !Directory.Exists(i.FilePath!))
            .ToList();

        if (issues.Count == 0)
        {
            return;
        }

        foreach (var issue in issues)
        {
            issue.FileIsMissing = true;
        }

        context.SaveChanges();
        _onFilesMissing(issues.Count);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounceTimer.Elapsed -= OnDebounceElapsed;
        _debounceTimer.Dispose();
        StopWatchers();
        _flushGate.Dispose();
    }
}
