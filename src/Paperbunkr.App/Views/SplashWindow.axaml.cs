using System;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// The borderless startup splash (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-
/// design.md, Decision 2). Shown first by <c>App.axaml.cs</c>; the heavy init runs on a background
/// thread reporting into the bound <see cref="SplashViewModel"/>; then <c>App.axaml.cs</c> builds
/// and shows <c>MainWindow</c> and calls <see cref="FadeOutAndCloseAsync"/>.
///
/// Reduced motion (<see cref="SkinService.GetReducedMotion"/>) is checked here rather than relying
/// on the <c>PbMotion*</c> resources, which <c>SkinService.ApplyPersistedSettings()</c> has not
/// necessarily zeroed yet at splash time.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
        : this(new SkinService().GetReducedMotion())
    {
    }

    public SplashWindow(bool reducedMotion)
    {
        InitializeComponent();

        if (!reducedMotion)
        {
            LogoImage.Classes.Add("animated");
            GlowBorder.Classes.Add("animated");
        }
    }

    /// <summary>Fades the whole window out over ~150ms, then closes it. Called after
    /// <c>MainWindow</c> is up.</summary>
    public async Task FadeOutAndCloseAsync()
    {
        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new CubicEaseIn(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0d) } },
            },
        };

        await fade.RunAsync(this);
        Close();
    }
}
