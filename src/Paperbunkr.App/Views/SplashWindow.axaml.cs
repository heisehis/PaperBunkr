using System;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// The borderless startup splash (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-
/// design.md, Decision 2). Shown first by <c>App.axaml.cs</c>; the heavy init runs on a background
/// thread reporting into the bound <see cref="ViewModels.SplashViewModel"/>; then <c>App.axaml.cs</c>
/// builds and shows <c>MainWindow</c> and calls <see cref="FadeOutAndCloseAsync"/>.
///
/// Motion is driven from here (property transitions + a ping-pong timer) rather than XAML keyframe
/// <c>Animation</c> - this Avalonia build has no animator registered for keyframe animation of
/// <c>RenderTransform</c>. Reduced motion (<see cref="SkinService.GetReducedMotion"/>) is checked
/// here rather than via the <c>PbMotion*</c> resources, which <c>SkinService.ApplyPersistedSettings()</c>
/// has not necessarily zeroed yet at splash time.
/// </summary>
public partial class SplashWindow : Window
{
    private static readonly TransformOperations ScaleRest = TransformOperations.Parse("scale(1)");
    private static readonly TransformOperations ScaleUp = TransformOperations.Parse("scale(1.035)");

    private readonly bool _reducedMotion;
    private DispatcherTimer? _breatheTimer;
    private bool _breatheUp;

    public SplashWindow()
        : this(new SkinService().GetReducedMotion())
    {
    }

    public SplashWindow(bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
        InitializeComponent();

        LogoImage.RenderTransform = ScaleRest;
        GlowBorder.Opacity = _reducedMotion ? 0.5 : 0.35;

        Loaded += OnLoaded;
        Closed += (_, _) => _breatheTimer?.Stop();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // Entrance: drop the pre-entrance ".enter" class so the transitions animate to the resting
        // Opacity 1 / scale(1). Under reduced motion the class was already visually a no-op path -
        // clearing it just snaps to the final state.
        LogoImage.Classes.Remove("enter");

        if (_reducedMotion)
        {
            return;
        }

        // Breathing: ping-pong the logo scale and the glow opacity every half-cycle; the
        // TransformOperationsTransition / DoubleTransition on each element tweens between the two.
        _breatheTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _breatheTimer.Tick += (_, _) =>
        {
            _breatheUp = !_breatheUp;
            LogoImage.RenderTransform = _breatheUp ? ScaleUp : ScaleRest;
            GlowBorder.Opacity = _breatheUp ? 0.7 : 0.35;
        };
        _breatheTimer.Start();
    }

    /// <summary>Fades the whole window out over ~150ms, then closes it. Called after
    /// <c>MainWindow</c> is up.</summary>
    public async Task FadeOutAndCloseAsync()
    {
        _breatheTimer?.Stop();

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
