using System;
using Avalonia;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Code-behind access to the live PbMotion* resources (docs/superpowers/specs/2026-09-07-chrome-
/// content-motion-polish-design.md) for the handful of consumers that need a duration or a reduced-
/// motion check outside XAML's own <c>{DynamicResource}</c> binding - <c>DetailHero</c>'s parallax
/// scroll handler, <c>PbToastHost</c>'s post-exit removal delay, <c>AnimatedStackPanel</c>'s reflow
/// gating. Reads directly from <see cref="Application.Resources"/>, the same dictionary
/// <c>SkinService.ApplyReducedMotion</c> writes to - not a design-time value or a cached copy, so
/// toggling Reduced Motion live is always reflected on the next read.
/// </summary>
public static class MotionTokens
{
    public static TimeSpan GetStandard() => Get("PbMotionStandard", TimeSpan.FromMilliseconds(220));

    public static bool IsReducedMotion() => GetStandard() == TimeSpan.Zero;

    private static TimeSpan Get(string key, TimeSpan fallback) =>
        Application.Current!.Resources.TryGetValue(key, out var resource) && resource is TimeSpan value
            ? value
            : fallback;
}
