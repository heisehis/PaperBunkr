using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Fades and lifts an element in the first time it scrolls into view (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md,
/// "Scroll-reveal" - the landing site's <c>Reveal.astro</c> brought into the app). One-shot per element: once revealed it stays
/// revealed. An element already on screen at its first layout reveals instantly, so the first paint is never delayed on top of
/// the card stagger. Reduced Motion: no hidden start state at all, so there is nothing to reveal.
/// </summary>
public static class ScrollReveal
{
    private const double RiseDistance = 12;
    private const double VisibleFraction = 0.08;

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ScrollReveal));

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    private sealed class State
    {
        public bool Revealed;
        public bool SawOutOfView;
    }

    private static readonly ConditionalWeakTable<Control, State> States = new();

    static ScrollReveal()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (!e.GetNewValue<bool>() || States.TryGetValue(control, out _))
            {
                return;
            }

            if (MotionTokens.IsReducedMotion())
            {
                return;
            }

            States.Add(control, new State());
            control.Opacity = 0;
            control.RenderTransform = TransformOperations.Parse($"translateY({RiseDistance}px)");
            control.EffectiveViewportChanged += OnEffectiveViewportChanged;
        });
    }

    /// <summary>Whether <paramref name="control"/> has been revealed (true when it was never hidden) - exposed for tests.</summary>
    public static bool IsRevealed(Control control) => !States.TryGetValue(control, out var state) || state.Revealed;

    private static void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (sender is not Control control || !States.TryGetValue(control, out var state) || state.Revealed)
        {
            return;
        }

        var viewport = e.EffectiveViewport;
        var bounds = new Rect(control.Bounds.Size);
        double visibleHeight = bounds.Intersect(viewport).Height;
        bool inView = bounds.Height > 0 && viewport.Height > 0 && visibleHeight >= Math.Min(bounds.Height, viewport.Height) * VisibleFraction;

        if (!inView)
        {
            state.SawOutOfView = true;
            return;
        }

        Reveal(control, state, animate: state.SawOutOfView);
    }

    private static void Reveal(Control control, State state, bool animate)
    {
        state.Revealed = true;
        control.EffectiveViewportChanged -= OnEffectiveViewportChanged;

        if (animate && !MotionTokens.IsReducedMotion())
        {
            var duration = TimeSpan.FromTicks(MotionTokens.GetStandard().Ticks * 2);
            var easing = new CubicEaseOut();
            control.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = easing },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = easing },
            };
        }

        control.Opacity = 1;
        control.RenderTransform = TransformOperations.Identity;
    }
}
