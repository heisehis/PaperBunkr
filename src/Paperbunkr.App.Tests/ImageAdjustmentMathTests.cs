using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ImageAdjustmentMath"/>'s port of ComicRackCE's
/// ImageProcessing.CreateColorMatrix/CreateColorScaleMatrix/CreateColorSaturationMatrix/ChangeGamma
/// (_reference/ComicRackCE/cYo.Common/Drawing/ImageProcessing.cs), same "plain values, no app
/// context needed" shape as <see cref="ZoomPanMathTests"/>. Values hand-derived from the CE source
/// rather than just re-running the implementation, per docs/superpowers/specs/2026-08-10-reader-
/// polish-continuous-scroll-chrome-overlays-design.md §13's "spot-check against CE's known outputs"
/// testing approach.
/// </summary>
public class ImageAdjustmentMathTests
{
    [Fact]
    public void CreateColorMatrix_AllZero_ReturnsSkiaIdentity()
    {
        var matrix = ImageAdjustmentMath.CreateColorMatrix(brightness: 0, contrast: 0, saturation: 0);

        Assert.Equal(new float[]
        {
            1, 0, 0, 0, 0,
            0, 1, 0, 0, 0,
            0, 0, 1, 0, 0,
            0, 0, 0, 1, 0,
        }, matrix);
    }

    [Fact]
    public void CreateColorMatrix_Brightness100_AddsFullNormalizedOffset_ToEveryChannel()
    {
        // brightness=100 -> normalized 1.0 -> CE's offset = brightness-blackLevel = 1.0. Real bug,
        // found via manual testing: this used to multiply by 255 here (matching CE's own GDI+
        // ColorMatrix convention, and the Android-target-only SkiaSharp.xml doc comment, both of
        // which describe a 0..255 translation column) - this SkiaSharp version's actual
        // CreateColorMatrix(float[]) binding expects the translation column normalized -1.0..1.0
        // instead, so *255 hard-clamped every non-trivial brightness value to solid black/white.
        var matrix = ImageAdjustmentMath.CreateColorMatrix(brightness: 100, contrast: 0, saturation: 0);

        Assert.Equal(1f, matrix[4]); // R row's offset
        Assert.Equal(1f, matrix[9]); // G row's offset
        Assert.Equal(1f, matrix[14]); // B row's offset
        Assert.Equal(1, matrix[18]); // alpha row untouched (alpha->alpha coefficient)
    }

    [Fact]
    public void CreateColorMatrix_ContrastMinus100_ZerosOutTheScale()
    {
        // contrast=-100 -> normalized -1.0 -> CE's scale = contrast+1 = 0 (blackLevel/whiteLevel
        // fixed at 0/1 since AutoContrast is skipped) - every R/G/B coefficient becomes 0.
        var matrix = ImageAdjustmentMath.CreateColorMatrix(brightness: 0, contrast: -100, saturation: 0);

        Assert.Equal(0, matrix[0]); // R->R
        Assert.Equal(0, matrix[6]); // G->G
        Assert.Equal(0, matrix[12]); // B->B
        Assert.Equal(0, matrix[4]); // no brightness offset either
    }

    [Fact]
    public void CreateColorMatrix_SaturationMinus100_ProducesGrayscaleWeights()
    {
        // saturation=-100 -> normalized -1.0 -> CE's sat = saturation+1 = 0 - with brightness/
        // contrast at their identity defaults, the composed matrix collapses to exactly the
        // saturation matrix's own gray-weight rows (CreateColorSaturationMatrix(0)).
        var matrix = ImageAdjustmentMath.CreateColorMatrix(brightness: 0, contrast: 0, saturation: -100);

        Assert.Equal(0.3086f, matrix[0], 4); // R row: grayRed
        Assert.Equal(0.6094f, matrix[1], 4); // R row: grayGreen
        Assert.Equal(0.082f, matrix[2], 4); // R row: grayBlue
        Assert.Equal(0.3086f, matrix[5], 4); // G row: grayRed
        Assert.Equal(0.6094f, matrix[6], 4); // G row: grayGreen
        Assert.Equal(0.082f, matrix[7], 4); // G row: grayBlue
    }

    [Fact]
    public void CreateGammaTable_ZeroGamma_IsIdentityTable()
    {
        var table = ImageAdjustmentMath.CreateGammaTable(0);

        for (int i = 0; i < 256; i++)
        {
            Assert.Equal((byte)i, table[i]);
        }
    }

    [Fact]
    public void CreateGammaTable_EndpointsAlwaysMapToThemselves_RegardlessOfGamma()
    {
        // (0/255)^x = 0 and (255/255)^x = 1 for any exponent - the LUT's endpoints are gamma-invariant.
        var table = ImageAdjustmentMath.CreateGammaTable(75);

        Assert.Equal(0, table[0]);
        Assert.Equal(255, table[255]);
    }

    [Fact]
    public void CreateGammaTable_ClampsExtremeGamma_ToCEsOwnZeroPointTwoToFiveRange()
    {
        // gamma=-1000 -> 1+(-10) = -9, clamped to CE's own [0.2, 5.0] floor - shouldn't throw or
        // produce a degenerate (NaN/negative-exponent) table.
        var table = ImageAdjustmentMath.CreateGammaTable(-1000);

        Assert.Equal(0, table[0]);
        Assert.Equal(255, table[255]);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, true)]
    [InlineData(1, 0, 0, 0, false)]
    [InlineData(0, -1, 0, 0, false)]
    [InlineData(0, 0, 1, 0, false)]
    [InlineData(0, 0, 0, 1, false)]
    public void IsIdentity_TrueOnlyWhenEveryChannelIsZero(double brightness, double contrast, double saturation, double gamma, bool expected)
    {
        Assert.Equal(expected, ImageAdjustmentMath.IsIdentity(brightness, contrast, saturation, gamma));
    }
}
