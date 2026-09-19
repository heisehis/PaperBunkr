using System.Collections.Concurrent;
using System.Diagnostics;
using Paperbunkr.App.Services.LibrarySearch;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Contract of <see cref="ILibraryViewScheduler"/> (docs/superpowers/specs/2026-09-19-library-search-
/// perf-design.md §1): a newer request supersedes an older one - cancels its token and drops its result -
/// and only the newest result is applied, on the "UI" side. The UI post and timers are injected so each
/// test drives the exact interleaving on the test thread, with no live Avalonia dispatcher (whose thread
/// ownership under the headless test bootstrap is not the xunit thread).
/// </summary>
public class LibraryViewSchedulerTests
{
    private sealed class FakeTimer : IDisposable
    {
        public required TimeSpan Interval { get; init; }
        public required Action Tick { get; init; }
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class Harness
    {
        public readonly ConcurrentQueue<Action> Ui = new();
        public readonly List<FakeTimer> Timers = new();
        public BackgroundLibraryViewScheduler Scheduler { get; }

        public Harness()
        {
            Scheduler = new BackgroundLibraryViewScheduler(
                Ui.Enqueue,
                (interval, tick) =>
                {
                    var timer = new FakeTimer { Interval = interval, Tick = tick };
                    Timers.Add(timer);
                    return timer;
                });
        }

        /// <summary>Waits until the worker has posted <paramref name="count"/> results, then runs them in order on this thread (the "UI" thread).</summary>
        public void PumpUi(int count)
        {
            Assert.True(WaitFor(() => Ui.Count >= count), $"expected {count} posted result(s), saw {Ui.Count}");
            while (Ui.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                return false;
            }

            Thread.Sleep(2);
        }

        return true;
    }

    [Fact]
    public void Inline_RunsComputeAndApplySynchronously_IgnoringDebounce()
    {
        var scheduler = InlineLibraryViewScheduler.Instance;
        var applied = new List<int>();

        scheduler.Schedule(TimeSpan.FromSeconds(30), _ => 1, applied.Add);
        scheduler.RunNow(_ => 2, applied.Add);

        Assert.Equal(new[] { 1, 2 }, applied);
    }

    [Fact]
    public void Inline_Debounce_RunsTheActionAtOnce()
    {
        int ran = 0;

        InlineLibraryViewScheduler.Instance.Debounce("k", TimeSpan.FromSeconds(30), () => ran++);

        Assert.Equal(1, ran);
    }

    [Fact]
    public void ZeroDebounce_ComputesOffTheCallingThread_AndAppliesWhereTheUiPumpRuns()
    {
        var harness = new Harness();
        int testThread = Environment.CurrentManagedThreadId;
        int computeThread = -1;
        int applyThread = -1;
        int applied = 0;

        harness.Scheduler.Schedule(
            TimeSpan.Zero,
            _ =>
            {
                computeThread = Environment.CurrentManagedThreadId;
                return 42;
            },
            result =>
            {
                applyThread = Environment.CurrentManagedThreadId;
                applied = result;
            });

        harness.PumpUi(1);

        Assert.Equal(42, applied);
        Assert.NotEqual(testThread, computeThread);
        Assert.Equal(testThread, applyThread);
        Assert.Empty(harness.Timers);
    }

    [Fact]
    public void NewerRequest_CancelsTheOlderToken_AndDropsItsResult()
    {
        var harness = new Harness();
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        bool firstSawCancellation = false;
        var applied = new List<string>();

        harness.Scheduler.Schedule(
            TimeSpan.Zero,
            token =>
            {
                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
                firstSawCancellation = token.IsCancellationRequested;

                // Deliberately finish anyway: the generation check, not the token, is the correctness guard.
                return "first";
            },
            applied.Add);

        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));

        harness.Scheduler.Schedule(TimeSpan.Zero, _ => "second", applied.Add);
        releaseFirst.Set();

        harness.PumpUi(2);

        Assert.Equal(new[] { "second" }, applied);
        Assert.True(firstSawCancellation, "the older job's token should have been cancelled");
        Assert.Equal(1, harness.Scheduler.StaleResultsDropped);
    }

    [Fact]
    public void RunNow_AppliesImmediately_AndSupersedesAPendingJob()
    {
        var harness = new Harness();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var applied = new List<string>();

        harness.Scheduler.Schedule(
            TimeSpan.Zero,
            _ =>
            {
                started.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return "background";
            },
            applied.Add);

        // Wait until the job is really running: a job cancelled before it starts never runs at all
        // (Task.Run(.., token)), which is fine but would leave nothing to observe being dropped.
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        harness.Scheduler.RunNow(_ => "now", applied.Add);
        Assert.Equal(new[] { "now" }, applied);

        release.Set();
        harness.PumpUi(1);

        Assert.Equal(new[] { "now" }, applied);
        Assert.Equal(1, harness.Scheduler.StaleResultsDropped);
    }

    [Fact]
    public void ThrowingCompute_IsSwallowed_AndNothingIsPosted()
    {
        var harness = new Harness();
        using var ran = new ManualResetEventSlim();
        int applied = 0;

        harness.Scheduler.Schedule<int>(TimeSpan.Zero, _ =>
        {
            ran.Set();
            throw new InvalidOperationException("boom");
        }, _ => applied++);

        Assert.True(ran.Wait(TimeSpan.FromSeconds(5)));
        Thread.Sleep(100); // let the continuation run

        Assert.Empty(harness.Ui);
        Assert.Equal(0, applied);
    }

    [Fact]
    public void DebouncedSchedule_StartsTheJobOnlyWhenItsTimerFires()
    {
        var harness = new Harness();
        var applied = new List<string>();

        harness.Scheduler.Schedule(TimeSpan.FromMilliseconds(150), _ => "typed", applied.Add);

        var timer = Assert.Single(harness.Timers);
        Assert.Equal(TimeSpan.FromMilliseconds(150), timer.Interval);
        Thread.Sleep(50);
        Assert.Empty(harness.Ui); // nothing has run yet

        timer.Tick();
        harness.PumpUi(1);

        Assert.Equal(new[] { "typed" }, applied);
    }

    [Fact]
    public void DebouncedSchedule_IsDisposedAndNeverRuns_WhenSupersededByRunNow()
    {
        var harness = new Harness();
        var applied = new List<string>();

        harness.Scheduler.Schedule(TimeSpan.FromMilliseconds(150), _ => "debounced", applied.Add);
        var timer = Assert.Single(harness.Timers);
        harness.Scheduler.RunNow(_ => "now", applied.Add);

        Assert.True(timer.Disposed);

        // Even if the timer callback still fires (already queued on the dispatcher), the generation check stops it.
        timer.Tick();
        Thread.Sleep(100);

        Assert.Empty(harness.Ui);
        Assert.Equal(new[] { "now" }, applied);
    }

    [Fact]
    public void NewerDebouncedSchedule_DisposesTheOlderTimer()
    {
        var harness = new Harness();

        harness.Scheduler.Schedule(TimeSpan.FromMilliseconds(150), _ => 1, _ => { });
        harness.Scheduler.Schedule(TimeSpan.FromMilliseconds(150), _ => 2, _ => { });

        Assert.Equal(2, harness.Timers.Count);
        Assert.True(harness.Timers[0].Disposed);
        Assert.False(harness.Timers[1].Disposed);
    }

    [Fact]
    public void Debounce_CoalescesCallsSharingAKey_AndRunsTheActionOnceOffTheCallingThread()
    {
        var harness = new Harness();
        int testThread = Environment.CurrentManagedThreadId;
        int runs = 0;
        int runThread = -1;

        harness.Scheduler.Debounce("k", TimeSpan.FromMilliseconds(500), () => runs += 100);
        harness.Scheduler.Debounce("k", TimeSpan.FromMilliseconds(500), () => runs += 100);
        harness.Scheduler.Debounce("k", TimeSpan.FromMilliseconds(500), () =>
        {
            runs++;
            runThread = Environment.CurrentManagedThreadId;
        });

        Assert.Equal(3, harness.Timers.Count);
        Assert.True(harness.Timers[0].Disposed);
        Assert.True(harness.Timers[1].Disposed);

        harness.Timers[2].Tick();

        Assert.True(WaitFor(() => Volatile.Read(ref runs) == 1), "the coalesced action never ran");
        Assert.NotEqual(testThread, runThread);
    }
}
