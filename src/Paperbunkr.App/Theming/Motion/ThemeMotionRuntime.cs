using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Theming.Motion;

/// <summary>
/// Plays a theme's declared motions (design doc section 9). Everything runs as a composition animation on the element's own visual - that
/// is, on the render thread - so there is no per-frame timer on the UI thread and no keyframe animation of <c>RenderTransform</c> (which
/// crashes in this framework). A motion can only change what <see cref="ThemeMotionDefinition"/> allows: opacity, translation, scale and
/// rotation of the element it is attached to.
///
/// Motion is decoration, so it is the first thing to go: Reduce motion and the Performance quality tier play nothing, the Balanced tier plays
/// finite motions only, and a continuous motion also stops while the battery is low. Whatever is skipped, the element is simply shown at rest.
/// </summary>
public static class ThemeMotionRuntime
{
    private const int MaxChain = 8;

    private static Dictionary<string, ThemeMotionDefinition> s_definitions = new(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<Control, ThemeMotionDefinition> s_exitMotions = new();
    private static ThemeEffectQuality s_quality = ThemeEffectQuality.High;
    private static Paperbunkr.Data.Entities.MotionPreferenceMode s_preference = Paperbunkr.Data.Entities.MotionPreferenceMode.System;

    /// <summary>Findings from the active theme's motion section (a refused motion, an unknown setting).</summary>
    public static IReadOnlyList<ThemeDiagnostic> Diagnostics { get; private set; } = Array.Empty<ThemeDiagnostic>();

    /// <summary>Every motion actually started, newest last - for tests. Bounded.</summary>
    public static List<string> Started { get; } = new();

    /// <summary>Test seam: answers "is the battery low" without the Win32 call.</summary>
    internal static Func<bool> IsBatteryLow { get; set; } = BatteryStatusInterop.IsThrottled;

    /// <summary>Loads the active theme's motions and the current policy. Called on every theme apply.</summary>
    public static void Configure(IReadOnlyDictionary<string, JsonObject> motions, ThemeEffectQuality quality, bool reducedMotion) =>
        Configure(motions, quality, reducedMotion
            ? Paperbunkr.Data.Entities.MotionPreferenceMode.Reduced
            : Paperbunkr.Data.Entities.MotionPreferenceMode.Full);

    public static void Configure(IReadOnlyDictionary<string, JsonObject> motions, ThemeEffectQuality quality,
        Paperbunkr.Data.Entities.MotionPreferenceMode preference)
    {
        var diagnostics = new List<ThemeDiagnostic>();
        var definitions = new Dictionary<string, ThemeMotionDefinition>(StringComparer.Ordinal);
        foreach (var (name, source) in motions)
        {
            if (ThemeMotionDefinition.Parse(name, source, diagnostics) is { } definition)
            {
                definitions[name] = definition;
            }
        }

        foreach (var definition in definitions.Values.Where(d => d.Then is not null && !definitions.ContainsKey(d.Then!) && !ThemeMotionDefinition.Presets.ContainsKey(d.Then!)))
        {
            diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Warning, ThemeDiagnosticCodes.InvalidMotion, $"Motion '{definition.Name}' continues with '{definition.Then}', which is not defined; it just ends.", $"motion.{definition.Name}.then"));
        }

        s_definitions = definitions;
        s_quality = quality;
        MotionPolicy.SetEffectQuality(quality);
        s_preference = preference;
        MotionPolicy.SetPreference(preference);
        Diagnostics = diagnostics;
    }

    /// <summary>Tells the runtime that Reduce motion changed, without waiting for the next theme apply.</summary>
    public static void SetReducedMotion(bool reducedMotion)
    {
        s_preference = reducedMotion
            ? Paperbunkr.Data.Entities.MotionPreferenceMode.Reduced
            : Paperbunkr.Data.Entities.MotionPreferenceMode.Full;
        MotionPolicy.SetPreference(s_preference);
    }

    public static void SetPreference(Paperbunkr.Data.Entities.MotionPreferenceMode preference)
    {
        s_preference = preference;
        MotionPolicy.SetPreference(preference);
    }

    /// <summary>The motion a name refers to: the theme's own, else a built-in preset, else null.</summary>
    public static ThemeMotionDefinition? Find(string name) =>
        s_definitions.TryGetValue(name, out var definition) ? definition : ThemeMotionDefinition.Presets.GetValueOrDefault(name);

    /// <summary>Whether the current policy lets this motion play at all.</summary>
    public static bool WouldPlay(ThemeMotionDefinition definition)
    {
        // Theme motions animate transforms, so a role must be allowed to move (spec §4.2): continuous motions are Ambient decoration;
        // hover/focus/pressed/selected are Hover feedback; load/enter/exit play as a Reveal.
        var mode = MotionPolicy.Effective(s_preference);
        if (definition.IsContinuous)
        {
            return MotionPolicy.AllowsContinuous(mode, s_quality, IsBatteryLow());
        }

        var role = definition.Trigger is ThemeMotionTrigger.Hover or ThemeMotionTrigger.Focus or ThemeMotionTrigger.Pressed or ThemeMotionTrigger.Selected
            ? MotionRole.Hover
            : MotionRole.Reveal;
        return MotionPolicy.Resolve(role, mode, s_quality).AllowsTransform;
    }

    /// <summary>
    /// Attaches the named motion to a control: it plays when the control appears, or on hover / focus, according to the motion's trigger.
    /// An unknown name, or a motion the policy disallows, does nothing - the control is shown at rest.
    /// </summary>
    public static void Play(Control control, string name)
    {
        if (Find(name) is not { } definition)
        {
            return;
        }

        switch (definition.Trigger)
        {
            case ThemeMotionTrigger.Hover:
                control.PointerEntered += (_, _) => Start(control, definition, 0);
                break;
            case ThemeMotionTrigger.Focus:
                control.GotFocus += (_, _) => Start(control, definition, 0);
                break;
            case ThemeMotionTrigger.Pressed:
                control.PointerPressed += (_, _) => Start(control, definition, 0);
                break;
            case ThemeMotionTrigger.Selected:
                control.PropertyChanged += (_, e) =>
                {
                    if ((e.Property.Name is "IsSelected" or "IsChecked") && Equals(e.NewValue, true))
                    {
                        Start(control, definition, 0);
                    }
                };
                break;
            case ThemeMotionTrigger.Exit:
                s_exitMotions.Remove(control);
                s_exitMotions.Add(control, definition);
                break;
            case ThemeMotionTrigger.Enter:
                PlayWhenAttached(control, definition);
                control.PropertyChanged += (_, e) =>
                {
                    if (e.Property.Name == "IsVisible" && Equals(e.NewValue, true))
                    {
                        Start(control, definition, 0);
                    }
                };
                break;
            default:
                PlayWhenAttached(control, definition);
                break;
        }
    }

    /// <summary>Starts a registered exit motion before the caller hides or removes the control.</summary>
    public static TimeSpan PlayExit(Control control)
    {
        if (!s_exitMotions.TryGetValue(control, out var definition) || !WouldPlay(definition) || !control.IsAttachedToVisualTree())
        {
            return TimeSpan.Zero;
        }

        Start(control, definition, 0);
        return definition.Delay + TimeSpan.FromTicks(definition.Duration.Ticks * definition.Iterations);
    }

    private static void PlayWhenAttached(Control control, ThemeMotionDefinition definition)
    {
        if (control.IsAttachedToVisualTree())
        {
            Start(control, definition, 0);
            return;
        }

        void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            control.AttachedToVisualTree -= OnAttached;
            Dispatcher.UIThread.Post(() => Start(control, definition, 0), DispatcherPriority.Loaded);
        }

        control.AttachedToVisualTree += OnAttached;
    }

    private static void Start(Control control, ThemeMotionDefinition definition, int chain)
    {
        if (!WouldPlay(definition) || !control.IsAttachedToVisualTree())
        {
            return;
        }

        try
        {
            if (ElementComposition.GetElementVisual(control) is not { } visual)
            {
                return;
            }

            var compositor = visual.Compositor;
            var easing = EasingFor(definition.Easing);

            void Configure(KeyFrameAnimation animation)
            {
                animation.Duration = definition.Duration;
                animation.DelayTime = definition.Delay;
                if (definition.IsContinuous)
                {
                    animation.IterationBehavior = AnimationIterationBehavior.Forever;
                }
                else
                {
                    animation.IterationBehavior = AnimationIterationBehavior.Count;
                    animation.IterationCount = definition.Iterations;
                }
            }

            if (definition.AnimatesOpacity)
            {
                var animation = compositor.CreateScalarKeyFrameAnimation();
                foreach (var frame in definition.Keyframes)
                {
                    animation.InsertKeyFrame((float)frame.At, (float)(frame.Opacity ?? 1), easing);
                }

                Configure(animation);
                visual.StartAnimation("Opacity", animation);
            }

            if (definition.AnimatesScale || definition.AnimatesRotation)
            {
                // Scale and rotation turn about the element's centre, as a designer expects.
                visual.CenterPoint = new Vector3D(control.Bounds.Width / 2, control.Bounds.Height / 2, 0);
            }

            if (definition.AnimatesScale)
            {
                var animation = compositor.CreateVector3DKeyFrameAnimation();
                foreach (var frame in definition.Keyframes)
                {
                    double scale = frame.Scale ?? 1;
                    animation.InsertKeyFrame((float)frame.At, new Vector3D(scale, scale, 1), easing);
                }

                Configure(animation);
                visual.StartAnimation("Scale", animation);
            }

            if (definition.AnimatesRotation)
            {
                var animation = compositor.CreateScalarKeyFrameAnimation();
                foreach (var frame in definition.Keyframes)
                {
                    animation.InsertKeyFrame((float)frame.At, (float)((frame.Rotate ?? 0) * Math.PI / 180), easing);
                }

                Configure(animation);
                visual.StartAnimation("RotationAngle", animation);
            }

            if (definition.AnimatesTranslation)
            {
                // Composition offsets are absolute, so the timeline is laid over where layout put the element. A motion that moves an
                // element is always finite and ends at rest (the definition guarantees both), so it finishes exactly where it started.
                var origin = visual.Offset;
                var animation = compositor.CreateVector3DKeyFrameAnimation();
                foreach (var frame in definition.Keyframes)
                {
                    animation.InsertKeyFrame((float)frame.At, new Vector3D(origin.X + (frame.TranslateX ?? 0), origin.Y + (frame.TranslateY ?? 0), origin.Z), easing);
                }

                Configure(animation);
                visual.StartAnimation("Offset", animation);
            }

            Record(definition.Name);

            if (definition.Then is { } next && !definition.IsContinuous && chain < MaxChain && Find(next) is { } following)
            {
                // One timer tick at the end of this motion, not a timer driving frames.
                var wait = definition.Delay + TimeSpan.FromTicks(definition.Duration.Ticks * definition.Iterations);
                DispatcherTimer.RunOnce(() => Start(control, following, chain + 1), wait);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // A motion is never worth a broken screen: the element stays at rest.
        }
    }

    private static void Record(string name)
    {
        if (Started.Count >= 256)
        {
            Started.RemoveRange(0, 128);
        }

        Started.Add(name);
    }

    private static Easing EasingFor(string name) => name.ToLowerInvariant() switch
    {
        "linear" => new LinearEasing(),
        "cubicin" => new CubicEaseIn(),
        "cubicinout" => new CubicEaseInOut(),
        "quadout" => new QuadraticEaseOut(),
        "quadinout" => new QuadraticEaseInOut(),
        "sineinout" => new SineEaseInOut(),
        // Motion v2 bans overshoot-and-return curves; an existing theme's "backout" keeps working as the damped spring, and "spring"
        // names it directly.
        "backout" or "spring" => new DampedSpringEasing(),
        "expoout" => new ExponentialEaseOut(),
        _ => new CubicEaseOut(),
    };
}
