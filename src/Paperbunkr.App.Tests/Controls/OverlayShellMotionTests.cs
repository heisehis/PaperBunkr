using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;
using static Paperbunkr.App.Tests.Theming.FoundationStyleTests;

namespace Paperbunkr.App.Tests.Controls;

/// <summary>
/// The <see cref="OverlayShell"/> open/close lifecycle (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md
/// §5.1), hosted in a real window with the real styles: closing never waits, the interactive state ends at once, the shell collapses only
/// after its exit, a reopen mid-exit cancels the collapse, and Motion Off or a detached shell collapse synchronously.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class OverlayShellMotionTests
{
    /// <summary>Exit collapses the test fires by hand (the headless dispatcher never fires timers): delay and callback, in order.</summary>
    private static readonly List<(TimeSpan Delay, Action Run, Cancellation Token)> Scheduled = new();

    private sealed class Cancellation : IDisposable
    {
        public bool Cancelled { get; private set; }

        public void Dispose() => Cancelled = true;
    }

    private static void WithMotion(MotionPreferenceMode mode, Action body)
    {
        var previous = MotionPolicy.Preference;
        var previousSchedule = OverlayShell.ScheduleAfter;
        MotionPolicy.SetPreference(mode);
        Scheduled.Clear();
        OverlayShell.ScheduleAfter = (run, delay) =>
        {
            var token = new Cancellation();
            Scheduled.Add((delay, run, token));
            return token;
        };
        try
        {
            body();
        }
        finally
        {
            OverlayShell.ScheduleAfter = previousSchedule;
            MotionPolicy.SetPreference(previous);
        }
    }

    /// <summary>Lets every pending exit elapse.</summary>
    private static void ElapseExits()
    {
        foreach (var (_, run, token) in Scheduled.ToList())
        {
            if (!token.Cancelled)
            {
                run();
            }
        }

        Scheduled.Clear();
    }

    private static (Window Window, OverlayShell Shell, Button Inside) HostShell()
    {
        var inside = new Button { Content = "OK" };
        var shell = new OverlayShell { Content = inside };
        var root = new Panel();
        root.Children.Add(new Button { Content = "Behind" });
        root.Children.Add(shell);
        return (Host(root), shell, inside);
    }

    [Fact]
    public void Open_ShowsTheShell_InTheOpenState()
    {
        WithFoundation("default", () => WithMotion(MotionPreferenceMode.Full, () =>
        {
            var (window, shell, _) = HostShell();

            shell.IsOpen = true;

            Assert.True(shell.IsVisible);
            Assert.True(shell.Classes.Contains("open"));
            Assert.True(shell.IsHitTestVisible);
            window.Close();
        }));
    }

    [Fact]
    public void Close_EndsInteractionAtOnce_ButKeepsDrawingUntilTheExitHasPlayed()
    {
        WithFoundation("default", () => WithMotion(MotionPreferenceMode.Full, () =>
        {
            var (window, shell, inside) = HostShell();
            shell.IsOpen = true;
            RunLayout(window);
            inside.Focus();
            Assert.True(inside.IsFocused);

            shell.IsOpen = false;

            Assert.True(shell.IsVisible);                                   // still drawing its exit
            Assert.True(shell.IsClosing);
            Assert.False(shell.IsHitTestVisible);                           // but no longer takes the pointer
            Assert.Equal(KeyboardNavigationMode.None, KeyboardNavigation.GetTabNavigation(shell));
            Assert.False(inside.IsFocused);                                 // nor keeps the keyboard

            Assert.Equal(TimeSpan.FromMilliseconds(120), Assert.Single(Scheduled).Delay);   // the Reveal exit
            ElapseExits();
            Assert.False(shell.IsVisible);
            Assert.False(shell.IsClosing);
            window.Close();
        }));
    }

    [Fact]
    public void ReopenMidExit_CancelsTheCollapse()
    {
        WithFoundation("default", () => WithMotion(MotionPreferenceMode.Full, () =>
        {
            var (window, shell, _) = HostShell();
            shell.IsOpen = true;
            RunLayout(window);

            shell.IsOpen = false;
            shell.IsOpen = true;
            ElapseExits();                                                  // the cancelled exit must not collapse the reopened shell

            Assert.True(shell.IsVisible);
            Assert.True(shell.Classes.Contains("open"));
            Assert.False(shell.IsClosing);
            Assert.True(shell.IsHitTestVisible);
            window.Close();
        }));
    }

    [Fact]
    public void MotionOff_CollapsesSynchronously()
    {
        WithFoundation("default", () => WithMotion(MotionPreferenceMode.Off, () =>
        {
            var (window, shell, _) = HostShell();
            shell.IsOpen = true;
            RunLayout(window);

            shell.IsOpen = false;

            Assert.False(shell.IsVisible);
            Assert.False(shell.IsClosing);
            window.Close();
        }));
    }

    [Fact]
    public void DetachedMidExit_FinishesTheClose()
    {
        WithFoundation("default", () => WithMotion(MotionPreferenceMode.Full, () =>
        {
            var (window, shell, _) = HostShell();
            shell.IsOpen = true;
            RunLayout(window);

            shell.IsOpen = false;
            Assert.True(shell.IsClosing);
            ((Panel)shell.Parent!).Children.Remove(shell);

            Assert.False(shell.IsVisible);
            Assert.False(shell.IsClosing);
            window.Close();
        }));
    }

    [Fact]
    public void UnhostedShell_CollapsesSynchronously()
    {
        WithMotion(MotionPreferenceMode.Full, () =>
        {
            var shell = new OverlayShell { IsOpen = true };

            shell.IsOpen = false;

            Assert.False(shell.IsVisible);
        });
    }
}
