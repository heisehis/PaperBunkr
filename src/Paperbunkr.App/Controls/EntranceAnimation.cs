using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Staggered entrance for grid/list item containers (docs/superpowers/specs/2026-09-07-chrome-
/// content-motion-polish-design.md item 1). Set declaratively on any <see cref="ItemsControl"/>
/// (or <c>ListBox</c>, which derives from it) whose realized items should play a staggered
/// entrance:
/// <code>controls:EntranceAnimation.Enabled="{Binding PlayEntranceAnimation}"</code>
///
/// Wires the control's own <see cref="ItemsControl.ContainerPrepared"/> event - the one hook
/// guaranteed to fire on every container preparation, first realization or a virtualizing panel's
/// recycling reuse alike (unlike <c>AttachedToVisualTree</c>, which a reused-in-place container may
/// not re-raise). Each firing reads <see cref="EnabledProperty"/>'s *current* value at that moment,
/// not a live subscription - that's what makes this "one-shot per trigger, not per scroll-recycle":
/// a container prepared long after a real reload/filter/sort/view-mode change simply reads whatever
/// the bound ViewModel flag's current value happens to be, which is only "just turned true" right
/// after a genuine trigger event, per the flag's own doc comment
/// (<c>LibraryScreenViewModel.PlayEntranceAnimation</c>).
///
/// The actual visual states live in a plain style (Primitives.axaml) keyed off the "entranceReady"/
/// "entered" classes this adds to each container - so Reduced Motion is honored for free via the
/// existing PbMotionStandard token zeroing, and a container this never enables for is left
/// untagged, therefore always fully visible regardless of animation state elsewhere.
/// </summary>
public static class EntranceAnimation
{
    private const int PerItemDelayMs = 24;

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("Enabled", typeof(EntranceAnimation));

    public static bool GetEnabled(ItemsControl itemsControl) => itemsControl.GetValue(EnabledProperty);
    public static void SetEnabled(ItemsControl itemsControl, bool value) => itemsControl.SetValue(EnabledProperty, value);

    // Tracks which ItemsControls already have a ContainerPrepared subscription, so re-setting the
    // same attached property twice (e.g. a container reused across DataContext rebinds) never
    // double-subscribes.
    private static readonly ConditionalWeakTable<ItemsControl, object?> Wired = new();

    private static readonly ConditionalWeakTable<StyledElement, DispatcherTimer> PendingTimers = new();

    static EntranceAnimation()
    {
        EnabledProperty.Changed.AddClassHandler<ItemsControl>((itemsControl, _) => EnsureWired(itemsControl));
    }

    private static void EnsureWired(ItemsControl itemsControl)
    {
        if (Wired.TryGetValue(itemsControl, out _))
        {
            return;
        }

        Wired.Add(itemsControl, null);

        itemsControl.ContainerPrepared += (_, e) =>
        {
            if (e.Container is StyledElement container)
            {
                Prepare(container, e.Index, GetEnabled(itemsControl));
            }
        };
    }

    private static void Prepare(StyledElement container, int index, bool enabled)
    {
        if (PendingTimers.TryGetValue(container, out var existingTimer))
        {
            existingTimer.Stop();
            PendingTimers.Remove(container);
        }

        container.Classes.Remove("entered");

        if (!enabled)
        {
            // Not participating this preparation (e.g. ordinary scroll-driven recycling, or the
            // triggering flag had already been consumed by an earlier preparation burst) - stay
            // fully visible, untagged.
            container.Classes.Remove("entranceReady");
            return;
        }

        container.Classes.Add("entranceReady");

        // Reduced Motion collapses the stagger delay itself to zero, not just the fade/slide
        // transition - otherwise items would still visibly reveal one-by-one in rhythm, just each
        // one snapping instead of fading, which is still a motion effect Reduced Motion should
        // suppress (self-review finding against avalonia-pro-max/review-checklist's "reduced-motion
        // path tested" item).
        int delayMs = MotionTokens.IsReducedMotion() ? 0 : index * PerItemDelayMs;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            PendingTimers.Remove(container);
            container.Classes.Add("entered");
        };
        PendingTimers.Add(container, timer);
        timer.Start();
    }
}
