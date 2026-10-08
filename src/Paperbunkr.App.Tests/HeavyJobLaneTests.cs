using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="HeavyJobLane"/>: one heavy background job at a time, user-started ahead of scheduled
/// (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md section 4.3).
/// </summary>
public class HeavyJobLaneTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>A job that holds the lane until the test lets it go.</summary>
    private sealed class Held
    {
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Run { get; }

        public Task Started => _started.Task;

        public Held(HeavyJobLane lane, bool userStarted, List<string>? order = null, string? name = null, CancellationToken token = default)
        {
            Run = lane.RunAsync(userStarted, token, async () =>
            {
                order?.Add(name!);
                _started.SetResult(true);
                await _release.Task;
            });
        }

        public async Task FinishAsync()
        {
            _release.SetResult(true);
            await Run.WaitAsync(Timeout);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not reached in time");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task TheFirstJobRunsAtOnce_AndASecondWaitsForIt()
    {
        var lane = new HeavyJobLane();
        var first = new Held(lane, userStarted: true);
        await first.Started.WaitAsync(Timeout);
        Assert.True(lane.IsBusy);

        var second = new Held(lane, userStarted: true);
        await WaitUntil(() => lane.WaitingCount == 1);
        Assert.False(second.Started.IsCompleted);

        await first.FinishAsync();
        await second.Started.WaitAsync(Timeout);
        await second.FinishAsync();

        Assert.False(lane.IsBusy);
        Assert.Equal(0, lane.WaitingCount);
    }

    [Fact]
    public async Task AUserStartedJob_GoesAheadOfScheduledOnesThatWereWaitingFirst()
    {
        var lane = new HeavyJobLane();
        var order = new List<string>();
        var running = new Held(lane, userStarted: false, order, "running");
        await running.Started.WaitAsync(Timeout);

        var scheduledA = new Held(lane, userStarted: false, order, "scheduled-a");
        await WaitUntil(() => lane.WaitingCount == 1);
        var scheduledB = new Held(lane, userStarted: false, order, "scheduled-b");
        await WaitUntil(() => lane.WaitingCount == 2);
        var user = new Held(lane, userStarted: true, order, "user");
        await WaitUntil(() => lane.WaitingCount == 3);

        await running.FinishAsync();
        await user.Started.WaitAsync(Timeout);
        await user.FinishAsync();
        await scheduledA.Started.WaitAsync(Timeout);
        await scheduledA.FinishAsync();
        await scheduledB.Started.WaitAsync(Timeout);
        await scheduledB.FinishAsync();

        Assert.Equal(new[] { "running", "user", "scheduled-a", "scheduled-b" }, order);
    }

    [Fact]
    public async Task WorkAlreadyInsideTheLane_DoesNotWaitOnItself()
    {
        var lane = new HeavyJobLane();
        bool innerRan = false;

        await lane.RunAsync(userStarted: false, CancellationToken.None, async () =>
        {
            Assert.False(lane.WouldWait);
            await Task.Yield();

            // The scheduler holds the lane and calls the scrape coordinator, which asks for it again.
            await lane.RunAsync(userStarted: true, CancellationToken.None, () =>
            {
                innerRan = true;
                return Task.CompletedTask;
            });

            Assert.True(lane.IsBusy); // the nested run must not have released the outer slot
        }).WaitAsync(Timeout);

        Assert.True(innerRan);
        Assert.False(lane.IsBusy);
    }

    [Fact]
    public async Task BeingInsideTheLane_DoesNotLeakToUnrelatedCallers()
    {
        var lane = new HeavyJobLane();
        var first = new Held(lane, userStarted: true);
        await first.Started.WaitAsync(Timeout);

        // This test method never entered the lane itself, so from here a new job would wait.
        Assert.True(lane.WouldWait);

        await first.FinishAsync();
        Assert.False(lane.WouldWait);
    }

    [Fact]
    public async Task CancellingAWaitingJob_TakesItOutOfTheQueue_AndTheLaneCarriesOn()
    {
        var lane = new HeavyJobLane();
        var first = new Held(lane, userStarted: true);
        await first.Started.WaitAsync(Timeout);

        using var cts = new CancellationTokenSource();
        bool ran = false;
        var cancelled = lane.RunAsync(userStarted: true, cts.Token, () =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        await WaitUntil(() => lane.WaitingCount == 1);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(Timeout));
        Assert.False(ran);
        Assert.Equal(0, lane.WaitingCount);

        var third = new Held(lane, userStarted: false);
        await WaitUntil(() => lane.WaitingCount == 1);
        await first.FinishAsync();
        await third.Started.WaitAsync(Timeout);
        await third.FinishAsync();
        Assert.False(lane.IsBusy);
    }

    [Fact]
    public async Task AJobThatThrows_StillHandsTheLaneOn()
    {
        var lane = new HeavyJobLane();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            lane.RunAsync(userStarted: true, CancellationToken.None, () => throw new InvalidOperationException("boom")));

        Assert.False(lane.IsBusy);
        int result = await lane.RunAsync(userStarted: true, CancellationToken.None, () => Task.FromResult(7)).WaitAsync(Timeout);
        Assert.Equal(7, result);
    }

    [Fact]
    public async Task TheSlotForm_HoldsTheLaneUntilDisposed_AndMarksTheCallerInside()
    {
        var lane = new HeavyJobLane();

        using (HeavyJobLane.Activate(await lane.EnterAsync(userStarted: true, CancellationToken.None)))
        {
            Assert.True(lane.IsBusy);
            Assert.False(lane.WouldWait); // this flow holds it

            using var nested = HeavyJobLane.Activate(await lane.EnterAsync(userStarted: true, CancellationToken.None));
            Assert.True(lane.IsBusy);
        }

        Assert.False(lane.IsBusy);
    }

    [Fact]
    public async Task APassThroughLane_NeverMakesAnyoneWait()
    {
        var lane = new HeavyJobLane(serialize: false);
        var first = new Held(lane, userStarted: true);
        var second = new Held(lane, userStarted: false);

        await first.Started.WaitAsync(Timeout);
        await second.Started.WaitAsync(Timeout);
        Assert.False(lane.IsBusy);
        Assert.False(lane.WouldWait);

        await first.FinishAsync();
        await second.FinishAsync();
    }

    [Fact]
    public void TheSharedLane_IsAPassThroughInTests_SoUnrelatedTestsAreNotSerialized()
    {
        Assert.False(HeavyJobLane.Shared.WouldWait);
    }
}
