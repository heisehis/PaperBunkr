using System.Collections.Concurrent;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.Plugins;

/// <summary>What went wrong with a domain-hook invocation - reported to the host at most once per command per session.</summary>
public enum DomainHookProblemKind
{
    /// <summary>The command threw.</summary>
    Failed,

    /// <summary>The command was still running after the timeout; its cancellation token was tripped.</summary>
    TimedOut,

    /// <summary>The command's queue was full, so the oldest waiting event was dropped.</summary>
    EventsDropped,
}

/// <summary>One problem worth telling the user about. <see cref="Message"/> is human-readable.</summary>
public sealed record DomainHookProblem(Command Command, string Hook, DomainHookProblemKind Kind, string Message);

/// <summary>
/// Runs the Plugin API 4.1 domain-event hooks safely (docs/superpowers/specs/2026-09-20-plugin-api-4-1-
/// design.md §5.1). These hooks fire from places that must never wait on a plugin - the reader
/// finishing a book, a library scan ending - so:
/// <list type="bullet">
/// <item><b>Fire-and-forget.</b> <see cref="Dispatch{TGlobals}"/> returns immediately; the plugin runs on
/// the thread pool. The return value is ignored (these hooks are notification-only).</item>
/// <item><b>One invocation at a time per command, with a bounded queue.</b> Each command owns a serial
/// lane with room for <see cref="DefaultQueueCapacity"/> waiting events; when it's full the <em>oldest</em>
/// waiting event is dropped. Queue rather than coalesce: a <c>BookRead</c> or <c>MissingFileDetected</c>
/// event is individually meaningful, so keeping only the latest would lose reads. This bound, not the
/// token, is what protects the thread pool from a plugin that hangs.</item>
/// <item><b>A cooperative cancellation token and a timeout.</b> Every invocation gets a
/// <see cref="CancellationToken"/> (on <see cref="PluginGlobals.CancellationToken"/>) that is tripped
/// after <see cref="DefaultTimeout"/>. A plugin that ignores it, or blocks synchronously, keeps running -
/// cancellation cannot abort a thread. So a command still running after its timeout is treated as
/// <b>hung</b>: it gets no new invocation until that call returns, and its queue keeps filling and
/// dropping as above. A hung plugin therefore costs at most one blocked thread and a bounded queue.</item>
/// <item><b>Failures are reported, not thrown, and not repeated.</b> Each kind of problem is reported
/// once per command per session via the callback, so a plugin that fails on every event doesn't spam
/// alerts.</item>
/// </list>
/// Only enabled, non-broken commands run (<see cref="PluginEngine.GetCommands"/>).
/// </summary>
public sealed class DomainHookDispatcher
{
    public const int DefaultQueueCapacity = 16;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly PluginEngine _engine;
    private readonly Action<DomainHookProblem> _onProblem;
    private readonly TimeSpan _timeout;
    private readonly int _capacity;
    private readonly ConcurrentDictionary<Command, Lane> _lanes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(Command, DomainHookProblemKind)> _reported = new();
    private readonly object _reportedGate = new();

    /// <param name="onProblem">Called at most once per (command, problem kind) per session. Never throws into the dispatcher.</param>
    /// <param name="timeout">Test seam - production uses <see cref="DefaultTimeout"/>.</param>
    /// <param name="queueCapacity">Test seam - production uses <see cref="DefaultQueueCapacity"/>.</param>
    public DomainHookDispatcher(PluginEngine engine, Action<DomainHookProblem> onProblem, TimeSpan? timeout = null, int queueCapacity = DefaultQueueCapacity)
    {
        _engine = engine;
        _onProblem = onProblem;
        _timeout = timeout ?? DefaultTimeout;
        _capacity = queueCapacity;
    }

    /// <summary>
    /// Queues <paramref name="hook"/> for every enabled command registered on it and returns
    /// immediately. <paramref name="globalsFactory"/> builds the typed globals per command, from that
    /// command's own environment; the dispatcher then sets <see cref="PluginGlobals.CancellationToken"/>.
    /// </summary>
    public void Dispatch<TGlobals>(string hook, Func<IPluginEnvironment, TGlobals> globalsFactory)
        where TGlobals : PluginGlobals
    {
        foreach (Command command in _engine.GetCommands(hook).ToList())
        {
            IPluginEnvironment? environment = command.Environment;
            if (environment is null)
            {
                continue;
            }

            Lane lane = _lanes.GetOrAdd(command, c => new Lane(this, c));
            lane.Enqueue(hook, token =>
            {
                TGlobals globals = globalsFactory(environment);
                globals.CancellationToken = token;
                return command.InvokeAsync(globals);
            });
        }
    }

    /// <summary>True while no command has a running or queued invocation.</summary>
    public bool IsIdle => _lanes.Values.All(l => l.IsIdle);

    /// <summary>Test seam: waits until <see cref="IsIdle"/>, or returns false after <paramref name="timeout"/>.</summary>
    public async Task<bool> WaitForIdleAsync(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!IsIdle)
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Whether <paramref name="command"/> is currently past its timeout and still running.</summary>
    public bool IsHung(Command command) => _lanes.TryGetValue(command, out Lane? lane) && lane.Hung;

    /// <summary>How many events are waiting behind <paramref name="command"/>'s running invocation.</summary>
    public int QueuedCount(Command command) => _lanes.TryGetValue(command, out Lane? lane) ? lane.QueuedCount : 0;

    private void Report(Command command, string hook, DomainHookProblemKind kind, string message)
    {
        lock (_reportedGate)
        {
            if (!_reported.Add((command, kind)))
            {
                return;
            }
        }

        try
        {
            _onProblem(new DomainHookProblem(command, hook, kind, message));
        }
        catch (Exception)
        {
            // Reporting a problem must never become a problem.
        }
    }

    private sealed record Work(string Hook, Func<CancellationToken, Task<object?>> Run);

    private sealed class Lane
    {
        private readonly DomainHookDispatcher _owner;
        private readonly Command _command;
        private readonly object _gate = new();
        private readonly Queue<Work> _queue = new();
        private bool _running;

        public Lane(DomainHookDispatcher owner, Command command)
        {
            _owner = owner;
            _command = command;
        }

        public bool Hung { get; private set; }

        public bool IsIdle
        {
            get
            {
                lock (_gate)
                {
                    return !_running && _queue.Count == 0;
                }
            }
        }

        public int QueuedCount
        {
            get
            {
                lock (_gate)
                {
                    return _queue.Count;
                }
            }
        }

        public void Enqueue(string hook, Func<CancellationToken, Task<object?>> run)
        {
            bool dropped = false;
            bool start = false;
            lock (_gate)
            {
                if (_queue.Count >= _owner._capacity)
                {
                    _queue.Dequeue();
                    dropped = true;
                }

                _queue.Enqueue(new Work(hook, run));
                if (!_running)
                {
                    _running = true;
                    start = true;
                }
            }

            if (dropped)
            {
                _command.Stats.RecordDropped();
                _owner.Report(_command, hook, DomainHookProblemKind.EventsDropped,
                    $"\"{_command.Name}\" ({hook}) can't keep up - its queue of {_owner._capacity} waiting events is full, so the oldest is being dropped.");
            }

            if (start)
            {
                _ = Task.Run(LoopAsync);
            }
        }

        private async Task LoopAsync()
        {
            while (true)
            {
                Work work;
                lock (_gate)
                {
                    if (!_queue.TryDequeue(out work!))
                    {
                        _running = false;
                        return;
                    }
                }

                await RunOneAsync(work).ConfigureAwait(false);
            }
        }

        private async Task RunOneAsync(Work work)
        {
            using var cancellation = new CancellationTokenSource();
            Task<object?> invocation;
            try
            {
                // On the thread pool, so a plugin that blocks synchronously before its first await
                // occupies a pool thread rather than this loop.
                invocation = Task.Run(() => work.Run(cancellation.Token));
            }
            catch (Exception ex)
            {
                _owner.Report(_command, work.Hook, DomainHookProblemKind.Failed, FailureMessage(work.Hook, ex));
                return;
            }

            bool timedOut = false;
            using (var delayCancellation = new CancellationTokenSource())
            {
                Task finished = await Task.WhenAny(invocation, Task.Delay(_owner._timeout, delayCancellation.Token)).ConfigureAwait(false);
                delayCancellation.Cancel();
                if (finished != invocation)
                {
                    timedOut = true;
                    cancellation.Cancel();
                    Hung = true;
                    _command.Stats.RecordTimeout();
                    _owner.Report(_command, work.Hook, DomainHookProblemKind.TimedOut,
                        $"\"{_command.Name}\" ({work.Hook}) is still running after {_owner._timeout.TotalSeconds:0.#}s and was asked to stop. It won't be given new events until it finishes.");
                }
            }

            try
            {
                // Held here even after a timeout: this lane starts nothing new until the call returns.
                await invocation.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timedOut)
            {
                // The expected way for a cooperative plugin to honour the cancellation we just asked for.
            }
            catch (Exception ex)
            {
                _owner.Report(_command, work.Hook, DomainHookProblemKind.Failed, FailureMessage(work.Hook, ex));
            }
            finally
            {
                Hung = false;
            }
        }

        private string FailureMessage(string hook, Exception ex) => $"\"{_command.Name}\" ({hook}) failed: {ex.Message}";
    }
}
