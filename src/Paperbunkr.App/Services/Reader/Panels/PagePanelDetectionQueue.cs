using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services.Reader.Panels;

internal readonly record struct PagePanelDetectionKey(int IssueId, int PageIndex, bool RightToLeft);

/// <summary>Serializes panel inference, shares work for the same page, and drops queued work after its page leaves the reader.</summary>
internal sealed class PagePanelDetectionQueue
{
    private readonly object _gate = new();
    private readonly Func<Bitmap, bool, PagePanels> _analyze;
    private readonly Action<double> _recordDuration;
    private readonly Dictionary<PagePanelDetectionKey, Pending> _pending = new();
    private readonly LinkedList<Pending> _queue = new();
    private PagePanelDetectionKey? _currentKey;
    private bool _workerRunning;

    public PagePanelDetectionQueue(Func<Bitmap, bool, PagePanels> analyze, Action<double> recordDuration)
    {
        _analyze = analyze;
        _recordDuration = recordDuration;
    }

    public void SetCurrent(PagePanelDetectionKey? key)
    {
        lock (_gate)
        {
            _currentKey = key;
            var node = _queue.First;
            while (node is not null)
            {
                var next = node.Next;
                if (!node.Value.KeepWhenStale && node.Value.Key != _currentKey)
                {
                    _queue.Remove(node);
                    _pending.Remove(node.Value.Key);
                    node.Value.Completion.TrySetResult(null);
                }

                node = next;
            }
        }
    }

    public Task<PagePanels?> RequestAsync(PagePanelDetectionKey key, Bitmap page, bool keepWhenStale = false)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var existing))
            {
                existing.KeepWhenStale |= keepWhenStale;
                // If a report joins work that is still queued, use its owned bitmap snapshot so
                // navigating away cannot dispose the analyzer's input before the worker starts.
                if (keepWhenStale && !existing.Started)
                {
                    existing.Page = page;
                }

                return existing.Completion.Task;
            }

            var pending = new Pending(key, page) { KeepWhenStale = keepWhenStale };
            _pending.Add(key, pending);
            _queue.AddLast(pending);
            if (!_workerRunning)
            {
                _workerRunning = true;
                _ = Task.Run(ProcessQueue);
            }

            return pending.Completion.Task;
        }
    }

    private void ProcessQueue()
    {
        while (true)
        {
            Pending? pending;
            lock (_gate)
            {
                // Prefer the visible page over an older retained report request. Stale ordinary
                // work is discarded here as well as in SetCurrent to close the selection race.
                var node = _queue.First;
                while (node is not null)
                {
                    var next = node.Next;
                    if (!node.Value.KeepWhenStale && node.Value.Key != _currentKey)
                    {
                        _queue.Remove(node);
                        _pending.Remove(node.Value.Key);
                        node.Value.Completion.TrySetResult(null);
                    }

                    node = next;
                }

                node = _queue.First;
                while (node is not null && node.Value.Key != _currentKey)
                {
                    node = node.Next;
                }

                node ??= _queue.First;
                if (node is null)
                {
                    _workerRunning = false;
                    return;
                }

                pending = node.Value;
                pending.Started = true;
                _queue.Remove(node);
            }

            PagePanels result;
            var timer = Stopwatch.StartNew();
            try
            {
                result = _analyze(pending.Page, pending.Key.RightToLeft);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result = PagePanels.Whole;
            }
            finally
            {
                timer.Stop();
                _recordDuration(timer.Elapsed.TotalMilliseconds);
            }

            lock (_gate)
            {
                if (_pending.TryGetValue(pending.Key, out var current) && ReferenceEquals(current, pending))
                {
                    _pending.Remove(pending.Key);
                }
            }

            pending.Completion.TrySetResult(result);
        }
    }

    private sealed class Pending(PagePanelDetectionKey key, Bitmap page)
    {
        public PagePanelDetectionKey Key { get; } = key;
        public Bitmap Page { get; set; } = page;
        public bool KeepWhenStale { get; set; }
        public bool Started { get; set; }
        public TaskCompletionSource<PagePanels?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
