using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="SplashWindow"/> construction + show. Regression guard for the crash an earlier draft
/// hit ("No animator registered for the property RenderTransform") - a keyframe <c>Animation</c>
/// that sets <c>RenderTransform</c> directly threw at <c>EndInit</c>. The motion is now entirely
/// code-driven (<c>Animation.RunAsync</c> from <c>Opened</c>) against a <see cref="ScaleTransform"/>
/// the code-behind installs; whether it visibly renders is manual/on-screen only.
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
    public void InstallsScaleTransformOnEmblem()
    {
        var window = new SplashWindow(reducedMotion: false) { DataContext = new SplashViewModel() };

        var logo = window.FindControl<Image>("LogoImage")!;
        Assert.IsType<ScaleTransform>(logo.RenderTransform);

        window.Close();
    }

    [Fact]
    public void ReducedMotion_SnapsEmblemVisible()
    {
        var window = new SplashWindow(reducedMotion: true) { DataContext = new SplashViewModel() };
        window.Show();

        var logo = window.FindControl<Image>("LogoImage")!;
        Assert.Equal(1d, logo.Opacity);

        window.Close();
    }
}
