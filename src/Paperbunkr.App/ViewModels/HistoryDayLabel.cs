using System;
using System.Globalization;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Day-header text for the Insights History tab (docs/superpowers/specs/2026-09-29-insights-reading-history-
/// design.md Q9): TODAY, YESTERDAY, the weekday within the last week, then SEP 14 - with the year only when it
/// isn't the current one. Invariant culture, matching the rest of the app's English-only UI strings.
/// </summary>
public static class HistoryDayLabel
{
    public static string For(DateTime localDate, DateTime localToday)
    {
        int daysAgo = (localToday.Date - localDate.Date).Days;
        if (daysAgo <= 0)
        {
            return "TODAY";
        }

        if (daysAgo == 1)
        {
            return "YESTERDAY";
        }

        if (daysAgo < 7)
        {
            return localDate.ToString("dddd", CultureInfo.InvariantCulture).ToUpperInvariant();
        }

        string format = localDate.Year == localToday.Year ? "MMM d" : "MMM d, yyyy";
        return localDate.ToString(format, CultureInfo.InvariantCulture).ToUpperInvariant();
    }
}
