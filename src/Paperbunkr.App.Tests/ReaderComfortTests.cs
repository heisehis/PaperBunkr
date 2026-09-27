using Avalonia;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using SkiaSharp;

namespace Paperbunkr.App.Tests;

/// <summary>The pure comfort pieces: warm shift schedule and colour, session clock, stats chip text, break nudge policy and spread stitching (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md).</summary>
public class ReaderComfortTests
{
    // --- Warm shift schedule ---

    [Theory]
    [InlineData(20, 59, false)]
    [InlineData(21, 0, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)]      // past midnight, inside the wrapped window
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]     // the end is exclusive
    [InlineData(12, 0, false)]
    public void Schedule_WrapsMidnight(int hour, int minute, bool expected) =>
        Assert.Equal(expected, WarmShiftSchedule.IsActive(new TimeOnly(hour, minute), 21 * 60, 7 * 60));

    [Theory]
    [InlineData(8, 59, false)]
    [InlineData(9, 0, true)]
    [InlineData(16, 59, true)]
    [InlineData(17, 0, false)]
    public void Schedule_SameDayWindow(int hour, int minute, bool expected) =>
        Assert.Equal(expected, WarmShiftSchedule.IsActive(new TimeOnly(hour, minute), 9 * 60, 17 * 60));

    [Fact]
    public void Schedule_StartEqualToEnd_IsNeverActive()
    {
        Assert.False(WarmShiftSchedule.IsActive(new TimeOnly(3, 0), 300, 300));
        Assert.False(WarmShiftSchedule.IsActive(new TimeOnly(12, 0), 0, 0));
    }

    [Theory]
    [InlineData(1260, "21:00")]
    [InlineData(420, "07:00")]
    [InlineData(0, "00:00")]
    [InlineData(1439, "23:59")]
    public void Format_And_TryParse_RoundTrip(int minutes, string text)
    {
        Assert.Equal(text, WarmShiftSchedule.Format(minutes));
        Assert.True(WarmShiftSchedule.TryParse(text, out int parsed));
        Assert.Equal(minutes, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("25:00")]
    [InlineData("nine")]
    [InlineData("9pm")]
    public void TryParse_RejectsNonTimes(string text) => Assert.False(WarmShiftSchedule.TryParse(text, out _));

    [Fact]
    public void HalfHourChoices_CoverTheDay()
    {
        Assert.Equal(48, WarmShiftSchedule.HalfHourChoices.Count);
        Assert.Equal("00:00", WarmShiftSchedule.HalfHourChoices[0]);
        Assert.Equal("21:00", WarmShiftSchedule.HalfHourChoices[42]);
        Assert.Equal("23:30", WarmShiftSchedule.HalfHourChoices[^1]);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(40, 0.4)]
    [InlineData(100, 1.0)]
    [InlineData(250, 1.0)]
    [InlineData(-5, 0.0)]
    public void WarmthFor_ClampsTheStrength(int strength, double expected) => Assert.Equal(expected, WarmShiftSchedule.WarmthFor(strength), 6);

    // --- Colour matrix ---

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(20, -10, 35)]
    [InlineData(-60, 90, -40)]
    public void Matrix_AtZeroWarmth_IsExactlyTheOldMatrix(double brightness, double contrast, double saturation)
    {
        var without = ImageAdjustmentMath.CreateColorMatrix(brightness, contrast, saturation);
        var withZero = ImageAdjustmentMath.CreateColorMatrix(brightness, contrast, saturation, 0);

        Assert.Equal(without, withZero);
    }

    [Fact]
    public void Matrix_Warmth_ScalesGreenAndBlueRowsButNotRed()
    {
        var plain = ImageAdjustmentMath.CreateColorMatrix(0, 0, 0);
        var warm = ImageAdjustmentMath.CreateColorMatrix(0, 0, 0, 1);

        Assert.Equal(plain[0], warm[0], 6);                                             // R stays
        Assert.Equal(plain[6] * (1 - ImageAdjustmentMath.WarmGreenLoss), warm[6], 5);   // G
        Assert.Equal(plain[12] * (1 - ImageAdjustmentMath.WarmBlueLoss), warm[12], 5);  // B
        Assert.Equal(1f, warm[18]);                                                     // alpha untouched
    }

    [Fact]
    public void Matrix_Warmth_AlsoScalesTheBrightnessOffsetPerChannel()
    {
        var warm = ImageAdjustmentMath.CreateColorMatrix(50, 0, 0, 1);
        var plain = ImageAdjustmentMath.CreateColorMatrix(50, 0, 0);

        Assert.Equal(plain[14] * (1 - ImageAdjustmentMath.WarmBlueLoss), warm[14], 5);
    }

    [Fact]
    public void IsIdentity_IncludesWarmth()
    {
        Assert.True(ImageAdjustmentMath.IsIdentity(0, 0, 0, 0));
        Assert.True(ImageAdjustmentMath.IsIdentity(0, 0, 0, 0, 0));
        Assert.False(ImageAdjustmentMath.IsIdentity(0, 0, 0, 0, 0.4));
    }

    [Fact]
    public void WarmthScale_IsClampedAndIdentityAtZero()
    {
        Assert.Equal((1f, 1f, 1f), ImageAdjustmentMath.WarmthScale(0));
        Assert.Equal(ImageAdjustmentMath.WarmthScale(1), ImageAdjustmentMath.WarmthScale(7));
        Assert.Equal(ImageAdjustmentMath.WarmthScale(0), ImageAdjustmentMath.WarmthScale(-3));
    }

    // --- Session clock ---

    private static readonly DateTime T0 = new(2026, 9, 25, 20, 0, 0);

    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    [Fact]
    public void Clock_AccruesWhilePresentAndActive()
    {
        var clock = new ReadingSessionClock();
        clock.Tick(At(0), present: true);

        for (int s = 10; s <= 60; s += 10)
        {
            clock.NoteInput(At(s));
            clock.Tick(At(s), present: true);
        }

        Assert.Equal(TimeSpan.FromSeconds(60), clock.ActiveTime);
        Assert.Equal(TimeSpan.FromSeconds(60), clock.ActiveSinceBreak);
    }

    [Fact]
    public void Clock_NoInputForTwoMinutes_PausesAndStartsABreak()
    {
        var clock = new ReadingSessionClock();
        int breaks = 0;
        clock.BreakStarted += () => breaks++;
        clock.Tick(At(0), true);

        for (int s = 10; s <= 300; s += 10)
        {
            clock.Tick(At(s), true);   // no input at all
        }

        Assert.Equal(TimeSpan.FromMinutes(2), clock.ActiveTime);   // credited up to the cutoff, then paused
        Assert.Equal(TimeSpan.Zero, clock.ActiveSinceBreak);
        Assert.Equal(1, breaks);                                     // once, not on every idle tick
    }

    [Fact]
    public void Clock_InputAfterABreak_ResumesAccrual()
    {
        var clock = new ReadingSessionClock();
        clock.Tick(At(0), true);
        for (int s = 10; s <= 200; s += 10)
        {
            clock.Tick(At(s), true);
        }

        var before = clock.ActiveTime;
        clock.NoteInput(At(205));
        clock.Tick(At(210), true);
        clock.NoteInput(At(215));
        clock.Tick(At(220), true);

        Assert.True(clock.ActiveTime > before);
        Assert.True(clock.ActiveSinceBreak > TimeSpan.Zero);
    }

    [Fact]
    public void Clock_NotPresent_DoesNotAccrue_AndALongAbsenceIsABreak()
    {
        var clock = new ReadingSessionClock();
        int breaks = 0;
        clock.BreakStarted += () => breaks++;
        clock.Tick(At(0), true);
        clock.NoteInput(At(5));
        clock.Tick(At(10), true);
        var before = clock.ActiveTime;

        for (int s = 20; s <= 200; s += 10)
        {
            clock.Tick(At(s), present: false);
        }

        Assert.Equal(before, clock.ActiveTime);
        Assert.Equal(1, breaks);
        Assert.Equal(TimeSpan.Zero, clock.ActiveSinceBreak);

        clock.Tick(At(210), present: true);
        Assert.Equal(1, breaks);   // returning is not a second break
    }

    [Fact]
    public void Clock_AShortAbsence_IsNotABreak()
    {
        var clock = new ReadingSessionClock();
        int breaks = 0;
        clock.BreakStarted += () => breaks++;
        clock.Tick(At(0), true);
        clock.NoteInput(At(10));
        clock.Tick(At(10), true);
        var active = clock.ActiveSinceBreak;

        clock.Tick(At(20), false);
        clock.Tick(At(30), false);
        clock.Tick(At(40), true);

        Assert.Equal(0, breaks);
        Assert.Equal(active, clock.ActiveSinceBreak);
    }

    [Fact]
    public void Clock_AbsentForThirtyMinutes_StartsANewVisit()
    {
        var clock = new ReadingSessionClock();
        clock.Tick(At(0), true);
        clock.NoteInput(At(5));
        clock.Tick(At(10), true);
        clock.NotePageViewed(1, 0);
        clock.NotePageViewed(1, 1);

        for (int s = 20; s <= 1900; s += 10)
        {
            clock.Tick(At(s), false);
        }

        Assert.Equal(0, clock.PagesViewed);
        Assert.Equal(TimeSpan.Zero, clock.ActiveTime);
    }

    [Fact]
    public void Clock_CountsDistinctPagesPerIssue()
    {
        var clock = new ReadingSessionClock();

        clock.NotePageViewed(1, 0);
        clock.NotePageViewed(1, 0);
        clock.NotePageViewed(1, 4);
        clock.NotePageViewed(2, 0);

        Assert.Equal(3, clock.PagesViewed);
    }

    [Fact]
    public void Clock_PaceNeedsThreePagesAndTwoActiveMinutes()
    {
        var clock = new ReadingSessionClock();
        clock.Tick(At(0), true);
        clock.NotePageViewed(1, 0);
        clock.NotePageViewed(1, 1);
        for (int s = 10; s <= 150; s += 10)
        {
            clock.NoteInput(At(s));
            clock.Tick(At(s), true);
        }

        Assert.Null(clock.PagesPerMinute);   // 2.5 minutes but only 2 pages

        clock.NotePageViewed(1, 2);
        clock.NotePageViewed(1, 3);
        clock.NotePageViewed(1, 4);

        Assert.Equal(5 / 2.5, clock.PagesPerMinute!.Value, 3);
    }

    [Fact]
    public void Clock_NoPaceBeforeTwoMinutes()
    {
        var clock = new ReadingSessionClock();
        clock.Tick(At(0), true);
        for (int page = 0; page < 6; page++)
        {
            clock.NotePageViewed(1, page);
        }

        for (int s = 10; s <= 60; s += 10)
        {
            clock.NoteInput(At(s));
            clock.Tick(At(s), true);
        }

        Assert.Null(clock.PagesPerMinute);
    }

    [Fact]
    public void Clock_Reset_ForgetsTheVisit()
    {
        var clock = new ReadingSessionClock();
        clock.Tick(At(0), true);
        clock.NoteInput(At(10));
        clock.Tick(At(10), true);
        clock.NotePageViewed(1, 0);

        clock.Reset(At(20));

        Assert.Equal(TimeSpan.Zero, clock.ActiveTime);
        Assert.Equal(0, clock.PagesViewed);
    }

    // --- Stats chip text ---

    [Fact]
    public void Hud_FormatsAllPieces_WhenThePaceIsKnown() =>
        Assert.Equal("24 min · 18 pages · 0.8/min · ~12 min left", SessionHudFormatter.Format(TimeSpan.FromMinutes(24), 18, 0.8, 10));

    [Fact]
    public void Hud_DropsPieces_FromTheRight_WhenNotAvailable()
    {
        Assert.Equal("<1 min · 1 page", SessionHudFormatter.Format(TimeSpan.FromSeconds(20), 1, null, 30));
        Assert.Equal("5 min · 4 pages", SessionHudFormatter.Format(TimeSpan.FromMinutes(5), 4, null, 30));
        Assert.Equal("5 min · 4 pages · 0.8/min", SessionHudFormatter.Format(TimeSpan.FromMinutes(5), 4, 0.8, 0));   // finished: no estimate
    }

    [Theory]
    [InlineData(0, "<1 min")]
    [InlineData(59, "<1 min")]
    [InlineData(60, "1 min")]
    [InlineData(65 * 60, "1 h 05 min")]
    [InlineData(120 * 60, "2 h 00 min")]
    public void Hud_FormatDuration(int seconds, string expected) =>
        Assert.Equal(expected, SessionHudFormatter.FormatDuration(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0, 1.0, null)]
    [InlineData(10, 0.0, null)]
    [InlineData(1, 4.0, "<1 min left")]
    [InlineData(12, 1.0, "~12 min left")]
    public void Hud_EstimateLeft(int remaining, double pace, string? expected) =>
        Assert.Equal(expected, SessionHudFormatter.EstimateLeft(remaining, pace));

    // --- Break nudges ---

    [Fact]
    public void Nudge_FiresAfterTheInterval_ThenEveryIntervalWhileReading()
    {
        var policy = new BreakNudgePolicy(TimeSpan.FromMinutes(20));

        Assert.False(policy.ShouldNudge(TimeSpan.FromMinutes(19), false));
        Assert.True(policy.ShouldNudge(TimeSpan.FromMinutes(20), false));
        Assert.False(policy.ShouldNudge(TimeSpan.FromMinutes(20), false));   // only once
        Assert.False(policy.ShouldNudge(TimeSpan.FromMinutes(39), false));
        Assert.True(policy.ShouldNudge(TimeSpan.FromMinutes(40), false));
    }

    [Fact]
    public void Nudge_WaitsWhileThePreviousToastIsStillOpen()
    {
        var policy = new BreakNudgePolicy(TimeSpan.FromMinutes(20));

        Assert.False(policy.ShouldNudge(TimeSpan.FromMinutes(25), nudgeOpen: true));
        Assert.True(policy.ShouldNudge(TimeSpan.FromMinutes(26), nudgeOpen: false));   // due since minute 20, fires as soon as nothing is open
    }

    [Fact]
    public void Nudge_Snooze_PushesTheNextOneTenMinutesOut()
    {
        var policy = new BreakNudgePolicy(TimeSpan.FromMinutes(20));
        Assert.True(policy.ShouldNudge(TimeSpan.FromMinutes(20), false));

        policy.Snooze(TimeSpan.FromMinutes(20));

        Assert.False(policy.ShouldNudge(TimeSpan.FromMinutes(29), false));
        Assert.True(policy.ShouldNudge(TimeSpan.FromMinutes(30), false));
    }

    [Fact]
    public void Nudge_Reset_StartsTheCountAgain()
    {
        var policy = new BreakNudgePolicy(TimeSpan.FromMinutes(20));
        Assert.True(policy.ShouldNudge(TimeSpan.FromMinutes(20), false));

        policy.Reset();

        Assert.False(policy.ShouldNudge(TimeSpan.FromMinutes(19), false));
        Assert.True(policy.ShouldNudge(TimeSpan.FromMinutes(20), false));
    }

    [Fact]
    public void Nudge_ShorterInterval_TakesEffectAtOnce()
    {
        var policy = new BreakNudgePolicy(TimeSpan.FromMinutes(40));

        policy.SetInterval(TimeSpan.FromMinutes(10));

        Assert.True(policy.ShouldNudge(TimeSpan.FromMinutes(10), false));
    }

    // --- Spread stitching ---

    [Fact]
    public void SpreadLayout_LeftToRight_PutsTheFirstPageOnTheLeft_AtACommonHeight()
    {
        // 100x200 and 200x400: both scale to 400 high, widths 200 + 200.
        var layout = SpreadComposer.ComputeLayout(new PixelSize(100, 200), new PixelSize(200, 400), rightToLeft: false);

        Assert.Equal(new PixelSize(400, 400), layout.Canvas);
        Assert.Equal(new PixelRect(0, 0, 200, 400), layout.First);
        Assert.Equal(new PixelRect(200, 0, 200, 400), layout.Second);
    }

    [Fact]
    public void SpreadLayout_RightToLeft_PutsTheFirstPageOnTheRight()
    {
        var layout = SpreadComposer.ComputeLayout(new PixelSize(100, 200), new PixelSize(200, 200), rightToLeft: true);

        Assert.Equal(new PixelSize(300, 200), layout.Canvas);
        Assert.Equal(new PixelRect(200, 0, 100, 200), layout.First);    // 100 wide, right
        Assert.Equal(new PixelRect(0, 0, 200, 200), layout.Second);      // 200 wide, left
    }

    [Fact]
    public void SpreadLayout_SlotsTileTheCanvasExactly()
    {
        var layout = SpreadComposer.ComputeLayout(new PixelSize(101, 157), new PixelSize(99, 143), rightToLeft: false);

        Assert.Equal(layout.Canvas.Width, layout.First.Width + layout.Second.Width);
        Assert.Equal(layout.First.Right, layout.Second.X);
        Assert.Equal(layout.Canvas.Height, layout.First.Height);
    }

    private static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        return bitmap;
    }

    [Fact]
    public void ComposeSk_PlacesEachPageInItsHalf()
    {
        using var red = Solid(60, 100, SKColors.Red);
        using var blue = Solid(60, 100, SKColors.Blue);

        using var ltr = SpreadComposer.ComposeSk(red, blue, rightToLeft: false);
        using var rtl = SpreadComposer.ComposeSk(red, blue, rightToLeft: true);

        Assert.Equal(120, ltr.Width);
        Assert.Equal(100, ltr.Height);
        Assert.Equal(SKColors.Red, ltr.GetPixel(20, 50));
        Assert.Equal(SKColors.Blue, ltr.GetPixel(100, 50));
        Assert.Equal(SKColors.Blue, rtl.GetPixel(20, 50));
        Assert.Equal(SKColors.Red, rtl.GetPixel(100, 50));
    }

    [Fact]
    public void ComposeSk_ScalesTheShorterPageUpToTheTallerOne()
    {
        using var small = Solid(50, 50, SKColors.Green);
        using var tall = Solid(100, 100, SKColors.Yellow);

        using var stitched = SpreadComposer.ComposeSk(small, tall, rightToLeft: false);

        Assert.Equal(200, stitched.Width);    // 50x50 becomes 100x100 next to the 100x100
        Assert.Equal(100, stitched.Height);
        Assert.Equal(SKColors.Green, stitched.GetPixel(50, 50));
        Assert.Equal(SKColors.Yellow, stitched.GetPixel(150, 50));
    }
}
