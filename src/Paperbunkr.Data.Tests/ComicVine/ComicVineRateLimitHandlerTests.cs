using System.Net;
using System.Net.Http;
using Paperbunkr.Data.ComicVine;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §3 - spacing, hourly budget, priority, pause.</summary>
public class ComicVineRateLimitHandlerTests
{
    private sealed class FakeClock
    {
        private readonly object _lock = new();
        private DateTimeOffset _now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Now { get { lock (_lock) return _now; } }
        public void Advance(TimeSpan by) { lock (_lock) _now += by; }
    }

    private sealed class RecordingInner : HttpMessageHandler
    {
        private readonly FakeClock _clock;
        private readonly Func<int, HttpStatusCode> _status;
        private int _count;
        public List<(string Url, DateTimeOffset At)> Calls { get; } = new();

        public RecordingInner(FakeClock clock, Func<int, HttpStatusCode>? status = null)
        {
            _clock = clock;
            _status = status ?? (_ => HttpStatusCode.OK);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int n;
            lock (Calls)
            {
                n = _count++;
                Calls.Add((request.RequestUri!.ToString(), _clock.Now));
            }

            return Task.FromResult(new HttpResponseMessage(_status(n)) { Content = new StringContent("{}") });
        }
    }

    private static ComicVineRateLimitHandler.Options Opts(FakeClock clock, Func<TimeSpan, CancellationToken, Task>? delay = null,
        int hourly = 200, int low = 150, TimeSpan? spacing = null) => new()
    {
        Now = () => clock.Now,
        Delay = delay ?? ((t, _) => { clock.Advance(t); return Task.CompletedTask; }),
        HourlyLimit = hourly,
        LowPriorityLimit = low,
        MinSpacing = spacing ?? TimeSpan.FromMilliseconds(1100),
    };

    private static HttpRequestMessage Req(string path, ComicVineRequestPriority priority = ComicVineRequestPriority.High) =>
        ComicVineHttp.Get("https://comicvine.test/" + path, priority);

    [Fact]
    public async Task Requests_AreSpacedAtLeastMinSpacingApart()
    {
        var clock = new FakeClock();
        var inner = new RecordingInner(clock);
        using var invoker = new HttpMessageInvoker(new ComicVineRateLimitHandler(inner, Opts(clock)));

        for (int i = 0; i < 3; i++)
        {
            using var _ = await invoker.SendAsync(Req("a" + i), CancellationToken.None);
        }

        Assert.Equal(3, inner.Calls.Count);
        Assert.True(inner.Calls[1].At - inner.Calls[0].At >= TimeSpan.FromMilliseconds(1100));
        Assert.True(inner.Calls[2].At - inner.Calls[1].At >= TimeSpan.FromMilliseconds(1100));
    }

    [Fact]
    public void TheShippedDefaults_ReserveAtLeastAFifthOfTheHourlyBudgetForInteractiveWork()
    {
        // The scraper's interactive actions and the background acquisition/scrape work share one 200/h budget
        // (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md section 3). Background (Low) may never take more than 80% of it.
        var defaults = new ComicVineRateLimitHandler.Options();

        Assert.True(defaults.LowPriorityLimit <= defaults.HourlyLimit * 0.8, $"Low may use {defaults.LowPriorityLimit} of {defaults.HourlyLimit}");
        Assert.True(defaults.LowPriorityLimit < defaults.HourlyLimit);
    }

    [Fact]
    public async Task AtTheShippedLimits_ABackgroundBacklogNeverLocksOutInteractiveRequests()
    {
        var clock = new FakeClock();
        var inner = new RecordingInner(clock);
        var defaults = new ComicVineRateLimitHandler.Options();
        using var invoker = new HttpMessageInvoker(new ComicVineRateLimitHandler(
            inner, Opts(clock, hourly: defaults.HourlyLimit, low: defaults.LowPriorityLimit, spacing: TimeSpan.Zero)));
        var start = clock.Now;

        // A backlog uses every Low slot the window allows...
        for (int i = 0; i < defaults.LowPriorityLimit; i++)
        {
            using var _ = await invoker.SendAsync(Req("low" + i, ComicVineRequestPriority.Low), CancellationToken.None);
        }

        // ...and a person clicking "Scrape with ComicVine..." right after is served immediately, for the whole reserved remainder.
        int reserved = defaults.HourlyLimit - defaults.LowPriorityLimit;
        for (int i = 0; i < reserved; i++)
        {
            using var _ = await invoker.SendAsync(Req("high" + i), CancellationToken.None);
        }

        Assert.Equal(start, clock.Now);                                       // nobody waited
        Assert.Equal(defaults.HourlyLimit, inner.Calls.Count);
    }

    [Fact]
    public async Task LowPriority_IsCappedBelowTheHourlyLimit_WhileHighKeepsGoing()
    {
        var clock = new FakeClock();
        var inner = new RecordingInner(clock);
        // hourly 5, low 3, no spacing: Low may only use 3 slots per window; 2 stay reserved for High.
        using var invoker = new HttpMessageInvoker(new ComicVineRateLimitHandler(inner, Opts(clock, hourly: 5, low: 3, spacing: TimeSpan.Zero)));
        var start = clock.Now;

        for (int i = 0; i < 3; i++)
        {
            using var _ = await invoker.SendAsync(Req("low" + i, ComicVineRequestPriority.Low), CancellationToken.None);
        }

        // Reserved budget: High proceeds immediately, no waiting.
        for (int i = 0; i < 2; i++)
        {
            using var _ = await invoker.SendAsync(Req("high" + i), CancellationToken.None);
        }

        Assert.Equal(start, clock.Now);
        Assert.Equal(5, inner.Calls.Count);

        // A 4th Low request has to wait for the window to free a slot.
        using var __ = await invoker.SendAsync(Req("low3", ComicVineRequestPriority.Low), CancellationToken.None);
        Assert.True(clock.Now - start >= TimeSpan.FromHours(1) - TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task High_IsServedBeforeQueuedLow_EvenWhenItArrivesLater()
    {
        var clock = new FakeClock();
        var inner = new RecordingInner(clock);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int delaysAfterGate = 0;
        var handler = new ComicVineRateLimitHandler(inner, Opts(clock, delay: async (t, ct) =>
        {
            await gate.Task.WaitAsync(ct);

            // The 1st post-gate delay is the pause; every later one follows exactly one grant. Each
            // grant resumes its request on a pool thread, so wait until that request has really reached
            // the inner handler - otherwise Calls order reflects thread scheduling, not grant order.
            int k = Interlocked.Increment(ref delaysAfterGate) - 1;
            while (k >= 1 && inner.Calls.Count < k)
            {
                await Task.Delay(1, ct);
            }

            clock.Advance(t);
        }, spacing: TimeSpan.FromSeconds(1)));
        using var invoker = new HttpMessageInvoker(handler);

        handler.ReportRateLimited(TimeSpan.FromMinutes(1)); // everyone has to wait, so all three queue up

        var low1 = invoker.SendAsync(Req("low1", ComicVineRequestPriority.Low), CancellationToken.None);
        var low2 = invoker.SendAsync(Req("low2", ComicVineRequestPriority.Low), CancellationToken.None);
        var high = invoker.SendAsync(Req("high", ComicVineRequestPriority.High), CancellationToken.None);

        gate.SetResult();
        await Task.WhenAll(low1, low2, high);

        Assert.EndsWith("high", inner.Calls[0].Url);
    }

    [Fact]
    public async Task Http420_PausesAllCallers_WithEscalatingCoolOff()
    {
        var clock = new FakeClock();
        var inner = new RecordingInner(clock, n => n == 0 ? (HttpStatusCode)420 : HttpStatusCode.OK);
        var handler = new ComicVineRateLimitHandler(inner, Opts(clock, spacing: TimeSpan.Zero));
        using var invoker = new HttpMessageInvoker(handler);

        using var first = await invoker.SendAsync(Req("first"), CancellationToken.None);
        Assert.Equal((HttpStatusCode)420, first.StatusCode);
        Assert.NotNull(handler.PausedUntil);

        var afterFirst = clock.Now;
        using var second = await invoker.SendAsync(Req("second", ComicVineRequestPriority.Low), CancellationToken.None);

        Assert.True(inner.Calls[1].At - afterFirst >= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ReportRateLimited_FromAResponseBody_PausesNextRequest()
    {
        var clock = new FakeClock();
        var inner = new RecordingInner(clock);
        var handler = new ComicVineRateLimitHandler(inner, Opts(clock, spacing: TimeSpan.Zero));
        using var invoker = new HttpMessageInvoker(handler);

        handler.ReportRateLimited(); // what ComicVineSource does on a 200 whose status_code is 107
        var start = clock.Now;
        using var response = await invoker.SendAsync(Req("x"), CancellationToken.None);

        Assert.True(inner.Calls[0].At - start >= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ASuccessfulResponse_ResetsTheEscalation()
    {
        var clock = new FakeClock();
        var inner = new RecordingInner(clock, n => n == 0 ? (HttpStatusCode)420 : HttpStatusCode.OK);
        var handler = new ComicVineRateLimitHandler(inner, Opts(clock, spacing: TimeSpan.Zero));
        using var invoker = new HttpMessageInvoker(handler);

        using var _1 = await invoker.SendAsync(Req("bad"), CancellationToken.None);   // violation 1 -> 1 min
        using var _2 = await invoker.SendAsync(Req("ok"), CancellationToken.None);    // success resets
        handler.ReportRateLimited();                                                  // should be 1 min again, not 5
        var start = clock.Now;
        using var _3 = await invoker.SendAsync(Req("after"), CancellationToken.None);

        var waited = inner.Calls[2].At - start;
        Assert.True(waited >= TimeSpan.FromMinutes(1));
        Assert.True(waited < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ACallerCancelledWhileQueued_ThrowsAndDoesNotConsumeASlot()
    {
        var clock = new FakeClock();
        var inner = new RecordingInner(clock);
        var handler = new ComicVineRateLimitHandler(inner, Opts(clock, spacing: TimeSpan.Zero));
        using var invoker = new HttpMessageInvoker(handler);

        handler.ReportRateLimited(TimeSpan.FromMinutes(1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoker.SendAsync(Req("never"), cts.Token));
        Assert.Empty(inner.Calls);
    }

    [Fact]
    public async Task RequestWithoutAPriority_DefaultsToHigh()
    {
        var clock = new FakeClock();
        var inner = new RecordingInner(clock);
        using var invoker = new HttpMessageInvoker(new ComicVineRateLimitHandler(inner, Opts(clock, hourly: 2, low: 0, spacing: TimeSpan.Zero)));
        var start = clock.Now;

        // low limit is 0, so a Low request would wait a full hour; a plain request must not.
        using var _ = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://comicvine.test/plain"), CancellationToken.None);

        Assert.Equal(start, clock.Now);
    }
}
