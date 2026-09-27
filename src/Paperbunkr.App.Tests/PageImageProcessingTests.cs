using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Auto-levels, auto-crop and sharpen (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md #2 and #3) on synthetic pages, and the pipeline applying them.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PageImageProcessingTests : IDisposable
{
    private const int W = 400;
    private const int H = 600;

    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"pb_processing_test_{Guid.NewGuid():N}.cbz");

    public void Dispose()
    {
        try { if (File.Exists(_cbzPath)) File.Delete(_cbzPath); } catch (IOException) { }
    }

    /// <summary>A grid filled with <paramref name="background"/> and textured "art" rectangles (a checker of two mid tones so they are never border-like).</summary>
    private static PageLumaGrid Grid(byte background, params (int X, int Y, int W, int H)[] art)
    {
        var luma = new byte[W * H];
        Array.Fill(luma, background);
        foreach (var (px, py, pw, ph) in art)
        {
            for (int y = py; y < py + ph; y++)
            {
                for (int x = px; x < px + pw; x++)
                {
                    luma[(y * W) + x] = ((x / 6) + (y / 6)) % 2 == 0 ? (byte)90 : (byte)150;
                }
            }
        }

        return new PageLumaGrid(luma, W, H);
    }

    // ===== Auto-levels =====

    [Fact]
    public void Levels_WashedOutPage_GetsAStretch_WithCEsClamps()
    {
        var luma = new byte[W * H];
        for (int i = 0; i < luma.Length; i++)
        {
            luma[i] = (byte)(96 + (i % 64));         // greys 96..159: washed out, nothing near black or white
        }

        var levels = PageLevelsAnalyzer.Analyze(new PageLumaGrid(luma, W, H));

        Assert.NotNull(levels);
        Assert.Equal(0.25f, levels!.Value.BlackPoint, 3);   // capped: CE never treats more than the lowest quarter as black
        Assert.Equal(0.75f, levels.Value.WhitePoint, 3);    // floored: nor less than the top quarter as white
    }

    [Fact]
    public void Levels_PageThatAlreadySpansTheRange_IsLeftAlone()
    {
        var luma = new byte[W * H];
        for (int i = 0; i < luma.Length; i++)
        {
            luma[i] = (byte)(i % 256);
        }

        Assert.Null(PageLevelsAnalyzer.Analyze(new PageLumaGrid(luma, W, H)));
    }

    [Fact]
    public void Levels_OnlyTheRequestedRegionCounts()
    {
        // A white border round a washed-out middle: over the whole page the white border keeps the white point at the top of the range; over the middle alone it is the middle's own.
        var grid = Grid(255, (100, 150, 200, 300));

        Assert.True(PageLevelsAnalyzer.Analyze(grid)!.Value.WhitePoint > 0.95f);
        Assert.Equal(0.75f, PageLevelsAnalyzer.Analyze(grid, (100, 150, 300, 450))!.Value.WhitePoint, 3);
    }

    [Fact]
    public void LevelsMatrix_MapsBlackToZero_AndWhiteToOne()
    {
        var m = ImageAdjustmentMath.CreateLevelsMatrix(0.25f, 0.75f);

        // Skia matrix: out = row . (r, g, b, a) + translation (the last column), all normalized 0-1.
        Assert.Equal(0f, (m[0] * 0.25f) + m[4], 4);
        Assert.Equal(1f, (m[0] * 0.75f) + m[4], 4);
        Assert.Equal(0.5f, (m[6] * 0.5f) + m[9], 4);
        Assert.Equal(1f, m[18]);                                  // alpha untouched
    }

    [Theory]
    [InlineData(0.0f, 1.0f, false)]
    [InlineData(0.04f, 0.96f, false)]
    [InlineData(0.06f, 1.0f, true)]
    [InlineData(0.0f, 0.9f, true)]
    public void NeedsLevels_FollowsCEsWashedOutRule(float black, float white, bool expected) =>
        Assert.Equal(expected, ImageAdjustmentMath.NeedsLevels(black, white));

    // ===== Sharpen =====

    [Theory]
    [InlineData(1, 15f, 11f)]
    [InlineData(2, 10f, 6f)]
    [InlineData(3, 5f, 1f)]
    public void SharpenKernel_IsCEsCross_ForEachLevel(int level, float centre, float divisor)
    {
        var kernel = ImageAdjustmentMath.CreateSharpenKernel(level);

        Assert.Equal(9, kernel.Length);
        Assert.Equal(centre / divisor, kernel[4], 5);
        Assert.Equal(-1 / divisor, kernel[1], 5);
        Assert.Equal(-1 / divisor, kernel[3], 5);
        Assert.Equal(-1 / divisor, kernel[5], 5);
        Assert.Equal(-1 / divisor, kernel[7], 5);
        Assert.Equal(0f, kernel[0]);
        Assert.Equal(1f, kernel.Sum(), 5);                        // brightness of a flat area is unchanged
    }

    [Fact]
    public void Sharpen_ZeroIsIdentity_AndAnyLevelIsNot()
    {
        Assert.True(ImageAdjustmentMath.IsIdentity(0, 0, 0, 0, 0, 0));
        Assert.False(ImageAdjustmentMath.IsIdentity(0, 0, 0, 0, 0, 1));
    }

    // ===== Auto-crop detection =====

    [Fact]
    public void Crop_WhiteBorder_IsTrimmedToTheArt_KeepingASmallMargin() // KeepMargin, 2% of the page
    {
        var crop = PageCropDetector.Detect(Grid(250, (60, 90, 280, 420)));

        Assert.NotNull(crop);
        Assert.InRange(crop!.Value.Left, 0.12, 0.14);             // 60/400 = 0.15 minus the 2% kept
        Assert.InRange(crop.Value.Right, 0.12, 0.14);             // 60/400 on the right too
        Assert.InRange(crop.Value.Top, 0.12, 0.14);               // 90/600 = 0.15 minus 2%
        Assert.InRange(crop.Value.Bottom, 0.12, 0.14);            // the art ends at 510: 90/600 left
    }

    [Fact]
    public void Crop_BlackBorder_IsTrimmedToo()
    {
        var crop = PageCropDetector.Detect(Grid(8, (70, 100, 260, 400)));

        Assert.NotNull(crop);
        Assert.True(crop!.Value.Left > 0.10);
    }

    [Fact]
    public void Crop_UnevenBorders_TrimEachSideByItsOwnAmount()
    {
        var crop = PageCropDetector.Detect(Grid(250, (20, 60, 340, 480)));   // left 20, right 40, top 60, bottom 60

        Assert.NotNull(crop);
        Assert.InRange(crop!.Value.Left, 0.0, 0.04);              // 5% of the width minus the margin, or nothing if it is too small to bother with
        Assert.InRange(crop.Value.Right, 0.07, 0.09);             // 10% minus 2%
        Assert.InRange(crop.Value.Top, 0.07, 0.09);
    }

    [Fact]
    public void Crop_IsCappedAtFifteenPercentPerSide()
    {
        var crop = PageCropDetector.Detect(Grid(250, (100, 150, 200, 300)));   // art in the middle: 25% margins would be trimmed, but no more than 15%

        Assert.NotNull(crop);
        Assert.Equal(PageCropDetector.MaxSideFraction, crop!.Value.Left, 6);
        Assert.Equal(PageCropDetector.MaxSideFraction, crop.Value.Top, 6);
    }

    [Fact]
    public void Crop_NothingToTrim_ReturnsNull()
    {
        var grid = Grid(250, (0, 0, W, H));                       // art to the very edge

        Assert.Null(PageCropDetector.Detect(grid));
    }

    [Fact]
    public void Crop_MidGreyOrNonUniformBorder_IsLeftAlone()
    {
        Assert.Null(PageCropDetector.Detect(Grid(128, (60, 90, 280, 420))));   // a grey page is not a scan border

        var noisy = Grid(250, (60, 90, 280, 420));
        for (int i = 0; i < noisy.Luma.Length; i += 3)
        {
            noisy.Luma[i] = (byte)(i % 2 == 0 ? 30 : 250);        // a photo-like outer ring: no agreement on a border colour
        }

        Assert.Null(PageCropDetector.Detect(noisy));
    }

    [Fact]
    public void Crop_BlankPage_IsLeftAlone() => Assert.Null(PageCropDetector.Detect(Grid(250)));

    [Fact]
    public void Crop_MostlyBlankPage_IsNotApplied() =>
        Assert.Null(PageCropDetector.Detect(Grid(250, (170, 250, 60, 100))));   // one tiny panel in a big blank page

    [Fact]
    public void Crop_ASpeckOfDust_DoesNotStopTheWalk()
    {
        var grid = Grid(250, (60, 90, 280, 420));
        grid.Luma[(30 * W) + 200] = 0;                            // one dark pixel in the top border

        var crop = PageCropDetector.Detect(grid);

        Assert.NotNull(crop);
        Assert.True(crop!.Value.Top > 0.10);
    }

    [Fact]
    public void CropRect_ToSourceRect_ScalesTheInsets()
    {
        var rect = new PageCropRect(0.1, 0.05, 0.1, 0.05).ToSourceRect(1000, 2000);

        Assert.Equal(100, rect.Left);
        Assert.Equal(100, rect.Top);
        Assert.Equal(900, rect.Right);
        Assert.Equal(1900, rect.Bottom);
    }

    // ===== The pipeline =====

    /// <summary>A .cbz whose pages are white with a textured dark block in the middle (a scan with a border).</summary>
    private void CreateBorderedCbz(int pageCount = 3)
    {
        using var zip = ZipFile.Open(_cbzPath, ZipArchiveMode.Create);
        for (int i = 0; i < pageCount; i++)
        {
            using var bitmap = new Bitmap(W, H);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(250, 250, 250));
                using var a = new SolidBrush(Color.FromArgb(90, 90, 90));
                using var b = new SolidBrush(Color.FromArgb(150, 150, 150));
                for (int y = 90; y < 510; y += 6)
                {
                    for (int x = 60; x < 340; x += 6)
                    {
                        g.FillRectangle(((x / 6) + (y / 6)) % 2 == 0 ? a : b, x, y, 6, 6);
                    }
                }
            }

            var entry = zip.CreateEntry($"page_{i:D3}.png");
            using var stream = entry.Open();
            bitmap.Save(stream, ImageFormat.Png);
        }
    }

    [Fact]
    public void Pipeline_CropsTheBorder_WhenAutoCropIsOn_AndNotWhenItIsOff()
    {
        CreateBorderedCbz();
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        Assert.Equal(new Avalonia.PixelSize(W, H), pipeline.GetPage(0).PixelSize);

        pipeline.SetProcessing(new PageProcessingOptions(false, true), null);
        var cropped = pipeline.GetPage(0).PixelSize;

        Assert.True(cropped.Width < W - 40, $"width {cropped.Width}");
        Assert.True(cropped.Height < H - 60, $"height {cropped.Height}");
        Assert.True(cropped.Width > 250, $"width {cropped.Width}");           // and the art is still there

        pipeline.SetProcessing(PageProcessingOptions.None, null);
        Assert.Equal(new Avalonia.PixelSize(W, H), pipeline.GetPage(0).PixelSize);    // switching back decodes the page whole again
    }

    [Fact]
    public void Pipeline_PerPageOverride_BeatsTheSetting()
    {
        CreateBorderedCbz();
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        pipeline.SetProcessing(new PageProcessingOptions(false, true), new Dictionary<int, PageCropMode> { [1] = PageCropMode.Never });
        Assert.True(pipeline.GetPage(0).PixelSize.Width < W - 40);                    // follows the setting
        Assert.Equal(new Avalonia.PixelSize(W, H), pipeline.GetPage(1).PixelSize);    // "never" leaves it whole

        pipeline.SetProcessing(PageProcessingOptions.None, new Dictionary<int, PageCropMode> { [2] = PageCropMode.Always });
        Assert.Equal(new Avalonia.PixelSize(W, H), pipeline.GetPage(0).PixelSize);    // setting off: whole
        Assert.True(pipeline.GetPage(2).PixelSize.Width < W - 40);                    // "always" crops even so
    }

    [Fact]
    public void Pipeline_CropsBeforeTheDownscale_SoTheViewportWidthAppliesToTheCroppedPage()
    {
        CreateBorderedCbz();
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;
        pipeline.SetViewportWidth(100);
        pipeline.SetProcessing(new PageProcessingOptions(true, true), null);

        var display = pipeline.GetPage(0);

        Assert.Equal(100, display.PixelSize.Width);                                    // downscaled (the processed bitmap is a normal, resizable one)
        Assert.True(display.PixelSize.Height < 100 * H / W, $"height {display.PixelSize.Height}");   // and cropped: shorter than the whole page's aspect would give
        Assert.Same(display, pipeline.GetPage(0));                                     // cached under its processing variant
    }

    [Fact]
    public void Pipeline_DetailTier_UsesTheSameCropAsTheDisplayTier_AndThumbnailsAreNeverCropped()
    {
        CreateBorderedCbz();
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;
        pipeline.SetProcessing(new PageProcessingOptions(false, true), null);

        var display = pipeline.GetPage(0);
        double displayAspect = display.PixelSize.Width / (double)display.PixelSize.Height;
        var detail = pipeline.GetDetailPage(0, new Avalonia.PixelSize(display.PixelSize.Width * 2, display.PixelSize.Height * 2));
        pipeline.ReleaseDetail();
        Assert.Equal(display.PixelSize.Width * 2, detail.PixelSize.Width);

        // The detail bitmap shows the same part of the page: its content matches the display bitmap's (mean tone of the middle is the same to within resampling).
        Assert.Equal(displayAspect, detail.PixelSize.Width / (double)detail.PixelSize.Height, 2);

        var thumb = pipeline.GetThumbnail(0);
        Assert.Equal(W / (double)H, thumb.PixelSize.Width / (double)thumb.PixelSize.Height, 1);
    }

    [Fact]
    public void Pipeline_AutoLevels_StretchesAWashedOutPage_AndLeavesANormalOneAlone()
    {
        using (var zip = ZipFile.Open(_cbzPath, ZipArchiveMode.Create))
        {
            foreach (var (name, low, high) in new[] { ("washed.png", 96, 159), ("normal.png", 0, 255) })
            {
                using var bitmap = new Bitmap(64, 64);
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 64; x++)
                    {
                        int v = low + ((x * (high - low)) / 63);
                        bitmap.SetPixel(x, y, Color.FromArgb(v, v, v));
                    }
                }

                using var stream = zip.CreateEntry(name).Open();
                bitmap.Save(stream, ImageFormat.Png);
            }
        }

        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;
        pipeline.SetProcessing(new PageProcessingOptions(true, false), null);

        static (byte Min, byte Max) Range(Avalonia.Media.Imaging.Bitmap bitmap)
        {
            var size = bitmap.PixelSize;
            var raw = new uint[size.Width * size.Height];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(raw, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new Avalonia.PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), raw.Length * 4, size.Width * 4);
            }
            finally
            {
                handle.Free();
            }

            byte min = 255, max = 0;
            foreach (uint p in raw)
            {
                byte g = (byte)((p >> 8) & 0xFF);
                min = Math.Min(min, g);
                max = Math.Max(max, g);
            }

            return (min, max);
        }

        // Entries sort by name: normal.png is page 0, washed.png page 1.
        var normal = Range(pipeline.GetPage(0));
        var washed = Range(pipeline.GetPage(1));

        Assert.True(normal.Min <= 2 && normal.Max >= 253, $"normal {normal}");
        Assert.True(washed.Min < 80 && washed.Max > 180, $"washed {washed}");           // 96..159 stretched over CE's 0.25..0.75 window: about 64..190
    }
}
