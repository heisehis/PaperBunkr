using System;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using Paperbunkr.App.Theming.Motion;

namespace Paperbunkr.App.Controls;

/// <summary>
/// The sliding tab underline (docs/superpowers/specs/2026-10-20-motion-system-v2-on-page-design.md Q20). Every panel holding
/// <c>Button.tab</c> children gets one 2 px accent bar in its adorner layer that slides to the active tab's left edge and width
/// (<c>translate(x) scale(w,1)</c> of a 1 px bar, on the Reveal move token and the move curve; Reduced snaps). The first placement, a resize
/// or a relayout snap; only a change of tab animates. The panel gets class <c>tabIndicatorHost</c>, which hides each tab's own underline.
/// Drawn as an adorner so no tab's layout changes. Installed app-wide with <see cref="PopupMotion"/>; no markup needed.
/// </summary>
public static class TabIndicator
{
    public const string HostClass = "tabIndicatorHost";

    private sealed class HostState
    {
        public Border? Bar;
        public Button? Active;
        public (double X, double Width)? Placed;
    }

    private static readonly ConditionalWeakTable<Panel, HostState> Hosts = new();
    private static readonly ConditionalWeakTable<Button, NotifyCollectionChangedEventHandler> Watched = new();
    private static bool s_installed;

    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        Control.LoadedEvent.AddClassHandler<Button>((button, _) =>
        {
            if (button.Classes.Contains("tab"))
            {
                Watch(button);
            }
        });
    }

    /// <summary>The bar's transform for a tab at <paramref name="x"/> that is <paramref name="width"/> wide.</summary>
    internal static string IndicatorTransform(double x, double width) =>
        FormattableString.Invariant($"translate({Math.Round(x, 2)}px,0px) scale({Math.Max(0, Math.Round(width, 2))},1)");

    private static void Watch(Button button)
    {
        if (!Watched.TryGetValue(button, out _))
        {
            NotifyCollectionChangedEventHandler handler = (_, _) => OnClassesChanged(button);
            Watched.Add(button, handler);
            button.Classes.CollectionChanged += handler;
        }

        if (button.Classes.Contains("active") && button.Parent is Panel host)
        {
            Place(host, button, animate: false);
        }
    }

    private static void OnClassesChanged(Button button)
    {
        if (button.Parent is Panel host && button.Classes.Contains("active") && button.IsAttachedToVisualTree())
        {
            Place(host, button, animate: true);
        }
    }

    private static void Place(Panel host, Button active, bool animate)
    {
        var state = Hosts.GetOrCreateValue(host);
        if (state.Bar is null)
        {
            state.Bar = CreateBar();
            host.Classes.Add(HostClass);

            // The adorner root itself is positioned by AdornerLayer (it overwrites the root's RenderTransform to track the host), so the
            // bar moves inside a plain container.
            AdornerLayer.SetAdorner(host, new Panel { IsHitTestVisible = false, Children = { state.Bar } });
            host.LayoutUpdated += (_, _) => Relayout(host, state);
        }

        bool changedTab = !ReferenceEquals(state.Active, active);
        state.Active = active;
        Move(host, state, animate && changedTab && state.Placed is not null);
    }

    /// <summary>A resize or relayout (a tab label changing width) snaps the bar to the active tab without animating.</summary>
    private static void Relayout(Panel host, HostState state)
    {
        if (state.Active is { } active && active.Parent == host && Measure(host, active) is { } now && now != state.Placed)
        {
            Move(host, state, animate: false);
        }
    }

    private static (double X, double Width)? Measure(Panel host, Button active) =>
        active.TranslatePoint(default, host) is { } origin && active.Bounds.Width > 0 ? (origin.X, active.Bounds.Width) : null;

    private static void Move(Panel host, HostState state, bool animate)
    {
        if (state.Bar is not { } bar || state.Active is not { } active || Measure(host, active) is not { } target)
        {
            return;
        }

        state.Placed = target;
        var transform = TransformOperations.Parse(IndicatorTransform(target.X, target.Width));
        if (!animate)
        {
            bar.Transitions = null;
            bar.RenderTransform = transform;
            bar.Opacity = 1;
            return;
        }

        bar.Transitions =
        [
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = MotionPolicy.Resolve(MotionRole.Reveal).Move,
                Easing = Curve(),
            },
        ];
        bar.RenderTransform = transform;
    }

    private static Border CreateBar()
    {
        var bar = new Border
        {
            Width = 1,
            Height = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            RenderTransformOrigin = RelativePoint.TopLeft,
            IsHitTestVisible = false,
            Opacity = 0,
        };
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("PbAccentBrush"));
        return bar;
    }

    private static Easing Curve() =>
        Application.Current is { } app && app.TryGetResource("PbMotionMoveEase", null, out var value) && value is Easing easing ? easing : new CubicEaseInOut();
}
