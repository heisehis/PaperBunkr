using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// The schedule behind the comic reader's warm shift (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md section 4): a warm tint on the pages between a start and an end
/// time of day. Times are minutes after local midnight; the window may wrap midnight (21:00 to 07:00). Pure so it is tested without a clock.
/// </summary>
public static class WarmShiftSchedule
{
    public const int MinutesPerDay = 24 * 60;

    /// <summary>Whether <paramref name="now"/> is inside the window. Start equal to end means never (an empty window, not an all-day one).</summary>
    public static bool IsActive(TimeOnly now, int startMinutes, int endMinutes)
    {
        int start = Normalize(startMinutes);
        int end = Normalize(endMinutes);
        if (start == end)
        {
            return false;
        }

        int minute = (now.Hour * 60) + now.Minute;
        return start < end
            ? minute >= start && minute < end
            : minute >= start || minute < end;   // wraps midnight
    }

    /// <summary>The warmth (0-1) to apply for a strength setting of 0-100.</summary>
    public static double WarmthFor(int strengthPercent) => Math.Clamp(strengthPercent, 0, 100) / 100.0;

    /// <summary>"21:00" for 1260.</summary>
    public static string Format(int minutes)
    {
        int m = Normalize(minutes);
        return $"{m / 60:00}:{m % 60:00}";
    }

    /// <summary>Parses "H:mm" / "HH:mm" (24-hour). Returns false for anything else.</summary>
    public static bool TryParse(string? text, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(text) || !TimeOnly.TryParseExact(text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return false;
        }

        minutes = (time.Hour * 60) + time.Minute;
        return true;
    }

    /// <summary>Every half hour of the day as "HH:mm", for the Preferences pickers.</summary>
    public static IReadOnlyList<string> HalfHourChoices { get; } = Enumerable.Range(0, 48).Select(i => Format(i * 30)).ToArray();

    private static int Normalize(int minutes) => ((minutes % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
}
