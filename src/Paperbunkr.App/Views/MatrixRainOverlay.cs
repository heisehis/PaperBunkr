using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// Matrix theme's animated code-rain background layer (docs/superpowers/specs/2026-09-16-theme-
/// system-design.md § Matrix rain effect). Mounted once in MainWindow.axaml, <c>IsVisible</c> bound
/// to <c>MainViewModel.IsMatrixThemeActive</c>. Mirrors <c>PageCanvas</c>'s composition-visual setup
/// exactly (<see cref="ElementComposition.GetElementVisual"/> → <c>Compositor.CreateCustomVisual</c>
/// → <see cref="ElementComposition.SetElementChildVisual"/> in <c>OnAttachedToVisualTree</c>).
/// </summary>
public sealed class MatrixRainOverlay : Control
{
    /// <summary>
    /// Attached property any content-bearing Border/Panel can set to register its bounds as an
    /// opaque backplate the rain must not render behind (overdraw constraint, § Matrix rain effect).
    /// Currently registered nowhere: an earlier pass marked MainWindow.axaml's two screen-hosting
    /// <c>TransitioningContentControl</c>s, but those span the entire content area and have no
    /// background of their own, so registering them clipped the whole rain out (it never rendered).
    /// Only mark a control that is genuinely opaque AND covers a small part of the overlay.
    /// </summary>
    public static readonly AttachedProperty<bool> IsOpaqueBackplateProperty =
        AvaloniaProperty.RegisterAttached<MatrixRainOverlay, Control, bool>("IsOpaqueBackplate");

    public static bool GetIsOpaqueBackplate(Control element) => element.GetValue(IsOpaqueBackplateProperty);

    public static void SetIsOpaqueBackplate(Control element, bool value) => element.SetValue(IsOpaqueBackplateProperty, value);

    private static readonly List<Control> BackplateRegistry = new();

    static MatrixRainOverlay()
    {
        IsOpaqueBackplateProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if ((bool)e.NewValue!)
            {
                if (!BackplateRegistry.Contains(control))
                {
                    BackplateRegistry.Add(control);
                }
                control.AttachedToVisualTree += OnBackplateAttached;
                control.DetachedFromVisualTree += OnBackplateDetached;
                control.LayoutUpdated += OnBackplateLayoutUpdated;
            }
            else
            {
                BackplateRegistry.Remove(control);
                control.AttachedToVisualTree -= OnBackplateAttached;
                control.DetachedFromVisualTree -= OnBackplateDetached;
                control.LayoutUpdated -= OnBackplateLayoutUpdated;
            }
        });
    }

    private static void OnBackplateAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control && !BackplateRegistry.Contains(control))
        {
            BackplateRegistry.Add(control);
        }
    }

    private static void OnBackplateDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
        {
            BackplateRegistry.Remove(control);
        }
    }

    private static void OnBackplateLayoutUpdated(object? sender, EventArgs e)
    {
        // Any registered backplate's own layout changing is a cue every live overlay should
        // recompute its exclude rects - cheap (a handful of controls at most), and layout changes
        // are inherently infrequent compared to render frames.
        foreach (var overlay in LiveOverlays)
        {
            overlay.RecomputeExcludeRects();
        }
    }

    private static readonly List<MatrixRainOverlay> LiveOverlays = new();

    private CompositionCustomVisual? _visual;
    private DispatcherTimer? _batteryTimer;
    private bool _lastThrottled;
    private ThemeService? _themeService;

    public MatrixRainOverlay()
    {
        IsHitTestVisible = false; // decorative only - never intercepts pointer input from real content above it
    }

    private bool _attached;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            // The overlay is mounted with IsVisible=false whenever another theme is active and only
            // flips true on a live switch to Matrix - an element that was invisible when it attached
            // may not have a composition visual to hang the custom visual on yet, so (re)try the
            // setup the moment it becomes visible instead of relying on the attach-time attempt.
            EnsureVisual();
            SendRunState();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _attached = true;
        if (!LiveOverlays.Contains(this))
        {
            LiveOverlays.Add(this);
        }
        EnsureVisual();

        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _batteryTimer.Tick += (_, _) =>
        {
            bool throttled = BatteryStatusInterop.IsThrottled();
            if (throttled != _lastThrottled)
            {
                _lastThrottled = throttled;
                SendRunState();
            }
        };
        _batteryTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _attached = false;
        _batteryTimer?.Stop();
        _batteryTimer = null;
        LiveOverlays.Remove(this);

        _visual?.SendHandlerMessage(new MatrixRainDispose());
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;
    }

    private void EnsureVisual()
    {
        if (_visual is not null || !_attached)
        {
            return;
        }

        var elementVisual = ElementComposition.GetElementVisual(this);
        if (elementVisual is null)
        {
            return;
        }

        var handler = new MatrixRainVisualHandler();
        _visual = elementVisual.Compositor.CreateCustomVisual(handler);
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.Size = new Vector(Bounds.Width, Bounds.Height);

        _themeService ??= new ThemeService();
        SendConfig();
        RecomputeExcludeRects();
        SendRunState();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_visual is not null)
        {
            _visual.Size = new Vector(Bounds.Width, Bounds.Height);
            SendConfig();
            RecomputeExcludeRects();
        }
    }

    private void SendConfig()
    {
        if (_visual is null || _themeService is null)
        {
            return;
        }

        // Self-contained: loads the Matrix theme's own glyphs/text color directly rather than
        // requiring a binding chain from the ViewModel layer - this overlay only ever renders while
        // Matrix is the active theme (IsVisible is bound to exactly that condition), so there's
        // nothing to parameterize by a different active theme's colors.
        try
        {
            var theme = _themeService.LoadTheme("matrix");
            var glyphs = theme.MatrixGlyphs is { Length: > 0 } ? theme.MatrixGlyphs : Array.Empty<string>();
            _visual.SendHandlerMessage(new MatrixRainConfig(Bounds.Size, glyphs, theme.Colors.Text));
        }
        catch
        {
            // Matrix theme.json missing/invalid - overlay simply stays blank rather than crashing
            // the app shell; IsVisible only turns true when the theme was actually selectable in the
            // first place, so this is a defensive fallback, not an expected path.
        }
    }

    /// <summary>
    /// Recomputes exclude rects from <see cref="BackplateRegistry"/>, translated into this overlay's
    /// own coordinate space (the registry holds arbitrary controls elsewhere in the visual tree).
    /// </summary>
    private void RecomputeExcludeRects()
    {
        if (_visual is null)
        {
            return;
        }

        var rects = new List<Rect>();
        foreach (var backplate in BackplateRegistry)
        {
            if (backplate.Bounds.Width <= 0 || backplate.Bounds.Height <= 0)
            {
                continue;
            }

            var topLeft = backplate.TranslatePoint(new Point(0, 0), this);
            if (topLeft is not { } origin)
            {
                continue;
            }

            rects.Add(new Rect(origin, backplate.Bounds.Size));
        }

        _visual.SendHandlerMessage(new MatrixRainExcludeRects(rects));
    }

    /// <summary>
    /// Whether the frame loop should be running right now: visible, not reduced-motion, not
    /// battery-throttled. Reduced motion is read the same way <c>ThemeService.ApplyReducedMotion</c>
    /// signals it everywhere else in this codebase - PbMotionFast resolved to TimeSpan.Zero - rather
    /// than a second database read.
    /// </summary>
    private void SendRunState()
    {
        if (_visual is null)
        {
            return;
        }

        bool reducedMotion = Application.Current?.Resources["PbMotionFast"] is TimeSpan { Ticks: 0 };

        bool shouldRun = IsVisible && !reducedMotion && !_lastThrottled;
        _visual.SendHandlerMessage(new MatrixRainRunState(shouldRun));
    }
}
