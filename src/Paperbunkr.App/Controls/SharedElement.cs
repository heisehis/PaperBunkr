using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Declarative participation in a shared-element cover flight (docs/superpowers/specs/2026-09-04-
/// navigation-transition-system-design.md). Set <see cref="KeyProperty"/> on the cover element in
/// each participating screen, e.g. <c>controls:SharedElement.Key="{Binding CoverKey}"</c> bound to
/// something like <c>"cover:42"</c> - the Library tile and the Detail hero for the same issue
/// resolve to the same key. <see cref="ImageSourceProperty"/> hands
/// <see cref="SharedElementTransitionService"/> the already-decoded bitmap to clone.
///
/// Registration is automatic and tied to the element's own visual-tree lifetime: it registers on
/// <see cref="Visual.AttachedToVisualTree"/> and unregisters on
/// <see cref="Visual.DetachedFromVisualTree"/>, reading <see cref="KeyProperty"/>'s current value
/// each time (so a virtualized-away element is simply never registered - the service treats "no
/// registration for this key" as "fall back to plain cross-fade", not an error). If
/// <see cref="KeyProperty"/> changes while the element is already attached (a recycled/rebound
/// container), it re-registers under the new key.
/// </summary>
public static class SharedElement
{
    public static readonly AttachedProperty<string?> KeyProperty =
        AvaloniaProperty.RegisterAttached<Visual, string?>("Key", typeof(SharedElement));

    public static readonly AttachedProperty<IImage?> ImageSourceProperty =
        AvaloniaProperty.RegisterAttached<Visual, IImage?>("ImageSource", typeof(SharedElement));

    public static string? GetKey(Visual element) => element.GetValue(KeyProperty);
    public static void SetKey(Visual element, string? value) => element.SetValue(KeyProperty, value);

    public static IImage? GetImageSource(Visual element) => element.GetValue(ImageSourceProperty);
    public static void SetImageSource(Visual element, IImage? value) => element.SetValue(ImageSourceProperty, value);

    // Keyed by the element itself (weakly, via ConditionalWeakTable) so wiring is cleaned up
    // automatically when the element is collected - no explicit teardown call needed anywhere.
    private static readonly ConditionalWeakTable<Visual, Subscription> Subscriptions = new();

    private sealed class Subscription
    {
        public string? RegisteredKey;
    }

    static SharedElement()
    {
        KeyProperty.Changed.AddClassHandler<Visual>(OnKeyChanged);
    }

    private static void OnKeyChanged(Visual element, AvaloniaPropertyChangedEventArgs e)
    {
        if (Subscriptions.TryGetValue(element, out var subscription))
        {
            // Already wired up (AttachedToVisualTree/DetachedFromVisualTree subscribed below). If
            // the key changed while the element is currently in the tree, re-register it now rather
            // than waiting for a detach/reattach that may never come.
            if (subscription.RegisteredKey is { } oldKey && element.IsAttachedToVisualTree())
            {
                SharedElementTransitionService.Shared.Unregister(oldKey, element);
                subscription.RegisteredKey = null;

                if (e.GetNewValue<string?>() is { } newKey)
                {
                    SharedElementTransitionService.Shared.Register(newKey, element, () => GetImageSource(element));
                    subscription.RegisteredKey = newKey;
                }
            }

            return;
        }

        subscription = new Subscription();
        Subscriptions.Add(element, subscription);

        element.AttachedToVisualTree += (_, _) =>
        {
            if (GetKey(element) is { } key)
            {
                SharedElementTransitionService.Shared.Register(key, element, () => GetImageSource(element));
                subscription.RegisteredKey = key;
            }
        };

        element.DetachedFromVisualTree += (_, _) =>
        {
            if (subscription.RegisteredKey is { } key)
            {
                SharedElementTransitionService.Shared.Unregister(key, element);
                subscription.RegisteredKey = null;
            }
        };

        // The property may already have a value by the time this first Changed notification fires
        // (a XAML-set/bound Key applied before the element ever attaches) - if it's already in the
        // tree (rare, but possible for a dynamically-added element), register immediately instead of
        // waiting for an AttachedToVisualTree that already happened.
        if (element.IsAttachedToVisualTree() && e.GetNewValue<string?>() is { } initialKey)
        {
            SharedElementTransitionService.Shared.Register(initialKey, element, () => GetImageSource(element));
            subscription.RegisteredKey = initialKey;
        }
    }
}
