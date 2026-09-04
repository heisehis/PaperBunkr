using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Paperbunkr.Data;
using Timer = System.Timers.Timer;

namespace Paperbunkr.App.Services;

/// <summary>
/// Debounced, serial background pipeline for file metadata write-back
/// (docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md). Owned by
/// <c>MainViewModel</c> like <see cref="LiveFolderWatchService"/>. Trigger sites call
/// <see cref="Enqueue"/> fire-and-forget after their own <c>SaveChanges()</c> - the database write
/// is already the source of truth, the file write is best-effort.
///
/// Coalesces by issue id (a second <see cref="Enqueue"/> for a pending id just resets its debounce),
/// then processes one file at a time (7-Zip spawns a process per call; concurrent archive writes
/// invite corruption). Settings are re-read at flush time: nothing writes unless
/// <see cref="Data.Entities.AppSettings.WriteMetadataToFiles"/>; a non-<c>manual</c> item also needs
/// <see cref="Data.Entities.AppSettings.WriteMetadataAutomatically"/>. Outcomes over one flush are
/// summarised into a single toast.
/// </summary>
public class MetadataWriteBackQueue : IDisposable
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly MetadataFileWriteBackService _service;
    private readonly Action<string, string> _showToast;
    private readonly TimeSpan _debounceWindow;

    private readonly object _pendingLock = new();
    private readonly Dictionary<int, bool> _pending = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly Timer _debounceTimer;
    private bool _disposed;

    public MetadataWriteBackQueue(Action<string, string> showToast)
        : this(PaperbunkrDb.CreateContext, new MetadataFileWriteBackService(), showToast, TimeSpan.FromMilliseconds(300))
    {
    }

    /// <summary>Test seam - shorter debounce, injectable context/service.</summary>
    internal MetadataWriteBackQueue(
        Func<PaperbunkrDbContext> contextFactory,
        MetadataFileWriteBackService service,
        Action<string, string> showToast,
        TimeSpan debounceWindow)
    {
        _contextFactory = contextFactory;
        _service = service;
        _showToast = showToast;
        _debounceWindow = debounceWindow;

        _debounceTimer = new Timer(_debounceWindow.TotalMilliseconds) { AutoReset = false };
        _debounceTimer.Elapsed += OnDebounceElapsed;
    }

    /// <summary>
    /// Queue a write for <paramref name="issueId"/>. <paramref name="manual"/> = the user asked for
    /// it explicitly ("Write metadata to files" action) - bypasses the automatic-mode gate and any
    /// caller-side "did anything change" check.
    /// </summary>
    public void Enqueue(int issueId, bool manual = false)
    {
        if (_disposed)
        {
            return;
        }

        lock (_pendingLock)
        {
            _pending[issueId] = _pending.TryGetValue(issueId, out bool existing) && existing || manual;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void Enqueue(IEnumerable<int> issueIds, bool manual = false)
    {
        foreach (int id in issueIds)
        {
            Enqueue(id, manual);
        }
    }

    private void OnDebounceElapsed(object? sender, ElapsedEventArgs e) => _ = FlushAsync();

    /// <summary>Test hook - runs the pending batch now without waiting on the debounce timer.</summary>
    internal Task DrainNowAsync()
    {
        _debounceTimer.Stop();
        return FlushAsync();
    }

    private async Task FlushAsync()
    {
        Dictionary<int, bool> batch;
        lock (_pendingLock)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            batch = new Dictionary<int, bool>(_pending);
            _pending.Clear();
        }

        await _flushGate.WaitAsync().ConfigureAwait(false);
        try
        {
            bool masterEnabled;
            bool automaticEnabled;
            bool includeSidecar;
            using (var context = _contextFactory())
            {
                var settings = context.GetOrCreateAppSettings();
                masterEnabled = settings.WriteMetadataToFiles;
                automaticEnabled = settings.WriteMetadataAutomatically;
                includeSidecar = settings.WriteNativeSidecar;
            }

            if (!masterEnabled)
            {
                return;
            }

            int wrote = 0;
            int alreadyCurrent = 0;
            var skippedFormat = new List<string>();
            int failed = 0;
            string? lastFailureMessage = null;
            string? singleSkipToast = null;

            foreach (var (issueId, manual) in batch)
            {
                if (!manual && !automaticEnabled)
                {
                    continue;
                }

                var outcome = await _service.WriteAsync(issueId, includeSidecar).ConfigureAwait(false);
                switch (outcome.Result)
                {
                    case MetadataWriteBackResult.Success:
                        wrote++;
                        break;
                    case MetadataWriteBackResult.SkippedUnsupportedFormat:
                        skippedFormat.Add(outcome.FileName ?? "a file");
                        if (batch.Count == 1 && !manual)
                        {
                            singleSkipToast = $"{outcome.FileName} can't be updated (not a CBZ/CB7/CBT), so its metadata is in the library only.";
                        }
                        break;
                    case MetadataWriteBackResult.SkippedMissingFile:
                    case MetadataWriteBackResult.SkippedReadOnly:
                        alreadyCurrent++;
                        break;
                    case MetadataWriteBackResult.Failed:
                        failed++;
                        lastFailureMessage = outcome.ErrorMessage;
                        break;
                }
            }

            ReportBatch(wrote, skippedFormat, failed, lastFailureMessage, singleSkipToast);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private void ReportBatch(int wrote, List<string> skippedFormat, int failed, string? lastFailureMessage, string? singleSkipToast)
    {
        if (singleSkipToast is not null)
        {
            _showToast("Saved to library only", singleSkipToast);
            return;
        }

        if (wrote == 0 && skippedFormat.Count == 0 && failed == 0)
        {
            return;
        }

        var parts = new List<string>();
        if (wrote > 0)
        {
            parts.Add($"{wrote} file{(wrote == 1 ? "" : "s")} updated");
        }

        if (skippedFormat.Count > 0)
        {
            parts.Add($"{skippedFormat.Count} skipped (unsupported format)");
        }

        if (failed > 0)
        {
            parts.Add($"{failed} failed");
        }

        string message = string.Join(" · ", parts) + ".";
        if (failed > 0 && lastFailureMessage is not null)
        {
            message += $" Last error: {lastFailureMessage}";
        }

        _showToast(failed > 0 ? "Metadata write-back had errors" : "Metadata written to files", message);
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
        _flushGate.Dispose();
    }
}
