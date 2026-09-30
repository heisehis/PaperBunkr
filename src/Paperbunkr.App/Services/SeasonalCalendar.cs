using System;

namespace Paperbunkr.App.Services;

public enum Season
{
    FreeComicBookDay,
    Halloween,
    WinterHolidays,
    NewYear,
}

/// <summary>
/// The Home masthead's opt-in seasonal flourish dates (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md C10). Pure. The
/// windows don't overlap, but the order below is also the priority if they ever do.
/// </summary>
public static class SeasonalCalendar
{
    public static Season? ActiveOn(DateOnly date)
    {
        if (date == FreeComicBookDay(date.Year))
        {
            return Season.FreeComicBookDay;
        }

        if (date.Month == 10 && date.Day >= 24)
        {
            return Season.Halloween;
        }

        if (date.Month == 12 && date.Day >= 15)
        {
            return Season.WinterHolidays;
        }

        if (date.Month == 1 && date.Day <= 3)
        {
            return Season.NewYear;
        }

        return null;
    }

    /// <summary>Free Comic Book Day: the first Saturday of May.</summary>
    public static DateOnly FreeComicBookDay(int year)
    {
        var first = new DateOnly(year, 5, 1);
        int offset = ((int)DayOfWeek.Saturday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset);
    }

    /// <summary>FluentIcons symbol name shown after the wordmark.</summary>
    public static FluentIcons.Common.Symbol Icon(Season season) => season switch
    {
        Season.FreeComicBookDay => FluentIcons.Common.Symbol.Gift,
        Season.Halloween => FluentIcons.Common.Symbol.WeatherMoon,
        Season.WinterHolidays => FluentIcons.Common.Symbol.WeatherSnowflake,
        _ => FluentIcons.Common.Symbol.Star,
    };

    /// <summary>The <c>App.axaml</c> colour resource key for the season's greeting tint.</summary>
    public static string TintResourceKey(Season season) => $"PbSeason{season}Color";

    public static string Label(Season season) => season switch
    {
        Season.FreeComicBookDay => "Free Comic Book Day",
        Season.Halloween => "Halloween",
        Season.WinterHolidays => "Happy holidays",
        _ => "Happy New Year",
    };
}
