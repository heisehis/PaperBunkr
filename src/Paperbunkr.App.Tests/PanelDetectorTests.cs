using Paperbunkr.App.Services.Reader.Panels;

namespace Paperbunkr.App.Tests;

/// <summary>Panel detection on synthetic pages (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md section 2). How it does on real pages is the user's on-screen check.</summary>
public class PanelDetectorTests
{
    private const int W = 400;
    private const int H = 600;

    /// <summary>A page filled with <paramref name="background"/>, with textured "art" rectangles (a checker of two mid tones, so they are never background-like).</summary>
    private static byte[] Page(byte background, params (int X, int Y, int W, int H)[] panels)
    {
        var luma = new byte[W * H];
        Array.Fill(luma, background);
        foreach (var (px, py, pw, ph) in panels)
        {
            for (int y = py; y < py + ph; y++)
            {
                for (int x = px; x < px + pw; x++)
                {
                    luma[(y * W) + x] = ((x / 6) + (y / 6)) % 2 == 0 ? (byte)90 : (byte)150;
                }
            }
        }

        return luma;
    }

    private static PagePanels Detect(byte[] luma, bool rtl = false) => PanelDetector.Detect(luma, W, H, rtl);

    private static void AssertRect(PanelRect expected, PanelRect actual, double tolerance = 0.02)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Width, expected.Width - tolerance, expected.Width + tolerance);
        Assert.InRange(actual.Height, expected.Height - tolerance, expected.Height + tolerance);
    }

    private static PanelRect Norm(int x, int y, int w, int h) => new(x / (double)W, y / (double)H, w / (double)W, h / (double)H);

    // Four panels in a 2x2 grid with 24 px gutters and a 20 px margin.
    private static readonly (int X, int Y, int W, int H)[] Grid =
    [
        (20, 20, 172, 268), (216, 20, 164, 268),
        (20, 312, 172, 268), (216, 312, 164, 268),
    ];

    [Fact]
    public void TwoByTwoGrid_FindsFourPanels_InReadingOrder()
    {
        var result = Detect(Page(245, Grid));

        Assert.True(result.Confident);
        Assert.Equal(4, result.Count);
        for (int i = 0; i < 4; i++)
        {
            AssertRect(Norm(Grid[i].X, Grid[i].Y, Grid[i].W, Grid[i].H), result.Rects[i]);
        }
    }

    [Fact]
    public void TwoByTwoGrid_RightToLeft_ReadsEachRowFromTheRight()
    {
        var result = Detect(Page(245, Grid), rtl: true);

        Assert.Equal(4, result.Count);
        AssertRect(Norm(Grid[1].X, Grid[1].Y, Grid[1].W, Grid[1].H), result.Rects[0]);   // top right first
        AssertRect(Norm(Grid[0].X, Grid[0].Y, Grid[0].W, Grid[0].H), result.Rects[1]);
        AssertRect(Norm(Grid[3].X, Grid[3].Y, Grid[3].W, Grid[3].H), result.Rects[2]);
        AssertRect(Norm(Grid[2].X, Grid[2].Y, Grid[2].W, Grid[2].H), result.Rects[3]);
    }

    [Fact]
    public void StaggeredRows_OneWidePanelOverTwo()
    {
        var panels = new[] { (20, 20, 360, 250), (20, 294, 172, 286), (216, 294, 164, 286) };

        var result = Detect(Page(245, panels));

        Assert.Equal(3, result.Count);
        AssertRect(Norm(20, 20, 360, 250), result.Rects[0]);
        AssertRect(Norm(20, 294, 172, 286), result.Rects[1]);
        AssertRect(Norm(216, 294, 164, 286), result.Rects[2]);
    }

    [Fact]
    public void LShapedLayout_CutsTheColumnFirst_ThenStacksTheRightSide()
    {
        var panels = new[] { (20, 20, 172, 560), (216, 20, 164, 268), (216, 312, 164, 268) };

        var ltr = Detect(Page(245, panels));
        var rtl = Detect(Page(245, panels), rtl: true);

        Assert.Equal(3, ltr.Count);
        AssertRect(Norm(20, 20, 172, 560), ltr.Rects[0]);
        AssertRect(Norm(216, 20, 164, 268), ltr.Rects[1]);
        AssertRect(Norm(216, 312, 164, 268), ltr.Rects[2]);

        Assert.Equal(3, rtl.Count);
        AssertRect(Norm(216, 20, 164, 268), rtl.Rects[0]);   // the right column first, top to bottom, then the tall left panel
        AssertRect(Norm(216, 312, 164, 268), rtl.Rects[1]);
        AssertRect(Norm(20, 20, 172, 560), rtl.Rects[2]);
    }

    [Fact]
    public void BlackGutters_LightPanels_AreFoundToo()
    {
        var luma = new byte[W * H];
        Array.Fill(luma, (byte)8);
        foreach (var (px, py, pw, ph) in Grid)
        {
            for (int y = py; y < py + ph; y++)
            {
                for (int x = px; x < px + pw; x++)
                {
                    luma[(y * W) + x] = ((x / 6) + (y / 6)) % 2 == 0 ? (byte)120 : (byte)190;
                }
            }
        }

        var result = Detect(luma);

        Assert.True(result.Confident);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void SixPanels_ThreeRowsOfTwo()
    {
        var panels = new List<(int, int, int, int)>();
        for (int row = 0; row < 3; row++)
        {
            panels.Add((20, 20 + (row * 190), 172, 170));
            panels.Add((216, 20 + (row * 190), 164, 170));
        }

        var result = Detect(Page(245, panels.ToArray()));

        Assert.Equal(6, result.Count);
    }

    [Fact]
    public void SplashPage_FallsBackToTheWholePage()
    {
        var result = Detect(Page(245, (8, 8, 384, 584)));

        Assert.False(result.Confident);
        Assert.Equal(PanelRect.WholePage, Assert.Single(result.Rects));
    }

    [Fact]
    public void PageWithoutGutters_FallsBackToTheWholePage()
    {
        var result = Detect(Page(90, (0, 0, W, H)));

        Assert.False(result.Confident);
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public void BlankPage_FallsBackToTheWholePage()
    {
        var result = Detect(Page(250));

        Assert.False(result.Confident);
        Assert.Equal(PanelRect.WholePage, Assert.Single(result.Rects));
    }

    [Fact]
    public void TinySpecks_AreNotPanels()
    {
        // One real panel plus two 12x12 specks in the margin: still fewer than two real panels.
        var result = Detect(Page(245, (20, 20, 360, 560), (390, 10, 8, 8)));

        Assert.False(result.Confident);
    }

    [Fact]
    public void SmallPanelsBelowTheAreaFloor_AreDropped()
    {
        // Two real panels and a 30x30 speck (0.4% of the page).
        var panels = new[] { (20, 20, 172, 560), (216, 20, 164, 300), (216, 360, 30, 30) };

        var result = Detect(Page(245, panels));

        Assert.True(result.Confident);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Results_StayInsideThePage_AndDoNotOverlap()
    {
        var result = Detect(Page(245, Grid));

        Assert.All(result.Rects, r =>
        {
            Assert.InRange(r.X, 0, 1);
            Assert.InRange(r.Y, 0, 1);
            Assert.InRange(r.Right, 0, 1);
            Assert.InRange(r.Bottom, 0, 1);
        });
        for (int i = 0; i < result.Count; i++)
        {
            for (int j = i + 1; j < result.Count; j++)
            {
                bool overlap = result.Rects[i].X < result.Rects[j].Right && result.Rects[j].X < result.Rects[i].Right
                    && result.Rects[i].Y < result.Rects[j].Bottom && result.Rects[j].Y < result.Rects[i].Bottom;
                Assert.False(overlap);
            }
        }
    }

    [Fact]
    public void NarrowGutters_BelowTheMinimum_DoNotSplit()
    {
        // 2 px gutter: thinner than 1.2% of the page.
        var panels = new[] { (20, 20, 178, 560), (200, 20, 180, 560) };

        var result = Detect(Page(245, panels));

        Assert.False(result.Confident);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 8)]
    public void DegenerateInput_ReturnsTheWholePage(int w, int h)
    {
        var result = PanelDetector.Detect(new byte[Math.Max(1, w * h)], w, h, false);

        Assert.Equal(PagePanels.Whole, result);
    }

    [Fact]
    public void ShortBuffer_ReturnsTheWholePage() =>
        Assert.Equal(PagePanels.Whole, PanelDetector.Detect(new byte[10], W, H, false));

    [Fact]
    public void Detect_IsDeterministic()
    {
        var page = Page(245, Grid);

        Assert.Equal(Detect(page).Rects, Detect(page).Rects);
    }

    [Fact]
    public void PanelRect_Geometry()
    {
        var r = new PanelRect(0.1, 0.2, 0.3, 0.4);

        Assert.Equal(0.4, r.Right, 9);
        Assert.Equal(0.6, r.Bottom, 9);
        Assert.Equal(0.25, r.CenterX, 9);
        Assert.Equal(0.4, r.CenterY, 9);
        Assert.Equal(0.12, r.Area, 9);
        Assert.True(r.Contains(0.2, 0.3));
        Assert.False(r.Contains(0.5, 0.3));
    }
}
