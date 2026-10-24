using System;
using Avalonia;
using Paperbunkr.App.Theming.Motion;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Code-behind access to the motion durations, for consumers that need a duration outside XAML's <c>{DynamicResource}</c> binding
/// (<c>DetailHero</c>'s parallax, <c>ToastPresenter</c>'s removal delay, <c>AnimatedStackPanel</c>'s reflow gating). Values come
/// straight from <see cref="MotionPolicy.Resolve"/>, the same source <c>ThemeService.WriteMotionTokens</c> writes the XAML tokens from,
/// so a live Motion or quality change is reflected on the next read (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-
/// overlays-design.md §3.4).
/// </summary>
public static class MotionTokens
{
    /// <summary>Legacy alias: the Hover fade.</summary>
    public static TimeSpan GetFast() => MotionPolicy.Resolve(MotionRole.Hover).Fade;

    /// <summary>Legacy alias: the Reveal fade.</summary>
    public static TimeSpan GetStandard() => MotionPolicy.Resolve(MotionRole.Reveal).Fade;

    /// <summary>Legacy alias: the cover flight.</summary>
    public static TimeSpan GetLarge() => MotionPolicy.FlightDuration();

    public static MotionSpec Get(MotionRole role) => MotionPolicy.Resolve(role);

    public static TimeSpan GetRevealExit() => MotionPolicy.Resolve(MotionRole.Reveal).Exit;

    public static bool IsReducedMotion() => MotionPolicy.IsReducedMotion(MotionPolicy.Preference);
}
