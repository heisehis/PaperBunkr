using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Services;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Drives <see cref="FreezeWatchdogService.Tick"/> directly with a fake clock, ping, dialog and sinks
/// (docs/superpowers/specs/2026-10-21-freeze-watchdog-redesign-design.md, "Testing"). The real background
/// thread, the dispatcher ping and the native TaskDialog are not exercised here - same "thin native
/// wrapper" precedent as NativeMessageBox; the dialog is checked on screen with PAPERBUNKR_SIMULATE_FREEZE.
/// </summary>
public class FreezeWatchdogServiceTests
{
    private sealed class FakeDialog : IFreezeDialog
    {
        public int ShowCount { get; private set; }
        public int CloseRequests { get; private set; }
        public bool IsOpen { get; private set; }
        private Action<FreezeDialogResult>? _onClosed;
        public Func<TimeSpan>? Elapsed { get; private set; }

        public void Show(Func<TimeSpan> elapsed, Action<FreezeDialogResult> onClosed)
        {
            ShowCount++;
            IsOpen = true;
            Elapsed = elapsed;
            _onClosed = onClosed;
        }

        public void RequestClose()
        {
            CloseRequests++;
            Click(FreezeDialogResult.Recovered);
        }

        public void Click(FreezeDialogResult result)
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            _onClosed?.Invoke(result);
        }
    }

    private sealed class Harness
    {
        public DateTime Now = new(2026, 1, 1);
        public bool Respond;
        public bool DebuggerAttached;
        public readonly FakeDialog Dialog = new();
        public readonly List<string> Milestones = new();
        public int FreezeLogs;
        public int StackCaptures;
        public readonly List<TimeSpan> RecoveryAlerts = new();
        public int? ExitCode;
        public FreezeWatchdogService Watchdog = null!;

        public static Harness Create(Func<TimeSpan, bool>? ping = null)
        {
            var h = new Harness();
            h.Watchdog = new FreezeWatchdogService(
                pingUiThread: ping ?? (_ => h.Respond),
                dialog: h.Dialog,
                clock: () => h.Now,
                isDebuggerAttached: () => h.DebuggerAttached,
                logMilestone: h.Milestones.Add,
                logFreeze: () => h.FreezeLogs++,
                captureStack: () => h.StackCaptures++,
                onRecoveredAfterFreeze: h.RecoveryAlerts.Add,
                exit: code => h.ExitCode = code);
            return h;
        }

        /// <summary>Advance the clock one second per tick, the real poll rhythm.</summary>
        public void Ticks(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Now = Now.AddSeconds(1);
                Watchdog.Tick();
            }
        }

        public int StalledLogs => Milestones.Count(m => m.Contains("stalled"));
        public int RecoveredLogs => Milestones.Count(m => m.Contains("recovered after"));
    }

    [Fact]
    public void Defaults_AreTenSecondStallAndThirtySecondFreeze()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), FreezeWatchdogService.DefaultStallThreshold);
        Assert.Equal(TimeSpan.FromSeconds(30), FreezeWatchdogService.DefaultFreezeThreshold);
    }

    [Fact]
    public void Tick_WhenUiThreadResponds_NothingHappens()
    {
        var h = Harness.Create();
        h.Respond = true;

        h.Ticks(100);

        Assert.Empty(h.Milestones);
        Assert.Equal(0, h.Dialog.ShowCount);
        Assert.Equal(0, h.StackCaptures);
    }

    [Fact]
    public void Tick_StallUnderTenSeconds_LeavesNoTrace()
    {
        var h = Harness.Create();

        h.Ticks(10);
        h.Respond = true;
        h.Ticks(1);

        Assert.Empty(h.Milestones);
        Assert.Equal(0, h.StackCaptures);
    }

    [Fact]
    public void Tick_StallPastTenSeconds_LogsAndCapturesStackOnceWithoutDialog()
    {
        var h = Harness.Create();

        h.Ticks(25);

        Assert.Equal(1, h.StalledLogs);
        Assert.Equal(1, h.StackCaptures);
        Assert.Equal(0, h.FreezeLogs);
        Assert.Equal(0, h.Dialog.ShowCount);
    }

    [Fact]
    public void Tick_RecoveryAfterStallOnly_LogsDurationWithoutAlert()
    {
        var h = Harness.Create();

        h.Ticks(15);
        h.Respond = true;
        h.Ticks(1);

        Assert.Equal(1, h.RecoveredLogs);
        Assert.Contains(h.Milestones, m => m.Contains("recovered after 16 s"));
        Assert.Empty(h.RecoveryAlerts);
    }

    [Fact]
    public void Tick_FreezeAtThirtySeconds_ShowsDialogOnceAndLogsCrash()
    {
        var h = Harness.Create();

        h.Ticks(30);
        Assert.Equal(0, h.Dialog.ShowCount);

        h.Ticks(1);
        Assert.Equal(1, h.Dialog.ShowCount);
        Assert.Equal(1, h.FreezeLogs);
    }

    [Fact]
    public void Tick_WhileDialogOpen_NeverShowsASecondOne()
    {
        var h = Harness.Create();

        h.Ticks(300);

        Assert.Equal(1, h.Dialog.ShowCount);
        Assert.True(h.Dialog.IsOpen);
    }

    [Fact]
    public void Dialog_ElapsedCounter_MeasuresFromLastAnsweredPing()
    {
        var h = Harness.Create();

        h.Ticks(42);

        Assert.Equal(TimeSpan.FromSeconds(42), h.Dialog.Elapsed!());
    }

    [Fact]
    public void Tick_RecoveryWhileDialogOpen_ClosesDialogAndRaisesAlert()
    {
        var h = Harness.Create();

        h.Ticks(45);
        h.Respond = true;
        h.Ticks(1);

        Assert.Equal(1, h.Dialog.CloseRequests);
        Assert.False(h.Dialog.IsOpen);
        Assert.Equal(new[] { TimeSpan.FromSeconds(46) }, h.RecoveryAlerts);
        Assert.Equal(1, h.RecoveredLogs);
    }

    [Fact]
    public void KeepWaiting_AsksAgainOnlyAFullThirtySecondsAfterTheClick()
    {
        var h = Harness.Create();
        h.Ticks(31);
        Assert.Equal(1, h.Dialog.ShowCount);

        // The dialog sat open well past the threshold before the click - must not re-fire straight away.
        h.Ticks(60);
        h.Dialog.Click(FreezeDialogResult.KeepWaiting);

        h.Ticks(30);
        Assert.Equal(1, h.Dialog.ShowCount);

        h.Ticks(1);
        Assert.Equal(2, h.Dialog.ShowCount);
        Assert.Equal(1, h.FreezeLogs);
        Assert.Contains(h.Milestones, m => m.Contains("Keep waiting"));
    }

    [Fact]
    public void Tick_RecoveryAfterKeepWaiting_StillRaisesAlert()
    {
        var h = Harness.Create();
        h.Ticks(31);
        h.Dialog.Click(FreezeDialogResult.KeepWaiting);

        h.Ticks(5);
        h.Respond = true;
        h.Ticks(1);

        Assert.Single(h.RecoveryAlerts);
        Assert.Equal(0, h.Dialog.CloseRequests);
    }

    [Fact]
    public void Close_LogsAndExitsWithCodeOne()
    {
        var h = Harness.Create();
        h.Ticks(31);

        h.Dialog.Click(FreezeDialogResult.Close);

        Assert.Equal(1, h.ExitCode);
        Assert.Contains(h.Milestones, m => m.Contains("Close Paperbunkr"));
    }

    [Fact]
    public void Tick_WithDebuggerAttached_NeverStallsOrFreezes()
    {
        var h = Harness.Create();
        h.DebuggerAttached = true;

        h.Ticks(120);

        Assert.Empty(h.Milestones);
        Assert.Equal(0, h.Dialog.ShowCount);
    }

    [Fact]
    public void Tick_AfterMachineSleepGap_ResetsInsteadOfFiring()
    {
        var h = Harness.Create();
        h.Ticks(1);

        // The watcher itself didn't run for an hour (sleep); the first tick back may miss a ping.
        h.Now = h.Now.AddHours(1);
        h.Watchdog.Tick();
        h.Ticks(5);

        Assert.Empty(h.Milestones);
        Assert.Equal(0, h.Dialog.ShowCount);
    }

    [Fact]
    public void StackCapture_OncePerEpisode_AtMostThreePerSession()
    {
        var h = Harness.Create();

        for (int episode = 0; episode < 5; episode++)
        {
            h.Respond = false;
            h.Ticks(20);
            h.Respond = true;
            h.Ticks(1);
        }

        Assert.Equal(5, h.StalledLogs);
        Assert.Equal(FreezeWatchdogService.MaxStackCapturesPerSession, h.StackCaptures);
    }

    [Fact]
    public void Tick_WhenPingThrows_TreatsAsUnresponsiveWithoutPropagating()
    {
        var h = Harness.Create(ping: _ => throw new InvalidOperationException("dispatcher unavailable"));

        Exception? exception = null;
        for (int i = 0; i < 31; i++)
        {
            h.Now = h.Now.AddSeconds(1);
            exception ??= Record.Exception(() => h.Watchdog.Tick());
        }

        Assert.Null(exception);
        Assert.Equal(1, h.Dialog.ShowCount);
    }

    [Fact]
    public void Tick_WhenASinkThrows_DoesNotPropagate()
    {
        var now = new DateTime(2026, 1, 1);
        var watchdog = new FreezeWatchdogService(
            pingUiThread: _ => false,
            dialog: new FakeDialog(),
            clock: () => now,
            isDebuggerAttached: () => false,
            logMilestone: _ => throw new InvalidOperationException("log failed"),
            logFreeze: () => throw new InvalidOperationException("crash log failed"),
            captureStack: () => throw new InvalidOperationException("stack failed"),
            exit: _ => { });

        Exception? exception = null;
        for (int i = 0; i < 40; i++)
        {
            now = now.AddSeconds(1);
            exception ??= Record.Exception(() => watchdog.Tick());
        }

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(47, "47 s")]
    [InlineData(60, "1 min")]
    [InlineData(72, "1 min 12 s")]
    [InlineData(3605, "60 min 5 s")]
    public void FreezeDurationText_Format(int seconds, string expected)
    {
        Assert.Equal(expected, FreezeDurationText.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(42, "0:42")]
    [InlineData(725, "12:05")]
    public void FreezeDurationText_Clock(int seconds, string expected)
    {
        Assert.Equal(expected, FreezeDurationText.Clock(TimeSpan.FromSeconds(seconds)));
    }
}
