using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Paperbunkr.App.Services;

/// <summary>
/// One heavy background job at a time (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md §4.3). A library scan, a
/// Verify pass, a metadata sync, a scrape, an organize run and a Metron sync each load a large part of the library and read a lot
/// of disk; run together on a two-core machine they starve the UI and multiply the memory in use. Each of them runs its work
/// through <see cref="RunAsync{T}"/>: the first in runs, the rest wait, and a job the user started goes ahead of scheduled ones.
///
/// <list type="bullet">
///   <item><b>Re-entrant.</b> Work already inside the lane that starts more heavy work (a scheduled task that calls the scrape
///   coordinator) does not wait on itself.</item>
///   <item><b>Cancellable while waiting.</b> A waiting job whose token trips leaves the queue with <see cref="OperationCanceledException"/>.</item>
///   <item><b>Off by default.</b> <see cref="Shared"/> starts as a pass-through so tests and tools that never asked for it are not
///   serialized against each other; the app switches it on at startup with <see cref="EnableShared"/> (the same shape as
///   <c>InputServiceLocator.Current</c>).</item>
/// </list>
/// Callers show the wait in the Activity Center with the existing queued-job pattern: <c>StartJob(..., startQueued: lane.IsBusy)</c>,
/// then <c>job.Begin()</c> as the first line of the work.
/// </summary>
public sealed class HeavyJobLane
{
    private sealed class Waiter
    {
        public TaskCompletionSource<bool> Signal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly AsyncLocal<HeavyJobLane?> s_inside = new();

    private readonly bool _serialize;
    private readonly object _gate = new();
    private readonly LinkedList<Waiter> _userWaiters = new();
    private readonly LinkedList<Waiter> _scheduledWaiters = new();
    private bool _busy;

    public HeavyJobLane(bool serialize = true)
    {
        _serialize = serialize;
    }

    /// <summary>The lane every heavy job in the app goes through. A pass-through until <see cref="EnableShared"/>.</summary>
    public static HeavyJobLane Shared { get; private set; } = new(serialize: false);

    /// <summary>Called once at app startup: from here on heavy jobs run one at a time.</summary>
    public static void EnableShared()
    {
        if (!Shared._serialize)
        {
            Shared = new HeavyJobLane();
        }
    }

    /// <summary>True while a job holds the lane. A job started now would wait (unless it is started from inside the lane).</summary>
    public bool IsBusy
    {
        get { lock (_gate) { return _busy; } }
    }

    public int WaitingCount
    {
        get { lock (_gate) { return _userWaiters.Count + _scheduledWaiters.Count; } }
    }

    /// <summary>Whether a job started from the current async flow would have to wait: for <c>startQueued</c>.</summary>
    public bool WouldWait => _serialize && !ReferenceEquals(s_inside.Value, this) && IsBusy;

    public async Task RunAsync(bool userStarted, CancellationToken cancellationToken, Func<Task> work)
    {
        await RunAsync(userStarted, cancellationToken, async () =>
        {
            await work().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    /// <summary>Waits for the lane (unless already inside it), runs <paramref name="work"/>, and hands the lane to the next waiter.</summary>
    public async Task<T> RunAsync<T>(bool userStarted, CancellationToken cancellationToken, Func<Task<T>> work)
    {
        using var slot = Activate(await EnterAsync(userStarted, cancellationToken).ConfigureAwait(false));
        return await work().ConfigureAwait(false);
    }

    /// <summary>
    /// A held place in the lane; disposing it hands the lane on. Pass it through <see cref="Activate"/> in the method that
    /// awaited <see cref="EnterAsync"/>.
    /// </summary>
    public sealed class Slot : IDisposable
    {
        private HeavyJobLane? _lane;
        private bool _markedInside;

        internal Slot(HeavyJobLane? lane)
        {
            _lane = lane;
        }

        internal void MarkInside()
        {
            if (_lane is not null)
            {
                s_inside.Value = _lane;
                _markedInside = true;
            }
        }

        public void Dispose()
        {
            var lane = Interlocked.Exchange(ref _lane, null);
            if (lane is null)
            {
                return;
            }

            if (_markedInside && ReferenceEquals(s_inside.Value, lane))
            {
                s_inside.Value = null;
            }

            lane.Exit();
        }
    }

    /// <summary>
    /// Marks the calling async flow as inside the lane, so heavy work it starts does not wait on itself. This has to run in the
    /// method that holds the slot, after its <c>await</c>: an <see cref="AsyncLocal{T}"/> set inside <see cref="EnterAsync"/>
    /// itself would not flow back out to the caller. Usage:
    /// <c>using var slot = HeavyJobLane.Activate(await lane.EnterAsync(userStarted, token));</c>
    /// </summary>
    public static Slot Activate(Slot slot)
    {
        slot.MarkInside();
        return slot;
    }

    /// <summary>Waits for the lane and returns the held slot; at once, with a slot that holds nothing, when the lane is a pass-through or the caller is already inside it.</summary>
    public async Task<Slot> EnterAsync(bool userStarted, CancellationToken cancellationToken)
    {
        if (!_serialize || ReferenceEquals(s_inside.Value, this))
        {
            return new Slot(null);
        }

        await WaitForTurnAsync(userStarted, cancellationToken).ConfigureAwait(false);
        return new Slot(this);
    }

    private async Task WaitForTurnAsync(bool userStarted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Waiter waiter;
        LinkedList<Waiter> queue;
        lock (_gate)
        {
            if (!_busy)
            {
                _busy = true;
                return;
            }

            waiter = new Waiter();
            queue = userStarted ? _userWaiters : _scheduledWaiters;
            queue.AddLast(waiter);
        }

        using var registration = cancellationToken.Register(() =>
        {
            bool removed;
            lock (_gate)
            {
                removed = queue.Remove(waiter);
            }

            if (removed)
            {
                waiter.Signal.TrySetCanceled(cancellationToken);
            }
        });

        // Completes when Exit hands this waiter the lane (it then owns it), or is cancelled above (it never did).
        await waiter.Signal.Task.ConfigureAwait(false);
    }

    private void Exit()
    {
        Waiter? next = null;
        lock (_gate)
        {
            var queue = _userWaiters.Count > 0 ? _userWaiters : _scheduledWaiters;
            if (queue.First is { } node)
            {
                next = node.Value;
                queue.RemoveFirst();
            }
            else
            {
                _busy = false;
            }
        }

        // A waiter taken out of the queue here can no longer be cancelled out of it, so this always hands the lane over.
        next?.Signal.TrySetResult(true);
    }
}
