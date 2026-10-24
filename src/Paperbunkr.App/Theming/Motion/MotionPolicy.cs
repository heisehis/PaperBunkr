using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Paperbunkr.App.Theming;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Theming.Motion;

/// <summary>
/// What an animation is for (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md §3.1). Every app-owned
/// animation belongs to exactly one role; the role fixes its timing, curve, travel and how it degrades.
/// </summary>
public enum MotionRole
{
    /// <summary>Press, toggle, checkbox, selection colour.</summary>
    Acknowledge,

    /// <summary>Hover colour and card/cover lift.</summary>
    Hover,

    /// <summary>Dialogs, drawers, toasts, popups, menus.</summary>
    Reveal,

    /// <summary>Tab content, result sets, view-mode swaps, expanders.</summary>
    Replace,

    /// <summary>Routes, the cover flight, reader page turns, smooth scrolling.</summary>
    Travel,

    /// <summary>Backdrop crossfades, Spotlight choreography and continuous decoration.</summary>
    Ambient,
}

/// <summary>The motion mode actually in force once System has been resolved against the platform.</summary>
public enum EffectiveMotion
{
    Full,
    Reduced,
    Off,
}

/// <summary>
/// One role's resolved timing. <see cref="Fade"/> drives opacity, <see cref="Move"/> drives render transforms, <see cref="Exit"/> drives
/// the leaving half. Reduced keeps a short fade and zeroes the move, so a surface appears as a fade with no travel without any style
/// needing to know the mode (spec §3.4).
/// </summary>
public readonly record struct MotionSpec(TimeSpan Fade, TimeSpan Move, TimeSpan Exit, bool AllowsTransform)
{
    public static readonly MotionSpec None = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, false);

    public bool Enabled => Fade > TimeSpan.Zero || Move > TimeSpan.Zero;
}

/// <summary>Resolves user, platform, quality, battery and the dev time scale into one decision per motion role (spec §4.2).</summary>
public static class MotionPolicy
{
    /// <summary>Environment variable that multiplies every duration, for tuning motion by eye (spec §4.4). Dev tool, not a setting.</summary>
    public const string ScaleEnvironmentVariable = "PB_MOTION_SCALE";

    private static bool? s_systemAnimationsEnabled;
    private static MotionPreferenceMode s_preference = MotionPreferenceMode.System;
    private static ThemeEffectQuality s_quality = ThemeEffectQuality.High;
    private static double s_scale = ReadScale(Environment.GetEnvironmentVariable(ScaleEnvironmentVariable));

    public static bool IsSystemPreferenceAvailable => s_systemAnimationsEnabled.HasValue;

    public static bool? SystemAnimationsEnabled => s_systemAnimationsEnabled;

    public static MotionPreferenceMode Preference => s_preference;

    public static ThemeEffectQuality EffectQuality => s_quality;

    /// <summary>The <see cref="ScaleEnvironmentVariable"/> multiplier in force (1 unless set).</summary>
    public static double TimeScale => s_scale;

    internal static void SetSystemPreferenceForTests(bool? animationsEnabled) => s_systemAnimationsEnabled = animationsEnabled;

    internal static void SetTimeScaleForTests(double scale) => s_scale = scale;

    public static bool RefreshSystemPreference()
    {
        bool? previous = s_systemAnimationsEnabled;
        s_systemAnimationsEnabled = SystemMotionPreference.TryGetAnimationsEnabled(out bool enabled) ? enabled : null;
        return previous != s_systemAnimationsEnabled;
    }

    public static void SetPreference(MotionPreferenceMode preference) => s_preference = preference;

    public static void SetEffectQuality(ThemeEffectQuality quality) => s_quality = quality;

    /// <summary>Parses <see cref="ScaleEnvironmentVariable"/>: a positive number clamped to 0.1–10; anything else is 1.</summary>
    internal static double ReadScale(string? raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0 && double.IsFinite(value)
            ? Math.Clamp(value, 0.1, 10)
            : 1;

    public static EffectiveMotion Effective(MotionPreferenceMode preference) => Effective(preference, s_systemAnimationsEnabled);

    internal static EffectiveMotion Effective(MotionPreferenceMode preference, bool? systemAnimationsEnabled) => preference switch
    {
        MotionPreferenceMode.Full => EffectiveMotion.Full,
        MotionPreferenceMode.Reduced => EffectiveMotion.Reduced,
        MotionPreferenceMode.Off => EffectiveMotion.Off,
        _ => systemAnimationsEnabled == false ? EffectiveMotion.Reduced : EffectiveMotion.Full,
    };

    /// <summary>True for Reduced and Off: anything that moves (travel, scale, flight, stagger, ambient) stays still.</summary>
    public static bool IsReducedMotion(MotionPreferenceMode preference) => Effective(preference) != EffectiveMotion.Full;

    internal static bool IsReducedMotion(MotionPreferenceMode preference, bool? systemAnimationsEnabled) =>
        Effective(preference, systemAnimationsEnabled) != EffectiveMotion.Full;

    /// <summary>The current policy's timing for <paramref name="role"/>.</summary>
    public static MotionSpec Resolve(MotionRole role) => Resolve(role, Effective(s_preference), s_quality, s_scale);

    internal static MotionSpec Resolve(MotionRole role, EffectiveMotion mode, ThemeEffectQuality quality, double scale = 1)
    {
        if (mode == EffectiveMotion.Off)
        {
            return MotionSpec.None;
        }

        // Performance keeps the cheap opacity/transform roles and drops the costly ones: Travel degrades to its Reduced form
        // (a crossfade, no travel), Ambient to nothing (spec §4.2, Q7).
        if (mode == EffectiveMotion.Full && quality == ThemeEffectQuality.Performance)
        {
            if (role == MotionRole.Travel)
            {
                mode = EffectiveMotion.Reduced;
            }
            else if (role == MotionRole.Ambient)
            {
                return MotionSpec.None;
            }
        }

        var spec = mode == EffectiveMotion.Full ? FullSpec(role) : ReducedSpec(role);
        return scale == 1 ? spec : spec with { Fade = Scale(spec.Fade, scale), Move = Scale(spec.Move, scale), Exit = Scale(spec.Exit, scale) };
    }

    /// <summary>The cover flight (spec §3.4 <c>PbMotionFlight</c>): 280 ms when Travel is allowed to move, else none.</summary>
    public static TimeSpan FlightDuration() => FlightDuration(Effective(s_preference), s_quality, s_scale);

    internal static TimeSpan FlightDuration(EffectiveMotion mode, ThemeEffectQuality quality, double scale = 1) =>
        Resolve(MotionRole.Travel, mode, quality).AllowsTransform ? Scale(Ms(280), scale) : TimeSpan.Zero;

    /// <summary>Whether <paramref name="role"/> may move things (travel, scale, height, stagger) right now. Code-driven motion asks this.</summary>
    public static bool AllowsMotion(MotionRole role) => Resolve(role).AllowsTransform;

    /// <summary>Continuous Ambient decoration (loops, rain, parallax): Full motion, High quality, and not on a low battery (spec §4.2).</summary>
    public static bool AllowsContinuous(bool batteryLow = false) => AllowsContinuous(Effective(s_preference), s_quality, batteryLow);

    internal static bool AllowsContinuous(EffectiveMotion mode, ThemeEffectQuality quality, bool batteryLow) =>
        mode == EffectiveMotion.Full && quality == ThemeEffectQuality.High && !batteryLow;

    private static MotionSpec FullSpec(MotionRole role) => role switch
    {
        MotionRole.Acknowledge => new(Ms(90), Ms(90), Ms(90), true),
        MotionRole.Hover => new(Ms(120), Ms(160), Ms(120), true),
        MotionRole.Reveal => new(Ms(180), Ms(180), Ms(120), true),
        MotionRole.Replace => new(Ms(160), TimeSpan.Zero, Ms(160), true),
        MotionRole.Travel => new(Ms(220), Ms(220), Ms(140), true),
        MotionRole.Ambient => new(Ms(360), Ms(360), Ms(360), true),
        _ => MotionSpec.None,
    };

    private static MotionSpec ReducedSpec(MotionRole role) => role switch
    {
        MotionRole.Acknowledge => new(Ms(90), TimeSpan.Zero, Ms(90), false),
        MotionRole.Hover => new(Ms(100), TimeSpan.Zero, Ms(100), false),
        MotionRole.Reveal => new(Ms(100), TimeSpan.Zero, Ms(80), false),
        MotionRole.Replace => new(Ms(100), TimeSpan.Zero, Ms(100), false),
        MotionRole.Travel => new(Ms(120), TimeSpan.Zero, Ms(120), false),
        _ => MotionSpec.None,
    };

    private static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private static TimeSpan Scale(TimeSpan value, double scale) => TimeSpan.FromTicks((long)(value.Ticks * scale));
}

/// <summary>Reads the Windows client-area animation accessibility preference. Other platforms report unavailable.</summary>
internal static class SystemMotionPreference
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public static bool TryGetAnimationsEnabled(out bool enabled)
    {
        enabled = true;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return SystemParametersInfo(SpiGetClientAreaAnimation, 0, out enabled, 0);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action,
        uint uiParam,
        [MarshalAs(UnmanagedType.Bool)] out bool pvParam,
        uint fWinIni);
}
