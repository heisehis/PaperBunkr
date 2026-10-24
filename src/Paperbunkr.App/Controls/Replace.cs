using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Paperbunkr.App.Theming.Motion;

namespace Paperbunkr.App.Controls;

/// <summary>
/// The Replace crossfade (docs/superpowers/specs/2026-10-20-motion-system-v2-on-page-design.md Q21/Q23): when the value bound to
/// <c>controls:Replace.On</c> changes - a tab, a range, a data set - the control fades in from 0 over <c>PbMotionReplace</c>. The first
/// value does not animate (that is the screen arriving, which the route transition already shows); a newer change cancels the running fade;
/// there is no outgoing half. It is a keyframe opacity animation (opacity keyframes are safe here; RenderTransform keyframes are what
/// crash), so the control's own <c>Transitions</c> are never touched. Off plays nothing.
/// </summary>
public static class Replace
{
    public static readonly AttachedProperty<object?> OnProperty =
        AvaloniaProperty.RegisterAttached<Control, object?>("On", typeof(Replace));

    private static readonly ConditionalWeakTable<Control, CancellationTokenSource> Running = new();

    static Replace()
    {
        OnProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            // A panel bound to its own "is this tab shown" flag only fades when it becomes the shown one; hiding never animates.
            if (e.OldValue is not null && !Equals(e.OldValue, e.NewValue) && e.NewValue is not false)
            {
                Play(control);
            }
        });
    }

    public static object? GetOn(Control control) => control.GetValue(OnProperty);

    public static void SetOn(Control control, object? value) => control.SetValue(OnProperty, value);

    /// <summary>Fades <paramref name="control"/> in now (also used directly, e.g. by a chart when its data changes).</summary>
    public static void Play(Control control)
    {
        if (Running.TryGetValue(control, out var previous))
        {
            previous.Cancel();
            Running.Remove(control);
        }

        TimeSpan duration = MotionPolicy.Resolve(MotionRole.Replace).Fade;
        if (duration <= TimeSpan.Zero || !control.IsAttachedToVisualTree())
        {
            return;
        }

        var cts = new CancellationTokenSource();
        Running.Add(control, cts);
        var animation = new Animation
        {
            Duration = duration,
            Easing = Curve(),
            FillMode = FillMode.None,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
            },
        };
        _ = animation.RunAsync(control, cts.Token);
    }

    private static Easing Curve() =>
        Application.Current is { } app && app.TryGetResource("PbMotionEase", null, out var value) && value is Easing easing ? easing : new CubicEaseOut();
}
