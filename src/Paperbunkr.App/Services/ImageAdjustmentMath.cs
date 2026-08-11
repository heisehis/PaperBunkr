using System;

namespace Paperbunkr.App.Services;

/// <summary>
/// Ported from ComicRackCE's <c>ImageProcessing.CreateColorMatrix</c>/<c>CreateColorScaleMatrix</c>/
/// <c>CreateColorSaturationMatrix</c> (_reference/ComicRackCE/cYo.Common/Drawing/ImageProcessing.cs)
/// and <c>ChangeGamma</c>'s LUT construction (same file), docs/superpowers/specs/2026-08-10-reader-
/// polish-continuous-scroll-chrome-overlays-design.md §9. AutoContrast/WhitePoint are skipped (the
/// spec's named deviations), so CE's <c>blackLevel</c>/<c>whiteLevel</c> are always 0/1 here -
/// simplifying CE's own <c>scale = (contrast+1)/(whiteLevel-blackLevel)</c> to just
/// <c>contrast+1</c>, and <c>offset = brightness-blackLevel</c> to just <c>brightness</c>.
///
/// Output is SkiaSharp's own 4x5 row-major <c>SKColorFilter.CreateColorMatrix</c> layout directly
/// (row = output R/G/B/A, col = input R/G/B/A/1) - not CE's 5x5 GDI+ <c>ColorMatrix</c> layout,
/// since the two conventions differ (GDI+ is a row-vector "input * matrix" transform with an extra
/// homogeneous row/col, and CE's own <c>ApplyColorMatrix</c> folds the additive offset in via
/// "alpha_in(255 for opaque) * matrix[3,x]" - a byte-level trick specific to that 5x5 mechanism).
/// The translation column is normalized -1.0..1.0, not 0..255 - confirmed by manual testing against
/// this project's actual SkiaSharp 3.119.4 binary, not the package's own (Android-target-only, the
/// sole SkiaSharp.xml actually shipped) doc comment, which describes the legacy 0..255 convention
/// and turned out to be stale for this version/overload. The underlying scale/saturation math itself
/// is ported faithfully from CE; only the matrix layout and translation-column scale are Skia-native.
/// </summary>
public static class ImageAdjustmentMath
{
    private const float GrayRed = 0.3086f;
    private const float GrayGreen = 0.6094f;
    private const float GrayBlue = 0.082f;

    /// <summary>
    /// <paramref name="brightness"/>/<paramref name="contrast"/>/<paramref name="saturation"/> are
    /// each -100..100 - the same raw units this codebase's <c>AppSettings</c>/<c>Issue</c> columns,
    /// <c>ReaderScreenViewModel</c>, and <c>PageCanvas</c>'s bindable properties all carry end to
    /// end (matching the Preferences/toolbar slider range directly, spec §9's "each −100..100
    /// matching CE's exact PreferencesDialog trackbar range"), normalized to CE's own -1.0..1.0
    /// internal scale only here, right before use - matching CE's own <c>PreferencesDialog.cs</c>
    /// (<c>tbBrightness.Value / 100f</c>, etc.) doing the same conversion once, at its own point of
    /// use, rather than storing the normalized value anywhere.
    /// </summary>
    public static float[] CreateColorMatrix(double brightness, double contrast, double saturation)
    {
        double brightnessNormalized = brightness / 100.0;
        double contrastNormalized = contrast / 100.0;
        double saturationNormalized = saturation / 100.0;

        float scale = (float)(contrastNormalized + 1.0);
        // Real bug, found via manual testing: SkiaSharp's CreateColorMatrix(float[]) doc comment
        // (only shipped for an Android-target build of the package, the sole SkiaSharp.xml actually
        // present in the 3.119.4 NuGet package) says the translation column is "unnormalized, 0...255
        // space" - matching CE's own GDI+ ColorMatrix convention this was ported from. That's stale
        // for this SkiaSharp major version's actual native binding: multiplying by 255 here made
        // *any* non-trivial brightness value overshoot the real [-1, 1] range the translation column
        // expects, hard-clamping to solid black/white the moment the slider moved barely off zero -
        // contrast/saturation never hit this because their own coefficients are small multiplicative
        // scale factors (~0..2) regardless of which convention is in effect, so only brightness (the
        // one channel using the offset/translation column at all) was affected. The normalized
        // brightness value IS the correct translation value directly - no further scaling.
        float offset = (float)brightnessNormalized;
        float sat = (float)(saturationNormalized + 1.0);

        // CE: CreateColorScaleMatrix(scale, offset) - uniform R/G/B scale + additive offset.
        var scaleLinear = new float[3, 3] { { scale, 0, 0 }, { 0, scale, 0 }, { 0, 0, scale } };
        var scaleOffset = new float[3] { offset, offset, offset };

        // CE: CreateColorSaturationMatrix(sat) - standard luminance-preserving saturation matrix.
        var saturationLinear = new float[3, 3]
        {
            { ((1 - sat) * GrayRed) + sat, (1 - sat) * GrayGreen, (1 - sat) * GrayBlue },
            { (1 - sat) * GrayRed, ((1 - sat) * GrayGreen) + sat, (1 - sat) * GrayBlue },
            { (1 - sat) * GrayRed, (1 - sat) * GrayGreen, ((1 - sat) * GrayBlue) + sat },
        };

        // CE composes CreateColorScaleMatrix(...) * CreateColorSaturationMatrix(...) in its own
        // row-vector convention - scale is applied first, then saturation. Composing two affine
        // transforms T2(T1(x)) where T1(x)=A1*x+b1, T2(y)=A2*y+b2 gives (A2*A1)*x + (A2*b1+b2);
        // saturation's own offset (b2) is zero, so just A2*b1 here.
        var combinedLinear = new float[3, 3];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                combinedLinear[r, c] = (saturationLinear[r, 0] * scaleLinear[0, c])
                                      + (saturationLinear[r, 1] * scaleLinear[1, c])
                                      + (saturationLinear[r, 2] * scaleLinear[2, c]);
            }
        }

        var combinedOffset = new float[3];
        for (int r = 0; r < 3; r++)
        {
            combinedOffset[r] = (saturationLinear[r, 0] * scaleOffset[0])
                               + (saturationLinear[r, 1] * scaleOffset[1])
                               + (saturationLinear[r, 2] * scaleOffset[2]);
        }

        return new float[]
        {
            combinedLinear[0, 0], combinedLinear[0, 1], combinedLinear[0, 2], 0, combinedOffset[0],
            combinedLinear[1, 0], combinedLinear[1, 1], combinedLinear[1, 2], 0, combinedOffset[1],
            combinedLinear[2, 0], combinedLinear[2, 1], combinedLinear[2, 2], 0, combinedOffset[2],
            0, 0, 0, 1, 0,
        };
    }

    /// <summary>
    /// Ported from <c>ChangeGamma</c>'s LUT construction (same CE file) - CE clamps the effective
    /// gamma value to [0.2, 5.0] before building the table. <paramref name="gamma"/> is -100..100,
    /// same raw slider-unit convention as <see cref="CreateColorMatrix"/>; CE feeds
    /// <c>ChangeGamma(1f + adjustment.Gamma)</c> (normalized), i.e. gamma=0 is the identity table.
    /// </summary>
    public static byte[] CreateGammaTable(double gamma)
    {
        double gammaValue = Math.Clamp(1.0 + (gamma / 100.0), 0.2, 5.0);
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            table[i] = (byte)Math.Min(255.0, (255.0 * Math.Pow(i / 255.0, 1.0 / gammaValue)) + 0.5);
        }

        return table;
    }

    /// <summary>A fully-transparent-to-adjustment table (output == input) - used for the alpha channel when composing a gamma <c>SKColorFilter.CreateTable</c>, which requires all four per-channel tables even though only R/G/B are meant to change.</summary>
    public static byte[] CreateIdentityTable()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            table[i] = (byte)i;
        }

        return table;
    }

    /// <summary>No-op check so callers can skip building/applying any filter at all for the common "nothing adjusted" case.</summary>
    public static bool IsIdentity(double brightness, double contrast, double saturation, double gamma) =>
        brightness == 0 && contrast == 0 && saturation == 0 && gamma == 0;
}
