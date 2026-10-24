using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Platform;
using Paperbunkr.App.Services.Splash;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming.Motion;

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
/// that. Reduced motion (<see cref="ThemeService.GetReducedMotion"/>) skips the motion entirely -
/// checked here rather than via the <c>PbMotion*</c> resources, which are not necessarily zeroed
/// yet at splash time.
/// </para>
/// </summary>
public partial class SplashWindow : Window
{
    private readonly bool _reducedMotion;
    private readonly bool _ambientMotionEnabled;
    private readonly ScaleTransform _logoScale = new(1, 1);
    private readonly CancellationTokenSource _motionCts = new();
    private bool _closing;

    public SplashWindow()
        : this(TryGetReducedMotion(), TryGetAmbientMotionEnabled())
    {
    }

    /// <summary>
    /// The persisted "Splash ambient motion" preference (docs/superpowers/specs/2026-09-21-cosmetics-pitch-
    /// design.md #6), read with the same schema-behind-the-model guard as <see cref="TryGetReducedMotion"/> - a
    /// failed read defaults to the shipped default (on). Bootstrap-crash safe mode always turns it off: that
    /// launch exists to get a window up with the least possible risk.
    /// </summary>
    private static bool TryGetAmbientMotionEnabled()
    {
        if (SafeModeState.Active)
        {
            return false;
        }

        try
        {
            var service = new ThemeService();
            MotionPolicy.SetPreference(service.GetMotionPreference());
            MotionPolicy.SetEffectQuality(service.GetEffectQuality());
            MotionPolicy.RefreshSystemPreference();
            return service.GetSplashAmbientMotion() && MotionPolicy.AllowsContinuous();
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Splash: ambient-motion lookup failed ({ex.GetType().Name}) - assuming on.");
            return true;
        }
    }

    /// <summary>
    /// The splash is shown by <c>App.RunDesktopStartupAsync</c> <b>before</b> the database
    /// integrity check and pending-migration step run. On a cold start right after an app update
    /// the DB schema can be behind the EF model, so a plain <see cref="ThemeService.GetReducedMotion"/>
    /// here throws <c>SqliteException</c> - which, before this guard, faulted the whole fire-and-
    /// forget startup task and left a dead splash with no MainWindow. Default to motion-on; the
    /// real preference is applied to the rest of the app once startup finishes.
    /// </summary>
    private static bool TryGetReducedMotion()
    {
        try
        {
            return new ThemeService().GetReducedMotion();
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Splash: reduced-motion lookup failed ({ex.GetType().Name}) - assuming motion on.");
            return false;
        }
    }

    public SplashWindow(bool reducedMotion)
        : this(reducedMotion, ambientMotionEnabled: true)
    {
    }

    public SplashWindow(bool reducedMotion, bool ambientMotionEnabled)
        : this(reducedMotion, ambientMotionEnabled, ChoosePalette())
    {
    }

    public SplashWindow(bool reducedMotion, bool ambientMotionEnabled, SplashPalette palette)
    {
        _reducedMotion = reducedMotion || !MotionPolicy.AllowsMotion(MotionRole.Reveal);
        _ambientMotionEnabled = ambientMotionEnabled;
        Palette = palette;
        ApplyPalette(palette);
        InitializeComponent();

        // Transparent so the card's rounded corners and shadow show (OverlayHostWindow uses the same setup).
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        LogoImage.RenderTransform = _logoScale;
        Card.CornerRadius = new CornerRadius(palette.CornerRadius);
        LoadingBar.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Avalonia.Controls.Primitives.RangeBase.ValueProperty,
                Duration = Paperbunkr.App.Controls.MotionTokens.Get(MotionRole.Replace).Fade,
                Easing = this.TryFindResource("PbMotionEase", out var ease) && ease is Easing e ? e : new CubicEaseOut(),
            },
        };

        var accent = palette.Accent;
        AmbientDots.DotColor = accent;
        if (GlowBorder.Background is RadialGradientBrush glowBrush && glowBrush.GradientStops.Count == 2)
        {
            // A light surface needs less of the accent for the same soft tint.
            byte alpha = palette.IsLight ? (byte)0x59 : (byte)0x8C;
            glowBrush.GradientStops[0].Color = Color.FromArgb(alpha, accent.R, accent.G, accent.B);
            glowBrush.GradientStops[1].Color = Color.FromArgb(0x00, accent.R, accent.G, accent.B);
        }
        if (LoadingBar.Foreground is LinearGradientBrush progressBrush && progressBrush.GradientStops.Count == 2)
        {
            progressBrush.GradientStops[0].Color = accent;
            progressBrush.GradientStops[1].Color = palette.AccentText;
        }

        if (_reducedMotion)
        {
            LogoImage.Opacity = 1;
            GlowBorder.Opacity = 0.5;
        }

        Opened += OnOpened;
    }

    /// <summary>The palette this splash draws with (exposed for tests).</summary>
    public SplashPalette Palette { get; }

    /// <summary>
    /// The theme this launch will open with, from <c>splash-theme.json</c> (docs/superpowers/specs/2026-10-22-splash-theme-redesign-design.md)
    /// - never the database, which may be mid-migration here. Auto mode is resolved the way <see cref="ThemeService"/> resolves it a moment
    /// later: the OS light/dark preference for Follow system, the local hour for Scheduled. Anything missing falls back to the Default theme.
    /// </summary>
    private static SplashPalette ChoosePalette()
    {
        try
        {
            var snapshot = new SplashThemeStore(SplashThemeStore.DefaultPath()).Load();
            PlatformThemeVariant? os = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant;
            return SplashThemeChooser.Choose(snapshot, os, DateTime.Now.Hour);
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Splash: theme snapshot lookup failed ({ex.GetType().Name}) - using the default theme.");
            return SplashPalette.Default;
        }
    }

    /// <summary>Writes the palette under the usual Pb* keys into this window's own resources, which the XAML's DynamicResources find before
    /// the application's (which at this point still hold the built-in defaults).</summary>
    private void ApplyPalette(SplashPalette p)
    {
        void Set(string name, Color color)
        {
            Resources[$"Pb{name}Color"] = color;
            Resources[$"Pb{name}Brush"] = new SolidColorBrush(color);
        }

        Set("Surface1", p.Surface);
        Set("Bg", p.Surface);
        Set("Text", p.Text);
        Set("TextMuted", p.TextMuted);
        Set("TextFaint", p.TextFaint);
        Set("Border", p.Border);
        Set("Accent", p.Accent);
        Set("AccentText", p.AccentText);
        Set("AccentSoft", p.AccentSoft);
        RequestedThemeVariant = p.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
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

        // After first paint (Opened), never before - and not at all when the preference is off.
        if (_ambientMotionEnabled)
        {
            AmbientDots.Start();
        }
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
        AmbientDots.Stop();

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
