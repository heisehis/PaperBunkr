using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The three effects the 2026-09-28 Home pitch added (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md C9, "Scroll-reveal",
/// "Hero tilt"): each works normally and is fully at rest under Reduced Motion. Reduced Motion is simulated the way
/// <c>ThemeService.ApplyReducedMotion</c> does it - a zero <c>PbMotionStandard</c> resource.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class HomeEffectControlsTests
{
    private static IDisposable ReducedMotion()
    {
        var resources = Application.Current!.Resources;
        bool had = resources.TryGetValue("PbMotionStandard", out var previous);
        resources["PbMotionStandard"] = TimeSpan.Zero;
        return new Restore(() =>
        {
            if (had)
            {
                resources["PbMotionStandard"] = previous;
            }
            else
            {
                resources.Remove("PbMotionStandard");
            }
        });
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    private static ParallaxBackdrop ArrangedBackdrop()
    {
        var backdrop = new ParallaxBackdrop { Source = new RenderTargetBitmap(new PixelSize(40, 40)) };
        backdrop.Measure(new Size(300, 400));
        backdrop.Arrange(new Rect(0, 0, 300, 400));
        return backdrop;
    }

    [Fact]
    public void Parallax_DriftsWithScroll_ClampedToItsOverscan()
    {
        var backdrop = ArrangedBackdrop();

        backdrop.ApplyScroll(100);
        Assert.Equal(35, backdrop.Offset, 1);

        backdrop.ApplyScroll(1500);
        Assert.Equal(50, backdrop.Offset, 1); // 12.5% of the 400px frame - never past the spare image

        backdrop.ApplyScroll(-40);
        Assert.Equal(0, backdrop.Offset);
    }

    [Fact]
    public void Parallax_UnderReducedMotion_NeverMoves()
    {
        var backdrop = ArrangedBackdrop();
        using (ReducedMotion())
        {
            backdrop.ApplyScroll(100);
            Assert.Equal(0, backdrop.Offset);
        }
    }

    [Fact]
    public void ScrollReveal_HidesUntilSeen_ButNothingUnderReducedMotion()
    {
        var hidden = new Border();
        ScrollReveal.SetEnabled(hidden, true);
        Assert.Equal(0, hidden.Opacity);
        Assert.False(ScrollReveal.IsRevealed(hidden));

        using (ReducedMotion())
        {
            var calm = new Border();
            ScrollReveal.SetEnabled(calm, true);
            Assert.Equal(1, calm.Opacity);
            Assert.Null(calm.RenderTransform);
            Assert.True(ScrollReveal.IsRevealed(calm));
        }
    }

    [Fact]
    public void ScrollReveal_AlreadyOnScreen_RevealsAtOnce()
    {
        var section = new Border { Height = 100 };
        ScrollReveal.SetEnabled(section, true);
        var window = new Window { Width = 300, Height = 400, Content = new ScrollViewer { Content = new StackPanel { Children = { section } } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(ScrollReveal.IsRevealed(section));
            Assert.Equal(1, section.Opacity);
        }
        finally
        {
            window.Close();
        }
    }

    // --- Spotlight accordion (docs/superpowers/specs/2026-09-29-home-spotlight-accordion-design.md) ---

    [Fact]
    public void AccordionLayout_OpenGetsItsWidth_RestShare_AndItAllAddsUp()
    {
        var widths = AccordionLayout.Compute(8, openIndex: 2, peekIndex: -1, width: 1100, openWidth: 400, minWidth: 44, spacing: 8, peekWeight: 1.6);

        Assert.Equal(400, widths[2]);
        Assert.Equal(1100, widths.Sum() + (7 * 8), 3);
        Assert.All(widths.Where((_, i) => i != 2), w => Assert.Equal(widths[0], w, 3));
    }

    [Fact]
    public void AccordionLayout_PeekedSliverIsWider()
    {
        var widths = AccordionLayout.Compute(8, openIndex: 0, peekIndex: 5, width: 1100, openWidth: 400, minWidth: 44, spacing: 8, peekWeight: 1.6);

        Assert.True(widths[5] > widths[4] * 1.5);
        Assert.Equal(1100, widths.Sum() + (7 * 8), 3);
    }

    [Fact]
    public void AccordionLayout_TooNarrow_DropsFromTheEnd_KeepingThreeSlivers()
    {
        var narrow = AccordionLayout.Compute(8, openIndex: 0, peekIndex: -1, width: 700, openWidth: 400, minWidth: 44, spacing: 8, peekWeight: 1.6);
        int shown = narrow.Count(w => w > 0);
        Assert.True(shown < 8);
        Assert.Equal(0, narrow[7]);
        Assert.All(narrow.Where(w => w > 0).Skip(1), w => Assert.True(w >= 44 - 0.001));

        var tiny = AccordionLayout.Compute(8, openIndex: 0, peekIndex: -1, width: 380, openWidth: 400, minWidth: 44, spacing: 8, peekWeight: 1.6);
        Assert.Equal(4, tiny.Count(w => w > 0));           // the open panel plus three slivers, however narrow
        Assert.True(tiny[0] < 400);                         // the open panel gives way instead
        Assert.Equal(380, tiny.Sum() + (3 * 8), 3);
    }

    [Fact]
    public void AccordionLayout_EdgeCounts()
    {
        Assert.Empty(AccordionLayout.Compute(0, 0, -1, 500, 400, 44, 8, 1.6));
        Assert.Equal(new[] { 500d }, AccordionLayout.Compute(1, 0, -1, 500, 400, 44, 8, 1.6));
    }

    [Fact]
    public void AccordionPanel_UnderReducedMotion_JumpsStraightToTheNewLayout()
    {
        var panel = new AccordionPanel { OpenWidth = 400, MinItemWidth = 44, Spacing = 8 };
        for (int i = 0; i < 5; i++)
        {
            panel.Children.Add(new Border());
        }

        panel.Measure(new Size(800, 300));
        panel.Arrange(new Rect(0, 0, 800, 300));
        Assert.Equal(400, panel.CurrentWidths[0], 3);

        using (ReducedMotion())
        {
            panel.OpenIndex = 3;
            panel.Measure(new Size(800, 300));
            panel.Arrange(new Rect(0, 0, 800, 300));
            Assert.Equal(400, panel.CurrentWidths[3], 3);
            Assert.Equal(400, panel.Children[3].Bounds.Width, 3);
        }
    }

    [Fact]
    public void AccordionPanel_KeyboardFocusOnAClosedPanel_ActivatesIt()
    {
        object? activated = null;
        var panel = new AccordionPanel { ActivateCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(o => activated = o) };
        for (int i = 0; i < 3; i++)
        {
            panel.Children.Add(new Button { DataContext = $"pick {i}" });
        }

        var window = new Window { Width = 900, Height = 320, Content = panel };
        try
        {
            window.Show();
            window.UpdateLayout();

            panel.Children[2].Focus(Avalonia.Input.NavigationMethod.Tab);

            Assert.Equal("pick 2", activated);
        }
        finally
        {
            window.Close();
        }
    }
}
