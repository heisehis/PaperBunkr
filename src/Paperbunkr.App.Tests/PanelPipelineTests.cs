using Paperbunkr.App.Services.Reader.Panels;
using SkiaSharp;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The ONNX-first panel pipeline (docs/superpowers/specs/2026-09-28-guided-view-detection-upgrade-design.md): reading order, borderless-band filling, the webtoon strip detector and the
/// service's fallbacks, all on synthetic pages. How well the model does on real pages is measured by tools/Paperbunkr.PanelHarness, not here.
/// </summary>
public class PanelPipelineTests
{
    private static PanelRect R(double x, double y, double w, double h) => new(x, y, w, h);

    // ---- PanelOrdering ----

    [Fact]
    public void Ordering_TwoRows_ReadsTopToBottomAndLeftToRight()
    {
        var sorted = PanelOrdering.Sort([R(0.5, 0.55, 0.4, 0.4), R(0.1, 0.05, 0.4, 0.4), R(0.1, 0.55, 0.4, 0.4), R(0.55, 0.06, 0.35, 0.38)], rightToLeft: false);

        Assert.Equal([R(0.1, 0.05, 0.4, 0.4), R(0.55, 0.06, 0.35, 0.38), R(0.1, 0.55, 0.4, 0.4), R(0.5, 0.55, 0.4, 0.4)], sorted);
    }

    [Fact]
    public void Ordering_RightToLeft_ReversesEachRow()
    {
        var sorted = PanelOrdering.Sort([R(0.05, 0.05, 0.4, 0.4), R(0.55, 0.05, 0.4, 0.4)], rightToLeft: true);

        Assert.Equal(0.55, sorted[0].X);
        Assert.Equal(0.05, sorted[1].X);
    }

    [Fact]
    public void Ordering_TallPanelBesideAStack_ReadsTheTallPanelThenTheStackTopDown()
    {
        var tall = R(0.05, 0.05, 0.4, 0.9);
        var upper = R(0.55, 0.05, 0.4, 0.4);
        var lower = R(0.55, 0.55, 0.4, 0.4);

        var sorted = PanelOrdering.Sort([lower, tall, upper], rightToLeft: false);

        Assert.Equal([tall, upper, lower], sorted);
    }

    // ---- OnnxPanelDetector: everything that does not need the model ----

    [Fact]
    public void ToPanels_NoBoxes_IsTheUnconfidentWholePage()
    {
        var result = OnnxPanelDetector.ToPanels([], rightToLeft: false);

        Assert.False(result.Confident);
        Assert.Equal(PagePanels.Whole, result);
    }

    /// <summary>A 400x600 white page whose middle third holds textured art.</summary>
    private static SKBitmap PageWithMiddleArt()
    {
        var bitmap = new SKBitmap(400, 600);
        bitmap.Erase(SKColors.White);
        for (int y = 200; y < 400; y++)
        {
            for (int x = 0; x < 400; x++)
            {
                bitmap.SetPixel(x, y, ((x / 6) + (y / 6)) % 2 == 0 ? new SKColor(90, 90, 90) : new SKColor(150, 150, 150));
            }
        }

        return bitmap;
    }

    [Fact]
    public void BorderlessBands_ArtBetweenTwoDetectedPanels_BecomesAPanel()
    {
        using var page = PageWithMiddleArt();
        var found = OnnxPanelDetector.FindBorderlessBands(page, [R(0.05, 0.02, 0.9, 0.3), R(0.05, 0.68, 0.9, 0.3)]);

        var band = Assert.Single(found);
        Assert.InRange(band.Y, 0.3, 0.36);
        Assert.InRange(band.Bottom, 0.64, 0.7);
        Assert.Equal(1.0, band.Width, 3);
    }

    [Fact]
    public void BorderlessBands_ABlankMargin_IsNotAPanel()
    {
        using var page = new SKBitmap(400, 600);
        page.Erase(SKColors.White);

        Assert.Empty(OnnxPanelDetector.FindBorderlessBands(page, [R(0.05, 0.3, 0.9, 0.3)]));
    }

    [Fact]
    public void BorderlessBands_AGapSmallerThanTheMinimum_IsIgnored()
    {
        using var page = PageWithMiddleArt();

        Assert.Empty(OnnxPanelDetector.FindBorderlessBands(page, [R(0, 0, 1, 0.34), R(0, 0.38, 1, 0.62)]));
    }

    // ---- The webtoon strip detector ----

    private const int StripW = 300;

    /// <summary>A strip of the given height with textured art blocks (y, height) full width on a white background.</summary>
    private static byte[] Strip(int height, params (int Y, int H)[] blocks)
    {
        var luma = new byte[StripW * height];
        Array.Fill(luma, (byte)250);
        foreach (var (by, bh) in blocks)
        {
            for (int y = by; y < by + bh; y++)
            {
                for (int x = 0; x < StripW; x++)
                {
                    luma[(y * StripW) + x] = ((x / 6) + (y / 6)) % 2 == 0 ? (byte)90 : (byte)150;
                }
            }
        }

        return luma;
    }

    [Fact]
    public void Strip_ThreeBlocksSeparatedByEmptyBands_FindsThreeBlocksInOrder()
    {
        const int h = 3000;
        var result = PanelDetector.DetectStrip(Strip(h, (100, 500), (800, 600), (1700, 700)), StripW, h);

        Assert.True(result.Confident);
        Assert.Equal(3, result.Count);
        Assert.InRange(result.Rects[0].Y, 100 / (double)h - 0.01, 100 / (double)h + 0.01);
        Assert.InRange(result.Rects[1].Y, 800 / (double)h - 0.01, 800 / (double)h + 0.01);
        Assert.InRange(result.Rects[2].Bottom, 2400 / (double)h - 0.01, 2400 / (double)h + 0.01);
    }

    [Fact]
    public void Strip_TinyEmptyGapInsideABlock_DoesNotSplitIt()
    {
        const int h = 2000;
        var luma = Strip(h, (100, 800), (1400, 400));
        // 6 px of paper inside the first block is under the minimum band (4% of the 300 px width is 12 px), so it stays one block.
        for (int y = 500; y < 506; y++)
        {
            Array.Fill(luma, (byte)250, y * StripW, StripW);
        }

        var result = PanelDetector.DetectStrip(luma, StripW, h);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Strip_NoEmptyBands_IsTheWholePage()
    {
        const int h = 2000;
        var result = PanelDetector.DetectStrip(Strip(h, (0, h)), StripW, h);

        Assert.False(result.Confident);
    }

    [Fact]
    public void Strip_BlocksTooShortToBeArt_AreDropped()
    {
        const int h = 2000;
        // A 10 px sliver (stray text) and two real blocks.
        var result = PanelDetector.DetectStrip(Strip(h, (100, 10), (400, 500), (1200, 500)), StripW, h);

        Assert.Equal(2, result.Count);
    }

    // ---- PanelDetectionService ----

    [Fact]
    public void Service_WithoutAModelFile_FallsBackToTheGutterHeuristic()
    {
        var previous = PanelDetectionService.ModelPath;
        try
        {
            PanelDetectionService.ModelPath = Path.Combine(Path.GetTempPath(), "no-such-panel-model.onnx");
            Assert.False(PanelDetectionService.OnnxAvailable);

            using var page = new SKBitmap(400, 600);
            page.Erase(new SKColor(245, 245, 245));
            foreach (var (px, py, pw, ph) in new[] { (20, 20, 172, 268), (216, 20, 164, 268), (20, 312, 172, 268), (216, 312, 164, 268) })
            {
                for (int y = py; y < py + ph; y++)
                {
                    for (int x = px; x < px + pw; x++)
                    {
                        page.SetPixel(x, y, ((x / 6) + (y / 6)) % 2 == 0 ? new SKColor(90, 90, 90) : new SKColor(150, 150, 150));
                    }
                }
            }

            var result = PanelDetectionService.Detect(page, rightToLeft: false);

            Assert.True(result.Confident);
            Assert.Equal(4, result.Count);
        }
        finally
        {
            PanelDetectionService.ModelPath = previous;
        }
    }

    [Fact]
    public void Service_TallPage_UsesTheStripDetector()
    {
        var previous = PanelDetectionService.ModelPath;
        try
        {
            PanelDetectionService.ModelPath = Path.Combine(Path.GetTempPath(), "no-such-panel-model.onnx");
            const int h = 3000;
            using var page = new SKBitmap(StripW, h);
            page.Erase(new SKColor(250, 250, 250));
            foreach (var (by, bh) in new[] { (100, 500), (800, 600), (1700, 700) })
            {
                for (int y = by; y < by + bh; y++)
                {
                    for (int x = 0; x < StripW; x++)
                    {
                        page.SetPixel(x, y, ((x / 6) + (y / 6)) % 2 == 0 ? new SKColor(90, 90, 90) : new SKColor(150, 150, 150));
                    }
                }
            }

            var result = PanelDetectionService.Detect(page, rightToLeft: false);

            Assert.True(result.Confident);
            Assert.Equal(3, result.Count);
        }
        finally
        {
            PanelDetectionService.ModelPath = previous;
        }
    }

    [Fact]
    public void Service_TinyPage_IsTheWholePageAndNeverThrows()
    {
        using var page = new SKBitmap(4, 4);

        Assert.Equal(PagePanels.Whole, PanelDetectionService.Detect(page, rightToLeft: false));
    }

    [Fact]
    public void Model_WhenBundled_LoadsAndRunsOnABlankPage()
    {
        string model = Path.Combine(AppContext.BaseDirectory, "Models", "panel-detector.onnx");
        Assert.True(File.Exists(model), $"the panel model must be copied next to the app: {model}");

        using var detector = new OnnxPanelDetector(model);
        using var page = new SKBitmap(400, 600);
        page.Erase(SKColors.White);

        var boxes = detector.Run(page);

        Assert.All(boxes, b => Assert.InRange(b.X, 0, 1));
    }
}
