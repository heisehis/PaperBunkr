using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;
using static Paperbunkr.App.Tests.Theming.FoundationStyleTests;

namespace Paperbunkr.App.Tests;

/// <summary>Motion v2 reader pieces (docs/superpowers/specs/2026-10-20-motion-system-v2-reader-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderMotionTests
{
    [Theory]
    [InlineData((int)PageTransitionDirection.Right, false, true)]
    [InlineData((int)PageTransitionDirection.Left, false, false)]
    [InlineData((int)PageTransitionDirection.Left, true, true)]                                  // right-to-left reads forward leftward
    [InlineData((int)PageTransitionDirection.Right, true, false)]
    [InlineData((int)PageTransitionDirection.Down, false, true)]
    [InlineData((int)PageTransitionDirection.Up, false, false)]
    public void ATurnReadsForwardOrBack_ByDirectionAndReadingOrder(int direction, bool rtl, bool forward) =>
        Assert.Equal(forward, PageTransitionMath.IsForwardReading((PageTransitionDirection)direction, rtl));

    [Fact]
    public void Book_ReadingForward_TheNewPageSweepsInOverTheStillOldOne()
    {
        var (newMoves, start) = PageTransitionMath.BookOffset(PageTransitionDirection.Right, forwardReading: true, progress: 0, mainAxisExtent: 1000);
        var (_, end) = PageTransitionMath.BookOffset(PageTransitionDirection.Right, forwardReading: true, progress: 1, mainAxisExtent: 1000);

        Assert.True(newMoves);
        Assert.Equal(1000, start);                                                            // starts off the trailing edge
        Assert.Equal(0, end);                                                                 // lands in place
    }

    [Fact]
    public void Book_ReadingBack_TheOldPageSlidesOffTheOtherWay()
    {
        var (newMoves, end) = PageTransitionMath.BookOffset(PageTransitionDirection.Left, forwardReading: false, progress: 1, mainAxisExtent: 1000);

        Assert.False(newMoves);
        Assert.Equal(1000, end);
    }

    [Fact]
    public void Curl_StartsFlat_FoldsFromTheOuterCorner_AndMirrorsForRightToLeft()
    {
        var page = new Rect(100, 50, 400, 600);

        Assert.False(PageTransitionMath.CurlFold(page, 0, mirrored: false).Active);

        var (on, normal, active) = PageTransitionMath.CurlFold(page, 0.3, mirrored: false);
        Assert.True(active);
        Assert.True(normal.X > 0, "the turned side faces the bottom-right corner");
        Assert.InRange(on.X, page.Left, page.Right);

        var (_, mirroredNormal, _) = PageTransitionMath.CurlFold(page, 0.3, mirrored: true);
        Assert.True(mirroredNormal.X < 0, "right-to-left folds from the bottom-left corner");
    }

    /// <summary>On a spread only the outer leaf turns, pivoting on the spine: by the end the fold has passed the leaf's inner edge (the
    /// whole leaf is over), and its folded-back corner lands on the facing page's outer edge.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Curl_OnASpreadLeaf_EndsWithTheWholeLeafTurnedOverTheSpine(bool rightToLeft)
    {
        var leaf = rightToLeft ? new Rect(0, 0, 400, 600) : new Rect(400, 0, 400, 600);       // the later page: right LTR, left RTL
        double spine = rightToLeft ? leaf.Right : leaf.Left;
        var (on, normal, active) = PageTransitionMath.CurlFold(leaf, 1, mirrored: rightToLeft);

        Assert.True(active);
        Assert.True(rightToLeft ? on.X > spine : on.X < spine, "the fold has crossed the spine");
        var outerCorner = rightToLeft ? leaf.BottomLeft : leaf.BottomRight;
        var landed = PageTransitionMath.Reflect(outerCorner, on, normal);
        double facingOuterEdge = rightToLeft ? 800 : 0;
        Assert.InRange(landed.X, facingOuterEdge - 40, facingOuterEdge + 40);
    }

    [Fact]
    public void Curl_TheTurnedAndFlatPartsSplitThePageAlongTheFold()
    {
        var page = new Rect(0, 0, 400, 600);
        var (on, normal, _) = PageTransitionMath.CurlFold(page, 0.4, mirrored: false);
        var corners = PageTransitionMath.Corners(page);

        var turned = PageTransitionMath.ClipToSide(corners, on, normal, normalSide: true);
        var flat = PageTransitionMath.ClipToSide(corners, on, normal, normalSide: false);

        Assert.Contains(page.BottomRight, turned);
        Assert.Contains(page.TopLeft, flat);
        Assert.Equal(page.Width * page.Height, Area(turned) + Area(flat), precision: 3);

        // The fold is a mirror: reflecting a point twice returns it.
        var p = new Point(320, 510);
        var back = PageTransitionMath.Reflect(PageTransitionMath.Reflect(p, on, normal), on, normal);
        Assert.Equal(p.X, back.X, precision: 6);
        Assert.Equal(p.Y, back.Y, precision: 6);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(100, -50, -4, 2)]
    [InlineData(5000, -5000, -12, 12)]                                                       // capped
    public void Parallax_DriftsAgainstThePage_Capped(double x, double y, double expectedX, double expectedY)
    {
        var offset = AmbientBackdrop.ParallaxOffset(new Vector(x, y));
        Assert.Equal(expectedX, offset.X, precision: 6);
        Assert.Equal(expectedY, offset.Y, precision: 6);
    }

    [Fact]
    public void Backdrop_CrossfadesANewGradientOnTheBackLayer_AndKeepsStillWithoutAmbientMotion()
    {
        WithFoundation("default", () =>
        {
            var previous = MotionPolicy.Preference;
            MotionPolicy.SetPreference(MotionPreferenceMode.Full);
            try
            {
                var backdrop = new AmbientBackdrop { Width = 200, Height = 100 };
                var window = Host(backdrop);
                var first = new SolidColorBrush(Colors.Navy);
                var second = new LinearGradientBrush();

                backdrop.Brush = first;
                Assert.Same(first, backdrop.FrontLayer.Background);                           // the first brush just appears

                backdrop.Brush = second;
                Assert.Same(second, backdrop.BackLayer.Background);                           // the next fades in over it
                Assert.Same(first, backdrop.FrontLayer.Background);
                Assert.NotNull(backdrop.BackLayer.Transitions);

                backdrop.ParallaxSource = new Vector(200, 0);
                Assert.Equal(0, backdrop.ParallaxShift.X);                                    // Ambient motion off: no parallax, no drift
                Assert.False(backdrop.IsDrifting);
                backdrop.MotionEnabled = true;
                Assert.Equal(-8, backdrop.ParallaxShift.X, precision: 6);
                Assert.Equal(MotionPolicy.AllowsContinuous(), backdrop.IsDrifting);           // drift only where continuous decoration is allowed
                backdrop.MotionEnabled = false;
                Assert.False(backdrop.IsDrifting);
                window.Close();
            }
            finally
            {
                MotionPolicy.SetPreference(previous);
            }
        });
    }

    [Fact]
    public void ClosingAPanel_HandsFocusBackToTheButtonThatOpenedIt()
    {
        WithFoundation("default", () =>
        {
            var previous = MotionPolicy.Preference;
            MotionPolicy.SetPreference(MotionPreferenceMode.Off);
            try
            {
                var opener = new Button { Content = "More tools" };
                var inside = new Button { Content = "Fit" };
                var panel = new Border { Child = inside };
                Reveal.SetReturnFocusTo(panel, opener);
                var root = new StackPanel { Children = { opener, panel } };
                var window = Host(root);
                Reveal.SetIsOpen(panel, true);
                RunLayout(window);
                inside.Focus();

                Reveal.SetIsOpen(panel, false);

                Assert.True(opener.IsFocused);
                window.Close();
            }
            finally
            {
                MotionPolicy.SetPreference(previous);
            }
        });
    }

    [Fact]
    public void WheelGlide_CoversItsShareOfTheDistance_AndSnapsAtTheEnd()
    {
        double next = PageCanvas.StepScrollTowards(0, 1000, PageCanvas.ScrollSmoothingSeconds);
        Assert.InRange(next, 630, 635);                                                       // 1 - 1/e of the way after one time constant
        Assert.Equal(1000, PageCanvas.StepScrollTowards(999.7, 1000, 0.016));
    }

    [Theory]
    [InlineData(0, 1000, 0.22)]
    [InlineData(2000, 1000, 0.34)]
    [InlineData(3000, 1000, 0.40)]
    public void NearJumpsGlide_ForLongerTheFurther(double distance, double viewport, double seconds) =>
        Assert.Equal(seconds, PageCanvas.JumpGlideSeconds(distance, viewport)!.Value, precision: 6);

    [Fact]
    public void AJumpFurtherThanThreeScreens_DoesNotGlide() => Assert.Null(PageCanvas.JumpGlideSeconds(3500, 1000));

    [Theory]
    [InlineData(0.0, false, 1, 6)]                                                            // start: thin read stack on the left
    [InlineData(1.0, false, 6, 1)]
    [InlineData(0.5, false, 4, 3)]
    [InlineData(0.0, true, 6, 1)]                                                             // right-to-left: read pages pile on the right
    public void PageStackEdges_FollowReadingProgress(double progress, bool rtl, int left, int right) =>
        Assert.Equal((left, right), ReaderPageVisualHandler.PageStackSheets(progress, rtl));

    [Theory]
    [InlineData(PageTransitionStyle.Curl, 250, 420)]
    [InlineData(PageTransitionStyle.Book, 250, 420)]
    [InlineData(PageTransitionStyle.Curl, 600, 600)]                                          // a slower saved speed still wins
    [InlineData(PageTransitionStyle.Slide, 250, 250)]
    public void PhysicalTurns_NeverRunShorterThan420ms(PageTransitionStyle style, int saved, int expected) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expected), PageCanvas.EffectiveTurnDuration(style, saved));

    [Fact]
    public void BookLook_ShadesTheSpine_MoreThanThePageMiddle_AndDrawsStackEdgesOutside()
    {
        using var bitmap = new SkiaSharp.SKBitmap(420, 200);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.White);
            ReaderPageVisualHandler.DrawBookLook(canvas, new Rect(10, 10, 200, 180), new Rect(210, 10, 200, 180), progress: 0.5, isRightToLeft: false);
        }

        byte Luma(int x) => bitmap.GetPixel(x, 100).Red;
        Assert.True(Luma(209) < Luma(110) - 40, "the spine is clearly darker than the page middle");
        Assert.True(Luma(110) > 250, "the page middle is left alone");
        Assert.NotEqual(SkiaSharp.SKColors.White, bitmap.GetPixel(7, 100));                   // stack edges outside the left page
    }

    [Fact]
    public void AProfile_CanTurnTheBookLookOff()
    {
        var settings = new AppSettings { ReaderBookLook = true };
        var effective = Paperbunkr.App.Services.Reader.ReaderProfileOverlay.Apply(settings, new Paperbunkr.App.Models.ReaderProfileState(ReaderBookLook: false));
        Assert.False(effective.ReaderBookLook);
    }

    private static double Area(IReadOnlyList<Point> polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return Math.Abs(sum) / 2;
    }
}
