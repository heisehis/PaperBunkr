using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;
using static Paperbunkr.App.Tests.Theming.FoundationStyleTests;

namespace Paperbunkr.App.Tests.Controls;

/// <summary>Motion v2 on-page pieces (docs/superpowers/specs/2026-10-20-motion-system-v2-on-page-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class OnPageMotionTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 20)]
    [InlineData(4, 80)]
    [InlineData(5, 100)]
    [InlineData(40, 100)]                                                          // the tail shares the last group: never a growing stagger
    public void TheEntranceStagger_IsCappedAtSixGroups(int index, int expectedMs) =>
        Assert.Equal(expectedMs, EntranceAnimation.ComputeDelayMs(index, reducedMotion: false));

    [Fact]
    public void Reduced_HasNoStagger() => Assert.Equal(0, EntranceAnimation.ComputeDelayMs(4, reducedMotion: true));

    [Fact]
    public void EveryEntranceIsOneShot_ByDefault() => Assert.True(EntranceAnimation.GetOneShot(new ItemsControl()));

    [Theory]
    [InlineData(0, 40, "translate(0px,0px) scale(40,1)")]
    [InlineData(64.256, 51.5, "translate(64.26px,0px) scale(51.5,1)")]
    public void TheTabBar_IsPlacedByTranslateAndWidthScale(double x, double width, string expected) =>
        Assert.Equal(expected, TabIndicator.IndicatorTransform(x, width));

    [Fact]
    public void TheTabBar_FollowsTheActiveTab_AndHidesTheTabsOwnUnderline()
    {
        WithFoundation("default", () =>
        {
            TabIndicator.Install();
            var first = new Button { Classes = { "tab", "active" }, Content = "Today", Width = 60 };
            var second = new Button { Classes = { "tab" }, Content = "Trends", Width = 70 };
            var strip = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 20, Children = { first, second } };
            var window = Host(strip);
            RunLayout(window);

            Assert.Contains(TabIndicator.HostClass, strip.Classes);
            var adorner = Assert.IsType<Panel>(Avalonia.Controls.Primitives.AdornerLayer.GetAdorner(strip));
            var bar = Assert.IsType<Border>(Assert.Single(adorner.Children));
            Assert.Equal(TransformOperations.Parse("translate(0px,0px) scale(60,1)").Value, ((TransformOperations)bar.RenderTransform!).Value);

            first.Classes.Remove("active");
            second.Classes.Add("active");
            RunLayout(window);
            // Mid-slide the animated value is an interpolated matrix (the switch animates); the target is the base value.
            var target = (TransformOperations)bar.GetBaseValue(Avalonia.Visual.RenderTransformProperty).Value!;
            Assert.Equal(TransformOperations.Parse("translate(80px,0px) scale(70,1)").Value, target.Value);
            Assert.NotNull(bar.Transitions);
            window.Close();
        });
    }

    [Fact]
    public void Replace_DoesNotFadeTheFirstValue_OrAHidingPanel_ButFadesAShownOne()
    {
        WithFoundation("default", () =>
        {
            var previous = MotionPolicy.Preference;
            MotionPolicy.SetPreference(MotionPreferenceMode.Full);
            try
            {
                var panel = new Border { Width = 40, Height = 40 };
                var window = Host(panel);

                Replace.SetOn(panel, false);                                          // first value: the screen arriving
                Assert.Equal(1, panel.Opacity);
                Replace.SetOn(panel, true);                                           // its tab becomes the shown one
                TestDispatcher.Drain();
                Assert.True(panel.Opacity < 1, "a newly shown panel starts its fade from transparent");
                window.Close();
            }
            finally
            {
                MotionPolicy.SetPreference(previous);
            }
        });
    }
}
