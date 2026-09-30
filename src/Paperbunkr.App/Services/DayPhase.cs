using System;

namespace Paperbunkr.App.Services;

public enum DayPhaseKind
{
    Morning,
    Afternoon,
    Evening,
    Night,
}

/// <summary>
/// Time-of-day phase for the Home greeting and light-theme sky (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md C2).
/// Pure, local time: morning 05:00-11:59, afternoon 12:00-16:59, evening 17:00-20:59, night 21:00-04:59.
/// </summary>
public static class DayPhase
{
    public static DayPhaseKind For(DateTime local) => local.Hour switch
    {
        >= 5 and < 12 => DayPhaseKind.Morning,
        >= 12 and < 17 => DayPhaseKind.Afternoon,
        >= 17 and < 21 => DayPhaseKind.Evening,
        _ => DayPhaseKind.Night,
    };

    public static string Greeting(DayPhaseKind phase) => phase switch
    {
        DayPhaseKind.Morning => "Good morning",
        DayPhaseKind.Afternoon => "Good afternoon",
        DayPhaseKind.Evening => "Good evening",
        _ => "Late-night reading?",
    };

    /// <summary>The next moment the phase changes after <paramref name="local"/> - what the Home refresh timer waits for.</summary>
    public static DateTime NextBoundary(DateTime local)
    {
        int[] boundaries = { 5, 12, 17, 21 };
        foreach (int hour in boundaries)
        {
            var candidate = local.Date.AddHours(hour);
            if (candidate > local)
            {
                return candidate;
            }
        }

        return local.Date.AddDays(1).AddHours(5);
    }
}
