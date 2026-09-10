using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// The borderless startup splash (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-
/// design.md, Decision 2). Shown first by <c>App.axaml.cs</c>; the heavy init runs (DB work on a
/// background thread, then MainViewModel/MainWindow construction on the UI thread) reporting into
/// the bound <see cref="ViewModels.SplashViewModel"/>; then <c>App.axaml.cs</c> shows
/// <c>MainWindow</c> and calls <see cref="FadeOutAndCloseAsync"/>.
///
/// <para>
/// The entrance + breathing motion is code-driven (<see cref="Animation.RunAsync(Avalonia.Animation.Animatable,CancellationToken)"/>)
/// and kicked off from <see cref="Window.Opened"/>. Three earlier attempts drove it from XAML
/// <c>&lt;Style.Animations&gt;</c> and it never visibly played - a style animation whose selector
/// already matches when the style attaches (at <c>EndInit</c>, before the visual is realized and
/// has a running clock) can complete before the first paint. Starting from <c>Opened</c> avoids
/// that. Reduced motion (<see cref="SkinService.GetReducedMotion"/>) skips the motion entirely -
/// checked here rather than via the <c>PbMotion*</c> resources, which are not necessarily zeroed
/// yet at splash time.
/// </para>
/// </summary>
public partial class SplashWindow : Window
{
    private readonly bool _reducedMotion;
    private readonly ScaleTransform _logoScale = new(1, 1);
    private readonly CancellationTokenSource _motionCts = new();
    private bool _closing;

    public SplashWindow()
        : this(TryGetReducedMotion())
    {
    }

    /// <summary>
    /// The splash is shown by <c>App.RunDesktopStartupAsync</c> <b>before</b> the database
    /// integrity check and pending-migration step run. On a cold start right after an app update
    /// the DB schema can be behind the EF model, so a plain <see cref="SkinService.GetReducedMotion"/>
    /// here throws <c>SqliteException</c> - which, before this guard, faulted the whole fire-and-
    /// forget startup task and left a dead splash with no MainWindow. Default to motion-on; the
    /// real preference is applied to the rest of the app once startup finishes.
    /// </summary>
    private static bool TryGetReducedMotion()
    {
        try
        {
            return new SkinService().GetReducedMotion();
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Splash: reduced-motion lookup failed ({ex.GetType().Name}) - assuming motion on.");
            return false;
        }
    }

    public SplashWindow(bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
        InitializeComponent();

        LogoImage.RenderTransform = _logoScale;

        if (reducedMotion)
        {
            LogoImage.Opacity = 1;
            GlowBorder.Opacity = 0.5;
        }

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (_reducedMotion || _closing)
        {
            LogoImage.Opacity = 1;
            return;
        }

        DiagnosticsService.LogMilestone("Splash: Opened - starting emblem motion.");
        _ = RunMotionAsync(_motionCts.Token);
    }

    private async Task RunMotionAsync(CancellationToken ct)
    {
        try
        {
            // Entrance: fade in + scale up from 0.88, once. Animations run against the *Visual*
            // (LogoImage) - the TransformAnimator casts its target to Visual and reaches into
            // Visual.RenderTransform (the ScaleTransform installed in the constructor); handing it
            // the ScaleTransform directly throws an InvalidCastException.
            var entrance = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(480),
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0d),
                        Setters =
                        {
                            new Setter(OpacityProperty, 0d),
                            new Setter(ScaleTransform.ScaleXProperty, 0.88d),
                            new Setter(ScaleTransform.ScaleYProperty, 0.88d),
                        },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1d),
                        Setters =
                        {
                            new Setter(OpacityProperty, 1d),
                            new Setter(ScaleTransform.ScaleXProperty, 1d),
                            new Setter(ScaleTransform.ScaleYProperty, 1d),
                        },
                    },
                },
            };

            await entrance.RunAsync(LogoImage, ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            LogoImage.Opacity = 1;

            // Breathing pulse: emblem scale + glow opacity, both looping forever until close.
            var breatheScale = new Animation
            {
                Duration = TimeSpan.FromSeconds(1.6),
                IterationCount = IterationCount.Infinite,
                PlaybackDirection = PlaybackDirection.Alternate,
                Easing = new SineEaseInOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0d),
                        Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, 1d),
                            new Setter(ScaleTransform.ScaleYProperty, 1d),
                        },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1d),
                        Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, 1.06d),
                            new Setter(ScaleTransform.ScaleYProperty, 1.06d),
                        },
                    },
                },
            };
            var breatheGlow = new Animation
            {
                Duration = TimeSpan.FromSeconds(1.6),
                IterationCount = IterationCount.Infinite,
                PlaybackDirection = PlaybackDirection.Alternate,
                Easing = new SineEaseInOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0.32d) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0.8d) } },
                },
            };

            await Task.WhenAll(
                breatheScale.RunAsync(LogoImage, ct),
                breatheGlow.RunAsync(GlowBorder, ct));
        }
        catch (OperationCanceledException)
        {
            // Expected on close.
        }
        catch (Exception ex)
        {
            // A splash-decoration animation must never take the app down (an earlier draft threw
            // an InvalidCastException from here that surfaced as an unobserved-task crash).
            DiagnosticsService.LogMilestone($"Splash: emblem motion failed ({ex.GetType().Name}: {ex.Message}) - showing static emblem.");
            try { LogoImage.Opacity = 1; } catch { /* tearing down */ }
        }
    }

    /// <summary>Fades the whole window out over ~150ms, then closes it. Called after
    /// <c>MainWindow</c> is up.</summary>
    public async Task FadeOutAndCloseAsync()
    {
        _closing = true;
        _motionCts.Cancel();

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
