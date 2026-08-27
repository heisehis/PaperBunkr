using System;
using Paperbunkr.App.Services;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Drives <see cref="FreezeWatchdogService.Tick"/> directly with a fake clock/ping/onFrozen -
/// docs/superpowers/specs/2026-08-23-app-chrome-crash-reporter-and-tray-design.md §3 calls for
/// exactly this ("an injectable clock/dispatcher stand-in so the test suite doesn't actually wait 10
/// real seconds"). <see cref="Start"/>/the real background thread are intentionally not exercised
/// here, matching this codebase's "thin native-wrapping service, no direct unit tests for the real
/// thread/dispatcher plumbing" precedent.
/// </summary>
public class FreezeWatchdogServiceTests
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FreezeThreshold = TimeSpan.FromSeconds(10);

    [Fact]
    public void Tick_WhenUiThreadResponds_NeverFires()
    {
        int firedCount = 0;
        var now = new DateTime(2026, 1, 1);
        var watchdog = new FreezeWatchdogService(
            pingUiThread: _ => true,
            onFrozen: () => firedCount++,
            clock: () => now,
            pollInterval: PollInterval,
            freezeThreshold: FreezeThreshold);

        for (int i = 0; i < 50; i++)
        {
            now = now.AddSeconds(1);
            watchdog.Tick();
        }

        Assert.Equal(0, firedCount);
    }

    [Fact]
    public void Tick_WhenUnresponsiveUnderThreshold_DoesNotFire()
    {
        int firedCount = 0;
        var now = new DateTime(2026, 1, 1);
        var watchdog = new FreezeWatchdogService(
            pingUiThread: _ => false,
            onFrozen: () => firedCount++,
            clock: () => now,
            pollInterval: PollInterval,
            freezeThreshold: FreezeThreshold);

        for (int i = 0; i < 9; i++)
        {
            now = now.AddSeconds(1);
            watchdog.Tick();
        }

        Assert.Equal(0, firedCount);
    }

    [Fact]
    public void Tick_WhenUnresponsiveBeyondThreshold_Fires()
    {
        int firedCount = 0;
        var now = new DateTime(2026, 1, 1);
        var watchdog = new FreezeWatchdogService(
            pingUiThread: _ => false,
            onFrozen: () => firedCount++,
            clock: () => now,
            pollInterval: PollInterval,
            freezeThreshold: FreezeThreshold);

        for (int i = 0; i < 11; i++)
        {
            now = now.AddSeconds(1);
            watchdog.Tick();
        }

        Assert.Equal(1, firedCount);
    }

    /// <summary>
    /// Mirrors CE's own <c>CrashWatchDog.LockWatcher</c>, which resets <c>LastTimeRunning</c> right
    /// before firing - a still-stuck UI thread waits a full threshold before the notification fires
    /// again, instead of re-firing on every single poll while frozen.
    /// </summary>
    [Fact]
    public void Tick_AfterFiring_WaitsAFullThresholdBeforeRefiring()
    {
        int firedCount = 0;
        var now = new DateTime(2026, 1, 1);
        var watchdog = new FreezeWatchdogService(
            pingUiThread: _ => false,
            onFrozen: () => firedCount++,
            clock: () => now,
            pollInterval: PollInterval,
            freezeThreshold: FreezeThreshold);

        for (int i = 0; i < 11; i++)
        {
            now = now.AddSeconds(1);
            watchdog.Tick();
        }
        Assert.Equal(1, firedCount);

        // lastResponse was reset to the moment it fired (now == 11s) - it takes a further 11s
        // (not just 10) to cross the threshold again (elapsed must be strictly > 10s), same as the
        // first fire above.
        for (int i = 0; i < 10; i++)
        {
            now = now.AddSeconds(1);
            watchdog.Tick();
        }
        Assert.Equal(1, firedCount);

        now = now.AddSeconds(1);
        watchdog.Tick();
        Assert.Equal(2, firedCount);
    }

    [Fact]
    public void Tick_WhenResponseArrivesAfterBeingStuck_ResetsWithoutFiring()
    {
        int firedCount = 0;
        var now = new DateTime(2026, 1, 1);
        bool respond = false;
        var watchdog = new FreezeWatchdogService(
            pingUiThread: _ => respond,
            onFrozen: () => firedCount++,
            clock: () => now,
            pollInterval: PollInterval,
            freezeThreshold: FreezeThreshold);

        for (int i = 0; i < 8; i++)
        {
            now = now.AddSeconds(1);
            watchdog.Tick();
        }

        respond = true;
        now = now.AddSeconds(1);
        watchdog.Tick();

        respond = false;
        for (int i = 0; i < 9; i++)
        {
            now = now.AddSeconds(1);
            watchdog.Tick();
        }

        Assert.Equal(0, firedCount);
    }

    [Fact]
    public void Tick_WhenPingThrows_TreatsAsUnresponsiveWithoutPropagating()
    {
        var now = new DateTime(2026, 1, 1);
        var watchdog = new FreezeWatchdogService(
            pingUiThread: _ => throw new InvalidOperationException("dispatcher unavailable"),
            onFrozen: () => { },
            clock: () => now,
            pollInterval: PollInterval,
            freezeThreshold: FreezeThreshold);

        var exception = Record.Exception(() => watchdog.Tick());

        Assert.Null(exception);
    }

    [Fact]
    public void Tick_WhenOnFrozenThrows_DoesNotPropagate()
    {
        var now = new DateTime(2026, 1, 1);
        var watchdog = new FreezeWatchdogService(
            pingUiThread: _ => false,
            onFrozen: () => throw new InvalidOperationException("message box failed"),
            clock: () => now,
            pollInterval: PollInterval,
            freezeThreshold: FreezeThreshold);

        Exception? exception = null;
        for (int i = 0; i < 11; i++)
        {
            now = now.AddSeconds(1);
            exception = Record.Exception(() => watchdog.Tick());
        }

        Assert.Null(exception);
    }
}
