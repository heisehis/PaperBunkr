using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// docs/superpowers/specs/2026-09-21-cosmetics-pitch-design.md - the pure/logic halves of #4 (timeline
/// connector node state), #6 (splash ambient dots) and #7 (reading-list cover mosaic fill rule). What
/// they look like on screen is the user's check; what decides what is drawn is asserted here.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CosmeticsPitchSliceTests
{
    // ---- #7 mosaic fill rule ----

    [Fact]
    public void Mosaic_NoCovers_IsEmpty()
        => Assert.Empty(ReadingListCoverMosaic.PickCoverKeys(Array.Empty<string>()));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Mosaic_OneOrTwoCovers_StaysASingleFirstCover(int count)
    {
        var keys = Enumerable.Range(1, count).Select(i => $"k{i}").ToList();

        Assert.Equal(new[] { "k1" }, ReadingListCoverMosaic.PickCoverKeys(keys));
    }

    [Fact]
    public void Mosaic_ThreeCovers_RepeatsTheFirstToFillTheGrid()
        => Assert.Equal(new[] { "a", "b", "c", "a" }, ReadingListCoverMosaic.PickCoverKeys(new[] { "a", "b", "c" }));

    [Fact]
    public void Mosaic_FourOrMoreCovers_UsesTheFirstFourInOrder()
        => Assert.Equal(new[] { "a", "b", "c", "d" }, ReadingListCoverMosaic.PickCoverKeys(new[] { "a", "b", "c", "d", "e", "f" }));

    // ---- #4 timeline connector node ----

    private static TimelineIssueCard Card(bool unread) => new()
    {
        IssueId = 1, Title = "T", SeriesName = "S", IsUnread = unread, IsReducedConfidence = false, CoverBrush = Brushes.Black,
    };

    [Fact]
    public void TimelineSection_HasUnread_TracksItsIssues()
    {
        var section = new TimelineSectionViewModel { Label = "Modern" };
        Assert.False(section.HasUnread); // an empty era has nothing to read

        section.Issues.Add(Card(unread: false));
        Assert.False(section.HasUnread);

        section.Issues.Add(Card(unread: true));
        Assert.True(section.HasUnread);
    }

    // ---- #6 splash ambient dots ----

    [Fact]
    public void AmbientDots_StayWithinWrapBounds_AtAnyTime()
    {
        const double w = 480, h = 340;
        for (int i = 0; i < AmbientDotsOverlay.DotCount; i++)
        {
            foreach (double t in new[] { 0.0, 1.0, 7.5, 60.0, 3600.0 })
            {
                var (center, radius, opacity) = AmbientDotsOverlay.DotAt(i, t, w, h);

                Assert.InRange(center.Y, -12.001, h + 12.001);
                Assert.InRange(center.X, -6.001, w + 6.001); // swayed +/- 6px around a base inside the width
                Assert.InRange(radius, 1, 2.6);
                Assert.InRange(opacity, 0.06, 0.18); // faint by design - never competes with the emblem
            }
        }
    }

    [Fact]
    public void AmbientDots_DriftUpward_OverTime()
    {
        // Between wraps a dot only ever moves up (smaller Y); sample two close instants for each dot.
        for (int i = 0; i < AmbientDotsOverlay.DotCount; i++)
        {
            var early = AmbientDotsOverlay.DotAt(i, 2.0, 480, 340).Center.Y;
            var later = AmbientDotsOverlay.DotAt(i, 2.5, 480, 340).Center.Y;

            Assert.True(later < early || later - early > 200, $"dot {i} should rise (or have just wrapped)");
        }
    }

    [Fact]
    public void Splash_StartsAmbientDots_OnlyWhenMotionAllowedAndEnabled()
    {
        Assert.True(AmbientRunningWhileOpen(reducedMotion: false, ambient: true));
        Assert.False(AmbientRunningWhileOpen(reducedMotion: false, ambient: false), "preference off");
        Assert.False(AmbientRunningWhileOpen(reducedMotion: true, ambient: true), "reduced motion always wins");
    }

    [Fact]
    public void Splash_StopsAmbientDots_WhenItFadesOut()
    {
        var window = new SplashWindow(reducedMotion: false, ambientMotionEnabled: true) { DataContext = new SplashViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var dots = window.FindControl<AmbientDotsOverlay>("AmbientDots")!;
        Assert.True(dots.IsRunning);

        // FadeOutAndCloseAsync stops the dots synchronously, before its fade animation starts. The animation itself needs a real
        // clock that headless tests don't provide (awaiting it hangs), so it is deliberately not awaited - the window is closed here.
        _ = window.FadeOutAndCloseAsync();
        Assert.False(dots.IsRunning);
        window.Close();
    }

    /// <summary>Whether the splash's dots are running while it is open (captured before Close() detaches and stops them).</summary>
    private static bool AmbientRunningWhileOpen(bool reducedMotion, bool ambient)
    {
        var window = new SplashWindow(reducedMotion, ambient) { DataContext = new SplashViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        bool running = window.FindControl<AmbientDotsOverlay>("AmbientDots")!.IsRunning;
        window.Close();
        return running;
    }
}
