using System;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
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
/// Motion is driven from here by toggling the <c>.enter</c> / <c>.breathe</c> classes the XAML
/// <c>TransformOperationsTransition</c>s tween - keyframe <c>Animation</c> on <c>RenderTransform</c>
/// has no registered animator in this Avalonia build and crashes at construction. Reduced motion
/// (<see cref="SkinService.GetReducedMotion"/>) is checked here rather than via the
/// <c>PbMotion*</c> resources, which have not necessarily been zeroed yet at splash time.
/// </summary>
public partial class SplashWindow : Window
{
    private readonly bool _reducedMotion;
    private DispatcherTimer? _breatheTimer;

    public SplashWindow()
        : this(new SkinService().GetReducedMotion())
    {
    }

    public SplashWindow(bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
        InitializeComponent();

        if (_reducedMotion)
        {
            // Snap to the resting state - no entrance, no breathing.
            LogoImage.Classes.Remove("enter");
            GlowBorder.Opacity = 0.5;
        }

        Opened += OnOpened;
        Closed += (_, _) => _breatheTimer?.Stop();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_reducedMotion)
        {
            return;
        }

        // Entrance: drop .enter so the emblem fades + scales up from scale(0.88) to rest. A short
        // delay lets the transitions attach before the value changes (otherwise the first change
        // can apply instantly).
        DispatcherTimer.RunOnce(() => LogoImage.Classes.Remove("enter"), TimeSpan.FromMilliseconds(30));

        // Breathing: toggle .breathe every half-cycle; the transitions tween scale 1.0<->1.04 on
        // the logo and opacity 0.4<->0.75 on the glow. First toggle after the entrance settles.
        _breatheTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.3) };
        _breatheTimer.Tick += (_, _) =>
        {
            bool up = !LogoImage.Classes.Contains("breathe");
            LogoImage.Classes.Set("breathe", up);
            GlowBorder.Classes.Set("breathe", up);
        };
        DispatcherTimer.RunOnce(() => _breatheTimer.Start(), TimeSpan.FromMilliseconds(550));
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
