using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Paperbunkr.App.Services.LibrarySearch;

/// <summary>
/// How Library view rebuilds are scheduled (docs/superpowers/specs/2026-09-19-library-search-perf-
/// design.md §1). Two implementations share the contract "a newer request supersedes an older one":
/// <see cref="InlineLibraryViewScheduler"/> (everything synchronous - the default, and what every
/// existing test relies on) and <see cref="BackgroundLibraryViewScheduler"/> (debounce, worker-thread
/// compute, generation-guarded UI swap - installed by <c>MainViewModel</c>).
/// All members must be called on the UI thread.
/// </summary>
internal interface ILibraryViewScheduler
{
    /// <summary>Runs <paramref name="compute"/> (background in production) after <paramref name="debounce"/>, then
    /// <paramref name="apply"/> on the UI thread - unless a newer <see cref="Schedule{T}"/>/<see cref="RunNow{T}"/> arrived first.</summary>
    void Schedule<T>(TimeSpan debounce, Func<CancellationToken, T> compute, Action<T> apply);

    /// <summary>Synchronous compute + apply on the calling thread. Supersedes (cancels) any pending schedule.</summary>
    void RunNow<T>(Func<CancellationToken, T> compute, Action<T> apply);

    /// <summary>Coalesces calls that share <paramref name="key"/>: runs <paramref name="action"/> once,
    /// <paramref name="delay"/> after the last call, off the UI thread in production. Used for the search-text settings write.</summary>
    void Debounce(string key, TimeSpan delay, Action action);
}

/// <summary>Everything synchronous, no debounce. Default for <c>LibraryScreenViewModel</c> so construction, nav-in loads and tests behave exactly as before.</summary>
internal sealed class InlineLibraryViewScheduler : ILibraryViewScheduler
{
    public static readonly InlineLibraryViewScheduler Instance = new();

    public void Schedule<T>(TimeSpan debounce, Func<CancellationToken, T> compute, Action<T> apply) =>
        apply(compute(CancellationToken.None));

    public void RunNow<T>(Func<CancellationToken, T> compute, Action<T> apply) =>
        apply(compute(CancellationToken.None));

    public void Debounce(string key, TimeSpan delay, Action action) => action();
}

/// <summary>
/// Debounce on a <see cref="DispatcherTimer"/>, compute on the thread pool, swap on the UI thread.
/// Every <see cref="Schedule{T}"/>/<see cref="RunNow{T}"/> cancels the previous request's
/// <see cref="CancellationTokenSource"/> <b>and</b> bumps a generation counter; the generation check at
/// swap time is the correctness guard (a job that ignores its token and finishes anyway is still
/// dropped), cancellation is what stops an abandoned job from burning a thread-pool thread.
///
/// The UI post and the timer are injectable (defaults: <c>Dispatcher.UIThread.Post</c> at Background
/// priority, and a <see cref="DispatcherTimer"/>) so tests can drive the exact interleavings without a
/// live dispatcher loop.
/// </summary>
internal sealed class BackgroundLibraryViewScheduler : ILibraryViewScheduler
{
    private readonly Action<Action> _postToUi;
    private readonly Func<TimeSpan, Action, IDisposable> _startTimer;

    private long _generation;
    private CancellationTokenSource? _cts;
    private IDisposable? _debounceTimer;
    private readonly Dictionary<string, IDisposable> _keyedTimers = new();

    public BackgroundLibraryViewScheduler(Action<Action>? postToUi = null, Func<TimeSpan, Action, IDisposable>? startTimer = null)
    {
        _postToUi = postToUi ?? (action => Dispatcher.UIThread.Post(action, DispatcherPriority.Background));
        _startTimer = startTimer ?? StartDispatcherTimer;
    }

    /// <summary>Number of results dropped because a newer request superseded them - for tests/diagnostics.</summary>
    public int StaleResultsDropped { get; private set; }

    public void Schedule<T>(TimeSpan debounce, Func<CancellationToken, T> compute, Action<T> apply)
    {
        long generation = Supersede(out var token);

        if (debounce <= TimeSpan.Zero)
        {
            StartJob(generation, token, compute, apply);
            return;
        }

        _debounceTimer = _startTimer(debounce, () =>
        {
            _debounceTimer = null;
            if (generation == _generation)
            {
                StartJob(generation, token, compute, apply);
            }
        });
    }

    public void RunNow<T>(Func<CancellationToken, T> compute, Action<T> apply)
    {
        Supersede(out var token);
        apply(compute(token));
    }

    public void Debounce(string key, TimeSpan delay, Action action)
    {
        if (_keyedTimers.Remove(key, out var existing))
        {
            existing.Dispose();
        }

        _keyedTimers[key] = _startTimer(delay, () =>
        {
            _keyedTimers.Remove(key);
            _ = Task.Run(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    DiagnosticsService.LogCrash($"LibraryViewScheduler.Debounce({key})", ex, isTerminating: false);
                }
            });
        });
    }

    private long Supersede(out CancellationToken token)
    {
        _cts?.Cancel();
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        _cts = new CancellationTokenSource();
        token = _cts.Token;
        return ++_generation;
    }

    private void StartJob<T>(long generation, CancellationToken token, Func<CancellationToken, T> compute, Action<T> apply)
    {
        _ = Task.Run(() => compute(token), token).ContinueWith(
            task =>
            {
                if (task.IsCanceled || (task.IsFaulted && task.Exception!.GetBaseException() is OperationCanceledException))
                {
                    return;
                }

                if (task.IsFaulted)
                {
                    DiagnosticsService.LogCrash("LibraryViewScheduler.Compute", task.Exception!.GetBaseException(), isTerminating: false);
                    return;
                }

                T result = task.Result;
                _postToUi(() =>
                {
                    if (generation != _generation)
                    {
                        StaleResultsDropped++;
                        return;
                    }

                    apply(result);
                });
            },
            TaskScheduler.Default);
    }

    private static IDisposable StartDispatcherTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            tick();
        };
        timer.Start();
        return new DispatcherTimerHandle(timer);
    }

    private sealed class DispatcherTimerHandle : IDisposable
    {
        private readonly DispatcherTimer _timer;

        public DispatcherTimerHandle(DispatcherTimer timer) => _timer = timer;

        public void Dispose() => _timer.Stop();
    }
}
