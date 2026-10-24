using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming.Motion;

namespace Paperbunkr.App.Controls;

/// <summary>
/// The reader's ambient letterbox (docs/superpowers/specs/2026-10-20-motion-system-v2-reader-design.md Q32-Q34). Two stacked layers make a
/// real crossfade between page gradients: a <c>BrushTransition</c> only interpolates solid colours, so between two gradients it just cut over
/// halfway. Each new <see cref="Brush"/> goes on the back layer, which fades in over the Ambient role's 360 ms, then becomes the front.
///
/// With <see cref="MotionEnabled"/> (Preferences > Reader > "Ambient motion", off by default) it also gives depth:
/// <list type="bullet">
/// <item><b>Parallax</b> - the layers drift up to 12 px against <see cref="ParallaxSource"/> (the page's pan, or continuous mode's scroll),
/// so the backdrop seems further away than the page. Ambient role: none under Reduced or the Performance tier.</item>
/// <item><b>Drift</b> - a very slow sway of the gradient while reading. Continuous Ambient: High quality only, paused on a low battery.</item>
/// </list>
/// The layers overscan by 16 px so neither ever shows an edge.
/// </summary>
public sealed class AmbientBackdrop : Panel
{
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<AmbientBackdrop, IBrush?>(nameof(Brush));

    public static readonly StyledProperty<Vector> ParallaxSourceProperty =
        AvaloniaProperty.Register<AmbientBackdrop, Vector>(nameof(ParallaxSource));

    public static readonly StyledProperty<bool> MotionEnabledProperty =
        AvaloniaProperty.Register<AmbientBackdrop, bool>(nameof(MotionEnabled));

    /// <summary>The drift's 0..1 phase. The drift animates this plain double on the backdrop itself (a keyframe animation of a
    /// <see cref="ScaleTransform"/> directly throws - Avalonia's transform animator only takes a Visual) and the sway follows it.</summary>
    internal static readonly StyledProperty<double> DriftPhaseProperty =
        AvaloniaProperty.Register<AmbientBackdrop, double>("DriftPhase");

    /// <summary>The farthest the layers drift from rest, in px.</summary>
    public const double MaxParallax = 12;

    /// <summary>How much of the page's pan/scroll the backdrop follows (opposite way).</summary>
    public const double ParallaxFactor = 0.04;

    private const double Overscan = 16;

    private readonly Border _front = Layer();
    private readonly Border _back = Layer();
    private readonly TranslateTransform _shift = new();
    private readonly ScaleTransform _sway = new(1, 1);
    private DispatcherTimer? _swap;
    private Animation? _drift;
    private System.Threading.CancellationTokenSource? _driftRun;

    public AmbientBackdrop()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        _front.RenderTransform = _shift;
        _back.RenderTransform = _shift;
        _back.Opacity = 0;
        Children.Add(_front);
        Children.Add(_back);
    }

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public Vector ParallaxSource
    {
        get => GetValue(ParallaxSourceProperty);
        set => SetValue(ParallaxSourceProperty, value);
    }

    public bool MotionEnabled
    {
        get => GetValue(MotionEnabledProperty);
        set => SetValue(MotionEnabledProperty, value);
    }

    /// <summary>The backdrop offset for a page pan/scroll of <paramref name="source"/>: opposite way, scaled, capped at <see cref="MaxParallax"/>.</summary>
    internal static Vector ParallaxOffset(Vector source) =>
        new(Math.Clamp(-source.X * ParallaxFactor, -MaxParallax, MaxParallax), Math.Clamp(-source.Y * ParallaxFactor, -MaxParallax, MaxParallax));

    internal Border FrontLayer => _front;

    /// <summary>The parallax translate both layers share (tests).</summary>
    internal TranslateTransform ParallaxShift => _shift;

    /// <summary>Whether the slow drift is running (tests).</summary>
    internal bool IsDrifting => _driftRun is not null;

    internal Border BackLayer => _back;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BrushProperty)
        {
            Crossfade(change.GetNewValue<IBrush?>());
        }
        else if (change.Property == DriftPhaseProperty)
        {
            double phase = change.GetNewValue<double>();
            _sway.ScaleX = 1 + (0.04 * phase);
            _sway.ScaleY = 1 + (0.03 * phase);
        }
        else if (change.Property == ParallaxSourceProperty || change.Property == MotionEnabledProperty)
        {
            ApplyParallax();
            if (change.Property == MotionEnabledProperty)
            {
                UpdateDrift();
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateDrift();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopDrift();
        _swap?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Crossfade(IBrush? brush)
    {
        _swap?.Stop();
        var fade = MotionPolicy.Resolve(MotionRole.Ambient).Fade;
        if (fade <= TimeSpan.Zero || _front.Background is null || !this.IsAttachedToVisualTree())
        {
            _front.Background = brush;
            _back.Transitions = null;
            _back.Opacity = 0;
            return;
        }

        // The back layer takes the new gradient and fades in over the old; once it is opaque it becomes the front.
        _back.Transitions = null;
        _back.Opacity = 0;
        _back.Background = brush;
        _back.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = fade, Easing = Curve() }];
        _back.Opacity = 1;
        _swap = new DispatcherTimer { Interval = fade };
        _swap.Tick += (_, _) =>
        {
            _swap?.Stop();
            _front.Background = _back.Background;
            _back.Transitions = null;
            _back.Opacity = 0;
        };
        _swap.Start();
    }

    private void ApplyParallax()
    {
        var offset = MotionEnabled && MotionPolicy.AllowsMotion(MotionRole.Ambient) ? ParallaxOffset(ParallaxSource) : default;
        _shift.X = offset.X;
        _shift.Y = offset.Y;
    }

    private void UpdateDrift()
    {
        if (MotionEnabled && this.IsAttachedToVisualTree() && MotionPolicy.AllowsContinuous(BatteryStatusInterop.IsThrottled()))
        {
            StartDrift();
        }
        else
        {
            StopDrift();
        }
    }

    /// <summary>A slow 24 s sway of the gradient layers, on their own scale so it never fights the parallax translate.</summary>
    private void StartDrift()
    {
        if (_driftRun is not null)
        {
            return;
        }

        foreach (var layer in new[] { _front, _back })
        {
            layer.RenderTransform = new TransformGroup { Children = { _sway, _shift } };
        }

        _drift = new Animation
        {
            Duration = TimeSpan.FromSeconds(24),
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(DriftPhaseProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(DriftPhaseProperty, 1d) } },
            },
        };
        _driftRun = new System.Threading.CancellationTokenSource();
        _ = _drift.RunAsync(this, _driftRun.Token);
    }

    private void StopDrift()
    {
        _driftRun?.Cancel();
        _driftRun = null;
        _drift = null;
        _sway.ScaleX = 1;
        _sway.ScaleY = 1;
        _front.RenderTransform = _shift;
        _back.RenderTransform = _shift;
    }

    private static Border Layer() => new()
    {
        Margin = new Thickness(-Overscan),
        RenderTransformOrigin = RelativePoint.Center,
        IsHitTestVisible = false,
    };

    private static Easing Curve() =>
        Application.Current is { } app && app.TryGetResource("PbMotionMoveEase", null, out var value) && value is Easing easing ? easing : new CubicEaseInOut();
}
