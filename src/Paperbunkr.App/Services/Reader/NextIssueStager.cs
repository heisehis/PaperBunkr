using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Paperbunkr.App.Services.Reader;

/// <summary>What the reader should do with the staged next issue at a given reading position (see <see cref="NextIssueStager.EvaluatePosition"/>).</summary>
public enum StagingAction
{
    /// <summary>Leave things as they are (the hysteresis band, or nothing relevant changed).</summary>
    None,

    /// <summary>Make sure the next issue is staged.</summary>
    Ensure,

    /// <summary>The reader moved back out of the end zone: drop what was staged.</summary>
    Discard,
}

/// <summary>The issue a stager should open next, as resolved by the reader (reading order: reading list, else series).</summary>
public sealed record StagingTarget(int IssueId, string? FilePath, bool IsRemote, bool FileIsMissing);

/// <summary>
/// Opens the next issue in the background while the reader is on the last few pages of the current one, so moving on is instant
/// (docs/superpowers/specs/2026-09-25-comic-reader-performance-design.md A; the model is Tachiyomi/Mihon's next-chapter preload). The staged
/// <see cref="ReaderImagePipeline"/> already has the archive open and parsed, the session held, and pages 0-1 read and decoded when the
/// reader adopts it in <c>Load</c>, which then skips only its own <see cref="ReaderImagePipeline.TryOpen"/>.
///
/// At most one pipeline is staged, for one issue. It is discarded when: it was adopted (ownership moves to the reader), the reader loads a
/// different issue, the reader falls back out of the end zone, it sits unused for <see cref="IdleTimeout"/>, the file changed on disk, the
/// setting is turned off, or the app shuts down. Everything is best-effort: any failure disposes the staged pipeline and is swallowed,
/// and <c>Load</c> then opens the issue exactly as it always did. The stager never touches the visible page, the reading position or the
/// reading-event log, and a staged pipeline does not feed <see cref="ReaderPerfStats.Current"/> until it is adopted.
///
/// Thread-safety: <see cref="EnsureStaged"/>, <see cref="TryAdopt"/>, <see cref="Discard"/> and <see cref="ExpireIfIdle"/> may be called from
/// the UI thread; opening happens on a thread-pool thread. Resolving the next issue needs the database, so the reader supplies that as a
/// delegate (the remote-row isolation rules keep <c>includeRemote</c> out of this file).
/// </summary>
public sealed class NextIssueStager : IDisposable
{
    /// <summary>How long a staged pipeline may sit unused before it is dropped.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Stage when this many pages or fewer remain after the current one (the last 3 pages).</summary>
    public const int StageWhenRemainingAtMost = 2;

    /// <summary>Drop what was staged once at least this many pages remain after the current one (below the last 5 pages). Between the two is a hysteresis band.</summary>
    public const int DiscardWhenRemainingAtLeast = 5;

    private readonly Func<int, int?, int?, int?, StagingTarget?> _resolveNext;
    private readonly Func<string, int?, ReaderImagePipeline?> _open;
    private readonly Func<string, string?> _stampOf;
    private readonly Action<ReaderImagePipeline> _dispose;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();

    private int _generation;
    private int? _stagedForIssueId;      // the current issue this staging (or attempt) belongs to
    private int? _stagedIssueId;         // the issue that was actually opened
    private ReaderImagePipeline? _pipeline;
    private string? _stagedStamp;
    private DateTime _lastTouchedUtc;
    private Timer? _idleTimer;
    private Task _pendingWork = Task.CompletedTask;
    private bool _disposed;

    /// <param name="resolveNext">(current issue id, series id, reading list id) → the issue that follows it in reading order, or null.</param>
    /// <param name="open">Opens a pipeline for a file with the user's memory limit; defaults to <see cref="ReaderImagePipeline.TryOpen"/>.</param>
    public NextIssueStager(
        Func<int, int?, int?, StagingTarget?> resolveNext,
        Func<string, int?, ReaderImagePipeline?>? open = null,
        Func<string, string?>? stampOf = null,
        Action<ReaderImagePipeline>? dispose = null,
        Func<DateTime>? utcNow = null)
        : this((issueId, seriesId, readingListId, _) => resolveNext(issueId, seriesId, readingListId), open, stampOf, dispose, utcNow)
    {
    }

    /// <param name="resolveNext">(current issue id, series id, reading list id, story event id) → the issue that follows it in reading order, or null.</param>
    public NextIssueStager(
        Func<int, int?, int?, int?, StagingTarget?> resolveNext,
        Func<string, int?, ReaderImagePipeline?>? open = null,
        Func<string, string?>? stampOf = null,
        Action<ReaderImagePipeline>? dispose = null,
        Func<DateTime>? utcNow = null)
    {
        _resolveNext = resolveNext;
        _open = open ?? ((path, limit) => ReaderImagePipeline.TryOpen(path, limit));
        _stampOf = stampOf ?? DefaultStampOf;
        _dispose = dispose ?? (p => p.Dispose());
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>The pure position rule: stage in the last 3 pages, drop below the last 5, otherwise do nothing.</summary>
    public static StagingAction EvaluatePosition(int position, int pageCount)
    {
        if (pageCount <= 0 || position < 0)
        {
            return StagingAction.None;
        }

        int remaining = pageCount - 1 - position;
        if (remaining <= StageWhenRemainingAtMost)
        {
            return StagingAction.Ensure;
        }

        return remaining >= DiscardWhenRemainingAtLeast ? StagingAction.Discard : StagingAction.None;
    }

    /// <summary>Size and modified time of the file: the marker for "this is the content I opened". Null when it is gone.</summary>
    internal static string? DefaultStampOf(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : null;
    }

    /// <summary>Whether something is staged (opened and ready) right now.</summary>
    public bool HasStaged
    {
        get { lock (_gate) { return _pipeline is not null; } }
    }

    /// <summary>The issue that is staged and ready, or null.</summary>
    public int? StagedIssueId
    {
        get { lock (_gate) { return _pipeline is not null ? _stagedIssueId : null; } }
    }

    /// <summary>Test seam: completes when the most recent staging attempt has finished (successfully or not).</summary>
    internal Task PendingWork
    {
        get { lock (_gate) { return _pendingWork; } }
    }

    /// <summary>
    /// Starts staging the issue that follows <paramref name="currentIssueId"/>, unless that is already staged or being staged. Staging
    /// for a different current issue replaces what was there. Returns immediately.
    /// </summary>
    public void EnsureStaged(int currentIssueId, int? seriesId, int? readingListId, int? memoryLimitMb, int viewportWidth, int? storyEventId = null)
    {
        int generation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_stagedForIssueId == currentIssueId)
            {
                _lastTouchedUtc = _utcNow();
                return;
            }

            DiscardLocked();
            _stagedForIssueId = currentIssueId;
            generation = ++_generation;
            _lastTouchedUtc = _utcNow();
            _pendingWork = Task.Run(() => Stage(generation, currentIssueId, seriesId, readingListId, storyEventId, memoryLimitMb, viewportWidth));
        }
    }

    private void Stage(int generation, int currentIssueId, int? seriesId, int? readingListId, int? storyEventId, int? memoryLimitMb, int viewportWidth)
    {
        ReaderImagePipeline? pipeline = null;
        try
        {
            var target = _resolveNext(currentIssueId, seriesId, readingListId, storyEventId);
            if (target is not { IsRemote: false, FileIsMissing: false, FilePath: { Length: > 0 } path })
            {
                return;
            }

            if (IsStale(generation))
            {
                return;
            }

            string? stamp = _stampOf(path);
            if (stamp is null)
            {
                return;
            }

            pipeline = _open(path, memoryLimitMb);
            if (pipeline is null)
            {
                return;
            }

            pipeline.RecordStats = false;
            pipeline.SetViewportWidth(viewportWidth);
            if (pipeline.PageCount > 0)
            {
                pipeline.SetVirtualizationWindow(0, Math.Min(1, pipeline.PageCount - 1));
            }

            lock (_gate)
            {
                if (_disposed || generation != _generation)
                {
                    // Superseded while it was opening: nobody wants it.
                }
                else
                {
                    _pipeline = pipeline;
                    _stagedIssueId = target.IssueId;
                    _stagedStamp = stamp;
                    _lastTouchedUtc = _utcNow();
                    EnsureIdleTimerLocked();
                    pipeline = null; // ownership moved
                }
            }
        }
        catch
        {
            // Best-effort: whatever went wrong, Load will simply open the issue itself.
        }
        finally
        {
            if (pipeline is not null)
            {
                SafeDispose(pipeline);
            }
        }
    }

    private bool IsStale(int generation)
    {
        lock (_gate)
        {
            return _disposed || generation != _generation;
        }
    }

    /// <summary>
    /// The reader is about to open <paramref name="issueId"/>: hand over the staged pipeline if that is the issue that was staged and its
    /// file is unchanged (it then feeds the perf overlay again). Otherwise, whatever was staged is discarded - the reader is going
    /// somewhere else - and null is returned.
    /// </summary>
    public ReaderImagePipeline? TryAdopt(int issueId, string? filePath)
    {
        ReaderImagePipeline? pipeline;
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            pipeline = _pipeline;
            bool matches = pipeline is not null
                           && _stagedIssueId == issueId
                           && filePath is not null
                           && _stagedStamp is not null
                           && _stampOf(filePath) == _stagedStamp;
            if (!matches)
            {
                DiscardLocked();
                return null;
            }

            // Ownership moves to the caller: forget it without disposing.
            _pipeline = null;
            _stagedIssueId = null;
            _stagedForIssueId = null;
            _stagedStamp = null;
            _generation++;
            StopIdleTimerLocked();
        }

        pipeline!.RecordStats = true;
        return pipeline;
    }

    /// <summary>Drops the staged pipeline (and cancels an attempt in flight).</summary>
    public void Discard()
    {
        lock (_gate)
        {
            DiscardLocked();
        }
    }

    /// <summary>Drops the staged pipeline if it has sat unused for <see cref="IdleTimeout"/>. Called by the idle timer; internal so tests can drive it.</summary>
    internal void ExpireIfIdle()
    {
        lock (_gate)
        {
            if (_pipeline is not null && _utcNow() - _lastTouchedUtc >= IdleTimeout)
            {
                DiscardLocked();
            }
        }
    }

    private void DiscardLocked()
    {
        _generation++; // any attempt in flight sees it is stale and disposes its own pipeline
        var pipeline = _pipeline;
        _pipeline = null;
        _stagedIssueId = null;
        _stagedForIssueId = null;
        _stagedStamp = null;
        StopIdleTimerLocked();
        if (pipeline is not null)
        {
            // Disposing waits for the pipeline's consumer thread, so keep that off the caller's (usually the UI) thread.
            _ = Task.Run(() => SafeDispose(pipeline));
        }
    }

    private void SafeDispose(ReaderImagePipeline pipeline)
    {
        try
        {
            _dispose(pipeline);
        }
        catch
        {
            // Already gone or failing to close: nothing useful to do.
        }
    }

    private void EnsureIdleTimerLocked()
    {
        _idleTimer ??= new Timer(_ => ExpireIfIdle(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private void StopIdleTimerLocked()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            DiscardLocked();
            _disposed = true;
        }
    }
}
