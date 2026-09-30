using Avalonia.Media;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Pure helpers behind the 2026-09-28 Home pitch (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md and
/// 2026-09-28-home-improvements-design.md): section layout, time-of-day phase, seasonal calendar, parallax offset and the
/// vibrant accent pick.
/// </summary>
public class HomePitchHelpersTests
{
    // --- HomeLayout (I1) ---

    [Fact]
    public void Layout_NullIsTheDefaultOrder_NothingHidden()
    {
        var layout = HomeLayout.Resolve(null, null);

        Assert.Equal(HomeSectionKey.Default, layout.Order);
        Assert.Equal(HomeSectionKey.Default, layout.Visible);
        Assert.Empty(layout.Hidden);
    }

    [Fact]
    public void Layout_DropsUnknownAndDuplicateKeys_AndAppendsMissingOnesAtTheirDefaultIndex()
    {
        var layout = HomeLayout.Resolve("readingList, bogus ,readingList,recentlyAdded", null);

        // readingList and recentlyAdded keep their saved relative order; every other known key is inserted at its default index.
        Assert.Equal(new[]
        {
            HomeSectionKey.Spotlight, HomeSectionKey.NeedsAttention, HomeSectionKey.ContinueReading, HomeSectionKey.ReadingList,
            HomeSectionKey.Collections, HomeSectionKey.BecauseYouRead, HomeSectionKey.RecentlyAdded,
        }, layout.Order);
        Assert.Equal(7, layout.Order.Distinct().Count());
    }

    [Fact]
    public void Layout_HiddenKeysStayInOrder_ButLeaveVisible()
    {
        var layout = HomeLayout.Resolve(null, "collections,nonsense");

        Assert.Contains(HomeSectionKey.Collections, layout.Order);
        Assert.DoesNotContain(HomeSectionKey.Collections, layout.Visible);
        Assert.Equal(new[] { HomeSectionKey.Collections }, layout.Hidden);
    }

    [Fact]
    public void Layout_SerializesDefaultsBackToNull_AndRoundTrips()
    {
        Assert.Equal((null, null), HomeLayout.Serialize(HomeSectionKey.Default, Array.Empty<string>()));

        var order = HomeSectionKey.Default.Reverse().ToList();
        var (orderCsv, hiddenCsv) = HomeLayout.Serialize(order, new[] { HomeSectionKey.Spotlight });
        var back = HomeLayout.Resolve(orderCsv, hiddenCsv);

        Assert.Equal(order, back.Order);
        Assert.DoesNotContain(HomeSectionKey.Spotlight, back.Visible);
    }

    // --- DayPhase (C2) ---

    [Theory]
    [InlineData(4, 59, DayPhaseKind.Night)]
    [InlineData(5, 0, DayPhaseKind.Morning)]
    [InlineData(11, 59, DayPhaseKind.Morning)]
    [InlineData(12, 0, DayPhaseKind.Afternoon)]
    [InlineData(16, 59, DayPhaseKind.Afternoon)]
    [InlineData(17, 0, DayPhaseKind.Evening)]
    [InlineData(20, 59, DayPhaseKind.Evening)]
    [InlineData(21, 0, DayPhaseKind.Night)]
    [InlineData(0, 0, DayPhaseKind.Night)]
    public void DayPhase_Boundaries(int hour, int minute, DayPhaseKind expected)
        => Assert.Equal(expected, DayPhase.For(new DateTime(2026, 9, 28, hour, minute, 0)));

    [Fact]
    public void DayPhase_GreetingsAndNextBoundary()
    {
        Assert.Equal("Late-night reading?", DayPhase.Greeting(DayPhaseKind.Night));
        Assert.Equal("Good morning", DayPhase.Greeting(DayPhaseKind.Morning));
        Assert.Equal(new DateTime(2026, 9, 28, 17, 0, 0), DayPhase.NextBoundary(new DateTime(2026, 9, 28, 16, 30, 0)));
        Assert.Equal(new DateTime(2026, 9, 29, 5, 0, 0), DayPhase.NextBoundary(new DateTime(2026, 9, 28, 22, 0, 0)));
        Assert.Equal(new DateTime(2026, 9, 28, 5, 0, 0), DayPhase.NextBoundary(new DateTime(2026, 9, 28, 1, 0, 0)));
    }

    // --- SeasonalCalendar (C10) ---

    [Theory]
    [InlineData(2026, 5, 2)]
    [InlineData(2027, 5, 1)]
    [InlineData(2028, 5, 6)]
    [InlineData(2029, 5, 5)]
    [InlineData(2030, 5, 4)]
    public void FreeComicBookDay_IsTheFirstSaturdayOfMay(int year, int month, int day)
    {
        var date = SeasonalCalendar.FreeComicBookDay(year);
        Assert.Equal(new DateOnly(year, month, day), date);
        Assert.Equal(DayOfWeek.Saturday, date.DayOfWeek);
        Assert.Equal(Season.FreeComicBookDay, SeasonalCalendar.ActiveOn(date));
        Assert.Null(SeasonalCalendar.ActiveOn(date.AddDays(1)));
    }

    [Theory]
    [InlineData(10, 23, null)]
    [InlineData(10, 24, Season.Halloween)]
    [InlineData(10, 31, Season.Halloween)]
    [InlineData(11, 1, null)]
    [InlineData(12, 14, null)]
    [InlineData(12, 15, Season.WinterHolidays)]
    [InlineData(12, 31, Season.WinterHolidays)]
    [InlineData(1, 1, Season.NewYear)]
    [InlineData(1, 3, Season.NewYear)]
    [InlineData(1, 4, null)]
    public void Seasons_WindowEdges(int month, int day, Season? expected)
        => Assert.Equal(expected, SeasonalCalendar.ActiveOn(new DateOnly(2026, month, day)));

    // --- ParallaxMath (C9) ---

    [Theory]
    [InlineData(-50, 0)]    // not scrolled past yet
    [InlineData(0, 0)]
    [InlineData(100, 35)]   // 0.35 of the scroll
    [InlineData(1000, 50)]  // clamped at 12.5% of a 400px frame - never runs out of spare image
    public void Parallax_OffsetIsAFractionOfTheScroll_ClampedToTheOverscan(double scrolledPast, double expected)
        => Assert.Equal(expected, ParallaxMath.Offset(scrolledPast, 400), 3);

    [Fact]
    public void Parallax_ZeroHeightFrame_IsZero() => Assert.Equal(0, ParallaxMath.Offset(100, 0));

    // --- SpotlightAccentSampler.PickVibrant (C1) ---

    [Fact]
    public void Vibrant_ASaturatedPatchBeatsAGreyMajority()
    {
        var pixels = Enumerable.Repeat(Color.FromRgb(0x77, 0x72, 0x6E), 200)          // muddy grey-brown, most of the cover
            .Concat(Enumerable.Repeat(Color.FromRgb(0xD8, 0x2A, 0x2A), 56))          // one strong red block
            .ToList();

        var picked = SpotlightAccentSampler.PickVibrant(pixels);

        Assert.True(picked.R > 0xC0 && picked.G < 0x50 && picked.B < 0x50, $"expected the red, got {picked}");
    }

    [Fact]
    public void Vibrant_AGreyCover_FallsBackToTheAverage()
    {
        var pixels = Enumerable.Repeat(Color.FromRgb(0x60, 0x60, 0x60), 100)
            .Concat(Enumerable.Repeat(Color.FromRgb(0xA0, 0xA0, 0xA0), 100))
            .ToList();

        Assert.Equal(Color.FromRgb(0x80, 0x80, 0x80), SpotlightAccentSampler.PickVibrant(pixels));
    }

    [Fact]
    public void Vibrant_NoPixels_IsTheFallback()
        => Assert.Equal(SpotlightAccentSampler.FallbackColor, SpotlightAccentSampler.PickVibrant(Array.Empty<Color>()));
}
