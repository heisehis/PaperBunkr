using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media.Transformation;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;
using static Paperbunkr.App.Tests.Theming.FoundationStyleTests;

namespace Paperbunkr.App.Tests.Controls;

/// <summary>The shell's screen host (docs/superpowers/specs/2026-10-20-motion-system-v2-navigation-design.md §3.1-3.2).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class RouteHostTests
{
    private sealed class ScreenA { }
    private sealed class ScreenB { }
    private sealed class ReaderLike : ITransientScreen { }

    private sealed class Cancellation : IDisposable
    {
        public bool Cancelled { get; private set; }

        public void Dispose() => Cancelled = true;
    }

    private readonly List<(Action Run, Cancellation Token)> _scheduled = new();
    private int _builtA;
    private int _builtReader;

    private void With(MotionPreferenceMode mode, Action<Window, RouteHost> body)
    {
        WithFoundation("default", () =>
        {
            var previous = MotionPolicy.Preference;
            var previousSchedule = RouteHost.ScheduleAfter;
            MotionPolicy.SetPreference(mode);
            RouteHost.ScheduleAfter = (run, _) =>
            {
                var token = new Cancellation();
                _scheduled.Add((run, token));
                return token;
            };
            try
            {
                var host = new RouteHost();
                host.DataTemplates.Add(new FuncDataTemplate<ScreenA>((_, _) => { _builtA++; return new Border(); }));
                host.DataTemplates.Add(new FuncDataTemplate<ScreenB>((_, _) => new ScrollViewer
                {
                    Height = 100,
                    Content = new Border { Height = 2000 },
                }));
                host.DataTemplates.Add(new FuncDataTemplate<ReaderLike>((_, _) => { _builtReader++; return new Border(); }));
                var window = Host(host);
                body(window, host);
                window.Close();
            }
            finally
            {
                RouteHost.ScheduleAfter = previousSchedule;
                MotionPolicy.SetPreference(previous);
            }
        });
    }

    private void ElapseAll()
    {
        foreach (var (run, token) in _scheduled.ToList())
        {
            if (!token.Cancelled)
            {
                run();
            }
        }

        _scheduled.Clear();
    }

    [Fact]
    public void AScreenViewIsBuiltOnce_AndReusedOnReturn()
    {
        With(MotionPreferenceMode.Full, (_, host) =>
        {
            var a = new ScreenA();
            host.Content = a;
            var first = host.ViewFor(a);
            host.Content = new ScreenB();
            ElapseAll();
            host.Content = a;
            ElapseAll();

            Assert.Equal(1, _builtA);
            Assert.Same(first, host.ViewFor(a));
            Assert.Single(host.Children);
        });
    }

    [Fact]
    public void ATransientScreen_IsRebuiltOnEveryVisit()
    {
        With(MotionPreferenceMode.Full, (_, host) =>
        {
            var reader = new ReaderLike();
            host.Content = reader;
            host.Content = new ScreenA();
            ElapseAll();
            host.Content = reader;
            ElapseAll();

            Assert.Equal(2, _builtReader);
        });
    }

    [Fact]
    public void TheLeavingScreen_StopsTakingInputAtOnce_AndIsRemovedAfterItsExit()
    {
        With(MotionPreferenceMode.Full, (_, host) =>
        {
            host.Content = new ScreenA();
            var first = host.CurrentLayer!;
            host.Intent = RouteIntent.Lateral;
            host.Content = new ScreenB();

            Assert.Equal(2, host.Children.Count);                                       // crossfading
            Assert.Same(first, host.LeavingLayer);
            Assert.False(first.IsHitTestVisible);
            Assert.Equal(Avalonia.Input.KeyboardNavigationMode.None, Avalonia.Input.KeyboardNavigation.GetTabNavigation(first));

            ElapseAll();
            Assert.Single(host.Children);
            Assert.Null(host.LeavingLayer);
            Assert.True(first.IsHitTestVisible);                                        // reset for its next visit
        });
    }

    [Fact]
    public void RapidNavigation_NeverStacksMoreThanTwoScreens_OrQueues()
    {
        With(MotionPreferenceMode.Full, (_, host) =>
        {
            var a = new ScreenA();
            var b = new ScreenB();
            host.Content = a;
            host.Content = b;
            host.Content = a;                                                            // back before the first transition ended
            host.Content = b;

            Assert.True(host.Children.Count <= 2);
            Assert.Same(host.ViewFor(b)!.Parent, host.CurrentLayer);
            Assert.All(host.Children, c => Assert.Same(host, c.Parent));
            ElapseAll();
            Assert.Single(host.Children);
            Assert.Equal(1, _builtA);
        });
    }

    [Fact]
    public void MotionOff_SwapsAtOnce_AndRemovesTheOldScreenOnTheNextTick()
    {
        With(MotionPreferenceMode.Off, (_, host) =>
        {
            host.Content = new ScreenA();
            var first = host.CurrentLayer!;
            host.Content = new ScreenB();

            Assert.False(first.IsVisible);                                              // hidden now - never detached inside the click
            Assert.Equal(1.0, host.CurrentLayer!.Opacity);
            ElapseAll();
            Assert.Single(host.Children);
        });
    }

    [Theory]
    [InlineData(RouteIntent.Lateral, "translateY(12px)")]
    [InlineData(RouteIntent.DrillIn, "scale(0.985)")]
    public void TheIntentPicksWhereTheNewScreenSettlesFrom(RouteIntent intent, string expected) =>
        Assert.Equal(TransformOperations.Parse(expected).Value, ((TransformOperations)RouteHost.StartTransform(intent, canMove: true)!).Value);

    [Theory]
    [InlineData(RouteIntent.Back)]
    [InlineData(RouteIntent.Reader)]
    [InlineData(RouteIntent.Neutral)]
    public void BackReaderAndNeutral_JustFadeTheNewScreenIn(RouteIntent intent) =>
        Assert.Null(RouteHost.StartTransform(intent, canMove: true));

    [Fact]
    public void Reduced_NeverMovesAScreen() =>
        Assert.Null(RouteHost.StartTransform(RouteIntent.Lateral, canMove: false));

    [Theory]
    [InlineData(RouteIntent.DrillIn, true)]
    [InlineData(RouteIntent.Back, false)]
    [InlineData(RouteIntent.Lateral, false)]
    public void AKeptView_StartsAtTheTopOnADrillIn_ButKeepsItsPlaceOnBackOrARailSwitch(RouteIntent intent, bool resetsToTop)
    {
        With(MotionPreferenceMode.Full, (window, host) =>
        {
            var a = new ScreenA();
            var b = new ScreenB();
            host.Content = b;
            RunLayout(window);
            var viewer = (ScrollViewer)host.ViewFor(b)!;
            viewer.Offset = new Avalonia.Vector(0, 500);
            host.Content = a;
            ElapseAll();

            host.Intent = intent;
            host.Content = b;
            RunLayout(window);

            Assert.Equal(resetsToTop ? 0 : 500, viewer.Offset.Y);
        });
    }
}
