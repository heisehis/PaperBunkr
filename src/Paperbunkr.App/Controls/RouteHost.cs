using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Controls;

/// <summary>
/// The shell's one screen host (docs/superpowers/specs/2026-10-20-motion-system-v2-navigation-design.md §3.1), replacing the separate
/// lateral and drill-down <c>TransitioningContentControl</c>s - which went blank while the other group was active (so crossing groups was a
/// cut) and rebuilt a screen's whole view on every return.
///
/// <para><b>Views are kept.</b> Each screen view model gets one view, built from this host's <see cref="Control.DataTemplates"/> the first
/// time it is shown and reused after, so coming back to Library keeps its scroll, focus and selection with no restore code. Screen view
/// models are permanent singletons owned by <c>MainViewModel</c>, so the cache is bounded. An <see cref="ITransientScreen"/> (a reader)
/// is rebuilt on every visit. A view lives in a <c>RouteLayer</c> Border the transition animates, so a screen's own root styling is never
/// touched. The layer is added when the screen is shown and removed once it has left, so screens attach and detach exactly as before.</para>
///
/// <para><b>The transition</b> follows <see cref="Intent"/> on the Travel role: a rail switch fades the new screen in while it settles
/// 12 px up; a drill-in grows it from 0.985; Back shrinks the old one to 0.985 as it leaves; a reader or an unknown move just crossfades.
/// The outgoing screen takes no pointer input and leaves tab order at once. A new navigation mid-transition removes the oldest leaving
/// screen and retargets the rest from where they are, so rapid clicks never queue. Removal always happens from a timer, never inside the
/// click that navigated (CLAUDE.md: detaching a control mid-event corrupts Avalonia's child lists).</para>
/// </summary>
public class RouteHost : Panel
{
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<RouteHost, object?>(nameof(Content));

    public static readonly StyledProperty<RouteIntent> IntentProperty =
        AvaloniaProperty.Register<RouteHost, RouteIntent>(nameof(Intent));

    /// <summary>Removes a leaving screen after its exit. A seam because the headless test dispatcher never fires timers.</summary>
    internal static Func<Action, TimeSpan, IDisposable> ScheduleAfter { get; set; } = (action, delay) => DispatcherTimer.RunOnce(action, delay);

    private const double LateralOffset = 12;
    private const double DepthScale = 0.985;

    private readonly Dictionary<object, Border> _layers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _built = new(ReferenceEqualityComparer.Instance);
    private Border? _current;
    private Border? _leaving;
    private IDisposable? _pendingRemoval;

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public RouteIntent Intent
    {
        get => GetValue(IntentProperty);
        set => SetValue(IntentProperty, value);
    }

    /// <summary>The view currently shown for <paramref name="content"/>, if it has been built (tests).</summary>
    internal Control? ViewFor(object content) => _layers.TryGetValue(content, out var layer) ? layer.Child : null;

    internal Border? CurrentLayer => _current;

    internal Border? LeavingLayer => _leaving;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty)
        {
            Navigate(change.NewValue);
        }
    }

    private void Navigate(object? content)
    {
        FinishLeaving();

        var outgoing = _current;
        var incoming = content is null ? null : LayerFor(content);
        if (ReferenceEquals(incoming, outgoing))
        {
            return;
        }

        bool reused = incoming is not null && content is not null && _built.Contains(content);
        _current = incoming;
        if (incoming is not null)
        {
            SetInteractive(incoming, true);
            Children.Add(incoming);
            _built.Add(content!);

            // A drill-in opens a new destination (another series, another issue): a kept view starts at the top, as the rebuilt view
            // used to. Back and rail switches keep where the user was - the reason views are kept at all.
            if (reused && Intent is RouteIntent.DrillIn or RouteIntent.Reader)
            {
                ScrollToStart(incoming);
            }
        }

        if (outgoing is null)
        {
            if (incoming is not null)
            {
                SetRest(incoming);
            }

            return;
        }

        SetInteractive(outgoing, false);
        _leaving = outgoing;

        var travel = MotionPolicy.Resolve(MotionRole.Travel);
        if (!travel.Enabled || !this.IsAttachedToVisualTree())
        {
            // Instant swap: hide the old screen now (hiding never detaches), remove it a tick later - this may run inside the click that
            // navigated.
            outgoing.IsVisible = false;
            if (incoming is not null)
            {
                SetRest(incoming);
            }

            _pendingRemoval = ScheduleAfter(FinishLeaving, TimeSpan.Zero);
            return;
        }

        Animate(incoming, outgoing, Intent, travel);
        var longest = travel.Fade > travel.Move ? travel.Fade : travel.Move;
        _pendingRemoval = ScheduleAfter(FinishLeaving, longest > travel.Exit ? longest : travel.Exit);
    }

    /// <summary>The start transform an incoming screen settles from, by intent (§3.2). None when Travel may not move.</summary>
    internal static ITransform? StartTransform(RouteIntent intent, bool canMove) => !canMove
        ? null
        : intent switch
        {
            RouteIntent.Lateral => TransformOperations.Parse($"translateY({LateralOffset}px)"),
            RouteIntent.DrillIn => TransformOperations.Parse($"scale({DepthScale})"),
            _ => null,
        };

    private void Animate(Border? incoming, Border outgoing, RouteIntent intent, MotionSpec travel)
    {
        var enter = Curve("PbMotionEase");
        var exit = Curve("PbMotionExitEase");

        if (incoming is not null)
        {
            Freeze(incoming);
            incoming.Opacity = 0;
            incoming.RenderTransform = StartTransform(intent, travel.Move > TimeSpan.Zero) ?? incoming.RenderTransform;
            incoming.Transitions = LayerTransitions(travel.Fade, travel.Move, enter);
            incoming.Opacity = 1;
            incoming.RenderTransform = TransformOperations.Identity;
        }

        // Retarget the leaving screen from wherever it is now (it may itself have been arriving a moment ago).
        Freeze(outgoing);
        outgoing.Transitions = LayerTransitions(travel.Exit, travel.Exit, exit);
        outgoing.Opacity = 0;
        if (intent == RouteIntent.Back && travel.Move > TimeSpan.Zero)
        {
            outgoing.RenderTransform = TransformOperations.Parse($"scale({DepthScale})");
        }
    }

    /// <summary>Pins the layer's current - possibly mid-animation - opacity and transform as local values with no transitions, so the
    /// next transition starts from what is on screen instead of jumping to the previous target.</summary>
    private static void Freeze(Border layer)
    {
        double opacity = layer.Opacity;
        var transform = layer.RenderTransform;
        layer.Transitions = null;
        layer.Opacity = opacity;
        layer.RenderTransform = transform;
    }

    private static Transitions LayerTransitions(TimeSpan fade, TimeSpan move, Easing easing) =>
    [
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = fade, Easing = easing },
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = move, Easing = easing },
    ];

    private static Easing Curve(string key) =>
        Application.Current is { } app && app.TryGetResource(key, null, out var value) && value is Easing easing ? easing : new CubicEaseOut();

    private void FinishLeaving()
    {
        _pendingRemoval?.Dispose();
        _pendingRemoval = null;

        if (_leaving is { } leaving && !ReferenceEquals(leaving, _current))
        {
            Children.Remove(leaving);
            if (leaving.DataContext is ITransientScreen && leaving.DataContext is { } key)
            {
                _layers.Remove(key);
                _built.Remove(key);
            }

            SetRest(leaving);
            SetInteractive(leaving, true);
            leaving.IsVisible = true;
        }

        _leaving = null;
    }

    private Border LayerFor(object content)
    {
        if (_layers.TryGetValue(content, out var layer))
        {
            return layer;
        }

        var view = this.FindDataTemplate(content)?.Build(content) ?? new TextBlock { Text = content.ToString() };
        layer = new Border
        {
            Classes = { "routeLayer" },
            Child = view,
            DataContext = content,
            RenderTransformOrigin = RelativePoint.Center,
        };
        _layers[content] = layer;
        return layer;
    }

    internal static void ScrollToStart(Control layer)
    {
        foreach (var viewer in layer.GetVisualDescendants().OfType<ScrollViewer>())
        {
            viewer.Offset = default;
        }
    }

    private static void SetRest(Border layer)
    {
        layer.Transitions = null;
        layer.Opacity = 1;
        layer.RenderTransform = null;
    }

    private static void SetInteractive(Border layer, bool interactive)
    {
        layer.IsHitTestVisible = interactive;
        KeyboardNavigation.SetTabNavigation(layer, interactive ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);
    }
}
