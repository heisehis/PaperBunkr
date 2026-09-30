using Avalonia.Controls;
using Paperbunkr.App.Views;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Semantics of <see cref="FocusReclaimer"/>'s three reclaim variants (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class FocusReclaimerTests
{
    private sealed class Rig
    {
        public Window Window = null!;
        public StackPanel Region = null!;
        public Button First = null!;
        public Button Second = null!;
        public Button Outside = null!;
        public int FallbackCalls;
        public FocusReclaimer Reclaimer = null!;
    }

    private static Rig Build()
    {
        var rig = new Rig
        {
            First = new Button { Content = "one" },
            Second = new Button { Content = "two" },
            Outside = new Button { Content = "outside" },
        };
        rig.Region = new StackPanel { Children = { rig.First, rig.Second } };
        rig.Window = new Window { Content = new StackPanel { Children = { rig.Outside, rig.Region } }, Width = 400, Height = 300 };
        rig.Reclaimer = new FocusReclaimer(rig.Region, () => true, () =>
        {
            rig.FallbackCalls++;
            rig.First.Focus();
        });
        rig.Window.Show();
        RunLayout(rig.Window);
        return rig;
    }

    [Fact]
    public void Reclaim_FocusesTheFallback_WhenNothingInsideHoldsFocus()
    {
        WithThemeAndTokens(() =>
        {
            var rig = Build();
            rig.Outside.Focus();

            rig.Reclaimer.Reclaim();
            RunLayout(rig.Window);

            Assert.Equal(1, rig.FallbackCalls);
            Assert.Same(rig.First, Focused(rig.Window));
            rig.Window.Close();
        });
    }

    [Fact]
    public void Reclaim_LeavesFocusAlone_WhenAnElementInsideHoldsIt()
    {
        WithThemeAndTokens(() =>
        {
            var rig = Build();
            rig.Second.Focus();

            rig.Reclaimer.Reclaim();
            RunLayout(rig.Window);

            Assert.Equal(0, rig.FallbackCalls);
            Assert.Same(rig.Second, Focused(rig.Window));
            rig.Window.Close();
        });
    }

    [Fact]
    public void Reclaim_TreatsAFocusedButNowHiddenElementAsLost()
    {
        WithThemeAndTokens(() =>
        {
            var rig = Build();
            rig.Second.Focus();
            rig.Second.IsVisible = false;

            rig.Reclaimer.Reclaim();
            RunLayout(rig.Window);

            Assert.Equal(1, rig.FallbackCalls);
            Assert.Same(rig.First, Focused(rig.Window));
            rig.Window.Close();
        });
    }

    [Fact]
    public void ReclaimIfFocusWithinOrNowhere_DoesNotStealFromASiblingRegion()
    {
        WithThemeAndTokens(() =>
        {
            var rig = Build();
            rig.Outside.Focus();

            rig.Reclaimer.ReclaimIfFocusWithinOrNowhere();
            RunLayout(rig.Window);

            Assert.Equal(0, rig.FallbackCalls);
            Assert.Same(rig.Outside, Focused(rig.Window));
            rig.Window.Close();
        });
    }

    [Fact]
    public void ReclaimIfFocusWithinOrNowhere_ReclaimsWhenFocusWasInsideAndIsThenHidden()
    {
        WithThemeAndTokens(() =>
        {
            var rig = Build();
            rig.Second.Focus();

            rig.Reclaimer.ReclaimIfFocusWithinOrNowhere();
            rig.Second.IsVisible = false;
            RunLayout(rig.Window);

            Assert.Equal(1, rig.FallbackCalls);
            Assert.Same(rig.First, Focused(rig.Window));
            rig.Window.Close();
        });
    }

    [Fact]
    public void ReclaimIfFocusLost_NoOpsWhileFocusIsLiveAnywhere_AndFiresOnceItIsGone()
    {
        WithThemeAndTokens(() =>
        {
            var rig = Build();
            rig.Outside.Focus();

            rig.Reclaimer.ReclaimIfFocusLost();
            RunLayout(rig.Window);
            Assert.Equal(0, rig.FallbackCalls);
            Assert.Same(rig.Outside, Focused(rig.Window));

            rig.Outside.IsVisible = false;
            rig.Reclaimer.ReclaimIfFocusLost();
            RunLayout(rig.Window);
            Assert.Equal(1, rig.FallbackCalls);
            Assert.Same(rig.First, Focused(rig.Window));
            rig.Window.Close();
        });
    }

    [Fact]
    public void FocusFirstButton_PrefersTheMatchingButton_AndSkipsDisabledAndHiddenOnes()
    {
        WithThemeAndTokens(() =>
        {
            var rig = Build();
            rig.First.IsEnabled = false;
            RunLayout(rig.Window);

            Assert.True(FocusReclaimer.FocusFirstButton(rig.Region));
            Assert.Same(rig.Second, Focused(rig.Window));

            rig.Second.IsVisible = false;
            rig.First.IsEnabled = true;
            rig.Outside.Classes.Add("wanted");
            RunLayout(rig.Window);
            Assert.True(FocusReclaimer.FocusFirstButton(rig.Window, b => b.Classes.Contains("wanted")));
            Assert.Same(rig.Outside, Focused(rig.Window));
            rig.Window.Close();
        });
    }
}
