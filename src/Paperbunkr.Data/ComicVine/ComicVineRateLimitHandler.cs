using System.Net;
using System.Net.Http;

namespace Paperbunkr.Data.ComicVine;

/// <summary>
/// Who is asking. <see cref="High"/> is foreground work (manual scraping, UI browsing) and is the
/// default for every request; <see cref="Low"/> is the acquisition daemon's background polling.
/// </summary>
public enum ComicVineRequestPriority
{
    High = 0,
    Low = 1,
}

/// <summary>
/// The single choke point for all ComicVine HTTP traffic (docs/superpowers/specs/2026-09-19-comic-
/// acquisition-daemon-design.md §3):
/// <list type="bullet">
///   <item>Requests are spaced at least <see cref="Options.MinSpacing"/> apart across every caller
///   (ComicVine velocity-limits with HTTP 420 / status_code 107; community guidance is ~1.1 s).</item>
///   <item>A sliding one-hour window is capped at <see cref="Options.HourlyLimit"/>. <see cref="ComicVineRequestPriority.Low"/>
///   may only use the first <see cref="Options.LowPriorityLimit"/> of it, so the rest stays reserved for
///   <see cref="ComicVineRequestPriority.High"/> and a big background scan can never starve the UI.</item>
///   <item>High-priority waiters are always served before Low ones, including ones that arrive later.</item>
///   <item>An HTTP 420/429 (or <see cref="ReportRateLimited"/>, for a 200 whose body says status_code 107)
///   pauses <b>all</b> calls, with an escalating cool-off.</item>
/// </list>
/// The per-request timeout is applied here, only to the actual send, because <c>HttpClient.Timeout</c>
/// would also count time spent queued and abort a throttled Low-priority call that is merely waiting its turn.
/// </summary>
public sealed class ComicVineRateLimitHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<ComicVineRequestPriority> PriorityKey = new("Paperbunkr.ComicVine.Priority");

    public sealed class Options
    {
        public TimeSpan MinSpacing { get; init; } = TimeSpan.FromMilliseconds(1100);
        public int HourlyLimit { get; init; } = 200;
        public int LowPriorityLimit { get; init; } = 150;
        public TimeSpan Window { get; init; } = TimeSpan.FromHours(1);
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);
        /// <summary>Cool-off per consecutive violation (last entry repeats). Reset by any successful response.</summary>
        public IReadOnlyList<TimeSpan> CoolOffs { get; init; } =
            [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];
        public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
        public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = (t, ct) => Task.Delay(t, ct);
    }

    private sealed class Waiter
    {
        public readonly TaskCompletionSource Granted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly Options _options;
    private readonly object _gate = new();
    private readonly Queue<Waiter> _high = new();
    private readonly Queue<Waiter> _low = new();
    private readonly List<DateTimeOffset> _grants = new();
    private DateTimeOffset _lastGrant = DateTimeOffset.MinValue;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private int _consecutiveViolations;
    private bool _pumping;
    private CancellationTokenSource? _waitCts;

    public ComicVineRateLimitHandler(HttpMessageHandler innerHandler, Options? options = null)
        : base(innerHandler)
    {
        _options = options ?? new Options();
    }

    /// <summary>When all calls are paused after a rate-limit response; <c>null</c> when not paused.</summary>
    public DateTimeOffset? PausedUntil
    {
        get
        {
            lock (_gate)
            {
                return _options.Now() < _pausedUntil ? _pausedUntil : null;
            }
        }
    }

    /// <summary>
    /// Pauses every caller. Called by the handler itself on HTTP 420/429, and by callers that parse the
    /// body and see ComicVine's <c>status_code</c> 107 on a 200 response.
    /// </summary>
    public void ReportRateLimited(TimeSpan? retryAfter = null)
    {
        lock (_gate)
        {
            var coolOff = retryAfter ?? _options.CoolOffs[Math.Min(_consecutiveViolations, _options.CoolOffs.Count - 1)];
            _consecutiveViolations++;
            var until = _options.Now() + coolOff;
            if (until > _pausedUntil)
            {
                _pausedUntil = until;
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var priority = request.Options.TryGetValue(PriorityKey, out var p) ? p : ComicVineRequestPriority.High;
        await AcquireSlotAsync(priority, cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        var response = await base.SendAsync(request, timeout.Token).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.TooManyRequests or (HttpStatusCode)420)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            ReportRateLimited(retryAfter);
        }
        else if (response.IsSuccessStatusCode)
        {
            lock (_gate)
            {
                _consecutiveViolations = 0;
            }
        }

        return response;
    }

    private async Task AcquireSlotAsync(ComicVineRequestPriority priority, CancellationToken cancellationToken)
    {
        var waiter = new Waiter();
        using var registration = cancellationToken.Register(() => waiter.Granted.TrySetCanceled(cancellationToken));

        bool startPump;
        lock (_gate)
        {
            (priority == ComicVineRequestPriority.High ? _high : _low).Enqueue(waiter);
            // A High arrival must interrupt a long wait that was computed for a Low head-of-line.
            _waitCts?.Cancel();
            startPump = !_pumping;
            _pumping = true;
        }

        if (startPump)
        {
            _ = Task.Run(PumpAsync);
        }

        await waiter.Granted.Task.ConfigureAwait(false);
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            Waiter? granted = null;
            TimeSpan wait = TimeSpan.Zero;
            CancellationTokenSource? cts = null;

            lock (_gate)
            {
                DropCancelled(_high);
                DropCancelled(_low);

                var queue = _high.Count > 0 ? _high : _low.Count > 0 ? _low : null;
                if (queue is null)
                {
                    _pumping = false;
                    return;
                }

                var now = _options.Now();
                _grants.RemoveAll(g => now - g >= _options.Window);

                if (now < _pausedUntil)
                {
                    wait = _pausedUntil - now;
                }

                var sinceLast = now - _lastGrant;
                if (sinceLast < _options.MinSpacing)
                {
                    wait = Max(wait, _options.MinSpacing - sinceLast);
                }

                int limit = ReferenceEquals(queue, _low) ? _options.LowPriorityLimit : _options.HourlyLimit;
                if (_grants.Count >= limit)
                {
                    var frees = _grants[_grants.Count - limit] + _options.Window;
                    wait = Max(wait, frees - now);
                }

                if (wait <= TimeSpan.Zero)
                {
                    granted = queue.Dequeue();
                    _grants.Add(now);
                    _lastGrant = now;
                }
                else
                {
                    cts = new CancellationTokenSource();
                    _waitCts = cts;
                }
            }

            if (granted is not null)
            {
                if (!granted.Granted.TrySetResult())
                {
                    // Cancelled between the check and now: give the slot back.
                    lock (_gate)
                    {
                        if (_grants.Count > 0)
                        {
                            _grants.RemoveAt(_grants.Count - 1);
                        }
                    }
                }

                continue;
            }

            try
            {
                await _options.Delay(wait, cts!.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A new waiter arrived (possibly High): re-evaluate from the top.
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_waitCts, cts))
                    {
                        _waitCts = null;
                    }
                }

                cts!.Dispose();
            }
        }
    }

    private static void DropCancelled(Queue<Waiter> queue)
    {
        while (queue.Count > 0 && queue.Peek().Granted.Task.IsCanceled)
        {
            queue.Dequeue();
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
