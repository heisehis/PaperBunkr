using System;
using System.Diagnostics;
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
///
/// <para>
/// <b>One-shot mode</b> (<see cref="OneShotProperty"/>, docs/superpowers/specs/2026-09-19-library-
/// scroll-smoothness-design.md §2): the always-on behavior above also arms a timer, two class changes and two
/// transitions for every container a virtualizing panel recycles while the user scrolls, because a
/// flag that stays <c>true</c> never stops being read. With <c>OneShot</c> on, <see cref="EnabledProperty"/>
/// turning <c>true</c> only <i>arms</i> an <see cref="EntranceWindow"/>; the window opens at the next container
/// preparation and lasts <see cref="EntranceWindow.Duration"/>, and only containers prepared inside it animate.
/// Owners pulse the flag (false then true) on the triggers that should animate. Screens that do not set
/// <c>OneShot</c> (Books, Home, Reading, Smart) keep the original behavior unchanged.
/// </para>
/// </summary>
public static class EntranceAnimation
{
    private const int PerItemDelayMs = 24;
    private const int MaxStaggerIndex = 20;

    public static readonly AttachedProperty<bool> OneShotProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("OneShot", typeof(EntranceAnimation));

    public static bool GetOneShot(ItemsControl itemsControl) => itemsControl.GetValue(OneShotProperty);
    public static void SetOneShot(ItemsControl itemsControl, bool value) => itemsControl.SetValue(OneShotProperty, value);

    private static readonly ConditionalWeakTable<ItemsControl, EntranceWindow> Windows = new();

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
        EnabledProperty.Changed.AddClassHandler<ItemsControl>((itemsControl, e) =>
        {
            EnsureWired(itemsControl);
            if (e.GetNewValue<bool>())
            {
                Windows.GetOrCreateValue(itemsControl).Arm();
            }
        });
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
                bool enabled = GetEnabled(itemsControl);
                if (enabled && GetOneShot(itemsControl))
                {
                    // One-shot: animate only inside the window a recent Enabled false->true opened.
                    enabled = Windows.GetOrCreateValue(itemsControl).IsOpen(Stopwatch.GetTimestamp());
                }

                Prepare(container, e.Index, enabled);
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

        Paperbunkr.App.Services.CoverPipelineStats.EntranceTimerArmed();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ComputeDelayMs(index, MotionTokens.IsReducedMotion())) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            PendingTimers.Remove(container);
            container.Classes.Add("entered");
        };
        PendingTimers.Add(container, timer);
        timer.Start();
    }

    /// <summary>
    /// Docs/superpowers/specs/2026-09-12-entrance-animation-v2-design.md §4: Library/Home are
    /// virtualized, so one container-preparation burst never realizes more than a viewport's worth
    /// of containers - <see cref="MaxStaggerIndex"/> is a no-op for them today. Books and Reading
    /// Lists are not virtualized, so every item realizes in one burst; without this clamp, a long
    /// list would get a stagger tail that keeps growing with its length. Reduced Motion still
    /// collapses the delay to zero entirely, not just the growth beyond the cap - otherwise items
    /// would still visibly reveal one-by-one in rhythm, just each one snapping instead of fading,
    /// which is still a motion effect Reduced Motion should suppress (self-review finding against
    /// avalonia-pro-max/review-checklist's "reduced-motion path tested" item).
    /// </summary>
    internal static int ComputeDelayMs(int index, bool reducedMotion) =>
        reducedMotion ? 0 : Math.Min(index, MaxStaggerIndex) * PerItemDelayMs;

    /// <summary>
    /// The one-shot entrance window (see the class remarks). <see cref="Arm"/> is called when the owner's flag turns
    /// <c>true</c>; the window then <b>opens at the first <see cref="IsOpen"/> query</b> (the first container
    /// preparation after the trigger, not the trigger itself, so a slow first layout cannot expire it) and stays
    /// open for <see cref="Duration"/>. Never armed, or armed and already expired, means closed.
    /// </summary>
    internal sealed class EntranceWindow
    {
        public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(500);

        private bool _armed;
        private bool _opened;
        private long _openedAt;

        public void Arm()
        {
            _armed = true;
            _opened = false;
        }

        public bool IsOpen(long nowTimestamp)
        {
            if (!_armed)
            {
                return false;
            }

            if (!_opened)
            {
                _opened = true;
                _openedAt = nowTimestamp;
                return true;
            }

            return Stopwatch.GetElapsedTime(_openedAt, nowTimestamp) < Duration;
        }
    }
}
