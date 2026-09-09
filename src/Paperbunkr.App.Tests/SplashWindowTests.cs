using Avalonia;
using Avalonia.Controls;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="SplashWindow"/> construction + show. Regression guard for the crash an earlier draft
/// hit ("No animator registered for the property RenderTransform") - keyframe <c>Animation</c> on
/// <c>RenderTransform</c> throws at <c>EndInit</c> in this Avalonia build, so the splash motion
/// must stay on the <c>TransformOperationsTransition</c> + class-toggle pattern. The actual eased
/// motion / breathing timer is manual/on-screen only, same carve-out the rest of this project's
/// headless suite uses for dispatcher-deferred motion.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SplashWindowTests
{
    [Fact]
    public void Constructs_AndShows_WithoutThrowing_MotionOn()
    {
        var window = new SplashWindow(reducedMotion: false) { DataContext = new SplashViewModel() };

        window.Show();

        Assert.NotNull(window.FindControl<Image>("LogoImage"));
        window.Close();
    }

    [Fact]
    public void ReducedMotion_StripsAnimateClass_AndShowsEmblem()
    {
        var window = new SplashWindow(reducedMotion: true) { DataContext = new SplashViewModel() };
        window.Show();

        var logo = window.FindControl<Image>("LogoImage")!;
        Assert.DoesNotContain("animate", logo.Classes);
        Assert.Equal(1d, logo.Opacity);

        window.Close();
    }

    [Fact]
    public void MotionOn_KeepsAnimateClass()
    {
        var window = new SplashWindow(reducedMotion: false) { DataContext = new SplashViewModel() };

        var logo = window.FindControl<Image>("LogoImage")!;
        Assert.Contains("animate", logo.Classes);

        window.Close();
    }
}
