using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Paperbunkr.App.Theming.Motion;

/// <summary>When a motion plays.</summary>
public enum ThemeMotionTrigger
{
    /// <summary>When the element appears (a layout is shown, a screen is navigated to).</summary>
    Load,
    Hover,
    Focus,
    Pressed,
    Selected,
    Enter,
    Exit,
}

/// <summary>One keyframe: a position in the timeline (0..1) and the value of each animated property there. A property left out keeps its resting value.</summary>
public sealed record ThemeMotionKeyframe(double At, double? Opacity, double? TranslateX, double? TranslateY, double? Scale, double? Rotate);

/// <summary>
/// A validated, named motion from a theme (design doc section 9.1): a timeline of keyframes over a closed set of properties - opacity,
/// translation, uniform scale and rotation - with a duration, delay, easing and iteration count. There is no way to name an arbitrary
/// property: anything outside this set refuses the motion, so a theme can never animate layout, colour resources or framework internals.
/// </summary>
public sealed record ThemeMotionDefinition(
    string Name,
    ThemeMotionTrigger Trigger,
    TimeSpan Duration,
    TimeSpan Delay,
    string Easing,
    int Iterations,
    bool IsContinuous,
    IReadOnlyList<ThemeMotionKeyframe> Keyframes,
    string? Then)
{
    public const int MinDurationMs = 40;
    public const int MaxDurationMs = 10000;
    public const int MaxExitDurationMs = 600;
    public const int MaxDelayMs = 5000;
    public const int MaxKeyframes = 16;
    public const int MaxIterations = 20;

    public static readonly IReadOnlyList<string> Easings = new[]
    {
        "linear", "cubicIn", "cubicOut", "cubicInOut", "quadOut", "quadInOut", "sineInOut", "backOut", "spring", "expoOut",
    };

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal) { "trigger", "duration", "delay", "easing", "iterations", "keyframes", "then" };

    private static readonly HashSet<string> KeyframeKeys = new(StringComparer.Ordinal) { "at", "opacity", "translateX", "translateY", "scale", "rotate" };

    public bool AnimatesOpacity => Keyframes.Any(k => k.Opacity is not null);

    public bool AnimatesTranslation => Keyframes.Any(k => k.TranslateX is not null || k.TranslateY is not null);

    public bool AnimatesScale => Keyframes.Any(k => k.Scale is not null);

    public bool AnimatesRotation => Keyframes.Any(k => k.Rotate is not null);

    /// <summary>Motions an author can use by name without defining them. A theme may redefine any of these.</summary>
    public static IReadOnlyDictionary<string, ThemeMotionDefinition> Presets { get; } = new Dictionary<string, ThemeMotionDefinition>(StringComparer.Ordinal)
    {
        ["fadeIn"] = Preset("fadeIn", 220, new(0, 0, null, null, null, null), new(1, 1, null, null, null, null)),
        ["fadeUp"] = Preset("fadeUp", 320, new(0, 0, null, 14, null, null), new(1, 1, null, 0, null, null)),
        ["scaleIn"] = Preset("scaleIn", 260, new(0, 0, null, null, 0.96, null), new(1, 1, null, null, 1, null)),
    };

    private static ThemeMotionDefinition Preset(string name, int ms, params ThemeMotionKeyframe[] keyframes) =>
        new(name, ThemeMotionTrigger.Load, TimeSpan.FromMilliseconds(ms), TimeSpan.Zero, "cubicOut", 1, false, keyframes, null);

    /// <summary>Null when the definition cannot be used; <paramref name="diagnostics"/> then says why. Never throws.</summary>
    public static ThemeMotionDefinition? Parse(string name, JsonObject source, List<ThemeDiagnostic> diagnostics)
    {
        string path = $"motion.{name}";
        ThemeMotionDefinition? Refuse(string message)
        {
            diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.InvalidMotion, $"Motion '{name}' {message}; it is not used.", path, FallbackUsed: true));
            return null;
        }

        foreach (var (key, _) in source)
        {
            if (!KnownKeys.Contains(key))
            {
                return Refuse($"has an unknown setting '{key}'");
            }
        }

        var trigger = ThemeMotionTrigger.Load;
        if (source["trigger"] is JsonValue triggerValue && triggerValue.TryGetValue(out string? triggerText) && triggerText is not null)
        {
            switch (triggerText.ToLowerInvariant())
            {
                case "load":
                case "navigation":
                    trigger = ThemeMotionTrigger.Load;
                    break;
                case "hover":
                    trigger = ThemeMotionTrigger.Hover;
                    break;
                case "focus":
                    trigger = ThemeMotionTrigger.Focus;
                    break;
                case "pressed":
                    trigger = ThemeMotionTrigger.Pressed;
                    break;
                case "selected":
                    trigger = ThemeMotionTrigger.Selected;
                    break;
                case "enter":
                    trigger = ThemeMotionTrigger.Enter;
                    break;
                case "exit":
                    trigger = ThemeMotionTrigger.Exit;
                    break;
                default:
                    return Refuse($"uses the trigger '{triggerText}', which is not one of: load, navigation, hover, focus, pressed, selected, enter, exit");
            }
        }

        double durationMs = source["duration"] is JsonValue d && d.TryGetValue(out double dv) ? dv : 240;
        if (double.IsNaN(durationMs) || durationMs < MinDurationMs || durationMs > MaxDurationMs)
        {
            return Refuse($"needs a duration between {MinDurationMs} and {MaxDurationMs} milliseconds");
        }

        if (trigger == ThemeMotionTrigger.Exit && durationMs > MaxExitDurationMs)
        {
            return Refuse($"uses an exit duration longer than {MaxExitDurationMs} milliseconds");
        }

        double delayMs = source["delay"] is JsonValue dl && dl.TryGetValue(out double dlv) ? dlv : 0;
        if (double.IsNaN(delayMs) || delayMs < 0 || delayMs > MaxDelayMs)
        {
            return Refuse($"needs a delay between 0 and {MaxDelayMs} milliseconds");
        }

        string easing = source["easing"] is JsonValue e && e.TryGetValue(out string? ev) && ev is not null ? ev : "cubicOut";
        if (!Easings.Contains(easing, StringComparer.OrdinalIgnoreCase))
        {
            return Refuse($"uses the easing '{easing}', which is not one of: {string.Join(", ", Easings)}");
        }

        int iterations = 1;
        bool continuous = false;
        if (source["iterations"] is JsonValue iterationsValue)
        {
            if (iterationsValue.TryGetValue(out string? word) && string.Equals(word, "infinite", StringComparison.OrdinalIgnoreCase))
            {
                continuous = true;
            }
            else if (iterationsValue.TryGetValue(out int count) && count is >= 1 and <= MaxIterations)
            {
                iterations = count;
            }
            else
            {
                return Refuse($"needs iterations between 1 and {MaxIterations}, or \"infinite\"");
            }
        }

        if (source["keyframes"] is not JsonArray array || array.Count < 2 || array.Count > MaxKeyframes)
        {
            return Refuse($"needs between 2 and {MaxKeyframes} keyframes");
        }

        var keyframes = new List<ThemeMotionKeyframe>();
        foreach (var node in array)
        {
            if (node is not JsonObject frame)
            {
                return Refuse("has a keyframe that is not an object");
            }

            foreach (var (key, _) in frame)
            {
                if (!KeyframeKeys.Contains(key))
                {
                    return Refuse($"animates '{key}', which is not one of: opacity, translateX, translateY, scale, rotate");
                }
            }

            if (frame["at"] is not JsonValue atValue || !atValue.TryGetValue(out double at) || at < 0 || at > 1)
            {
                return Refuse("has a keyframe without an \"at\" between 0 and 1");
            }

            if (!TryRead(frame, "opacity", 0, 1, out var opacity) || !TryRead(frame, "translateX", -600, 600, out var tx) || !TryRead(frame, "translateY", -600, 600, out var ty)
                || !TryRead(frame, "scale", 0.2, 3, out var scale) || !TryRead(frame, "rotate", -720, 720, out var rotate))
            {
                return Refuse("has a keyframe value out of range (opacity 0 to 1, translate -600 to 600, scale 0.2 to 3, rotate -720 to 720)");
            }

            keyframes.Add(new ThemeMotionKeyframe(at, opacity, tx, ty, scale, rotate));
        }

        if (keyframes[0].At != 0 || keyframes[^1].At != 1 || keyframes.Zip(keyframes.Skip(1)).Any(pair => pair.Second.At <= pair.First.At))
        {
            return Refuse("needs keyframes in order, starting at 0 and ending at 1");
        }

        var definition = new ThemeMotionDefinition(
            name, trigger, TimeSpan.FromMilliseconds(durationMs), TimeSpan.FromMilliseconds(delayMs), easing, iterations, continuous, keyframes,
            source["then"] is JsonValue thenValue && thenValue.TryGetValue(out string? then) && !string.IsNullOrWhiteSpace(then) ? then : null);

        // A motion has to leave the element where the layout put it: fully opaque, unmoved, unscaled, unrotated. Otherwise a finished
        // animation would leave a control half-faded or shifted for good, and a looping one would jump at each repeat.
        var last = keyframes[^1];
        if ((last.Opacity ?? 1) != 1 || (last.TranslateX ?? 0) != 0 || (last.TranslateY ?? 0) != 0 || (last.Scale ?? 1) != 1 || (last.Rotate ?? 0) % 360 != 0)
        {
            return Refuse("must end at rest (opacity 1, no translation, scale 1, no rotation)");
        }

        if (continuous && definition.AnimatesTranslation)
        {
            return Refuse("is continuous and moves the element; a continuous motion may only change opacity, scale and rotation");
        }

        if (continuous && trigger == ThemeMotionTrigger.Exit)
        {
            return Refuse("uses an exit trigger with an infinite duration");
        }

        return definition;
    }

    private static bool TryRead(JsonObject frame, string name, double low, double high, out double? value)
    {
        value = null;
        if (frame[name] is null)
        {
            return true;
        }

        if (frame[name] is not JsonValue node || !node.TryGetValue(out double number) || double.IsNaN(number) || number < low || number > high)
        {
            return false;
        }

        value = number;
        return true;
    }
}
