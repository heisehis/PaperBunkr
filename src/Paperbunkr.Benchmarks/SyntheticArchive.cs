using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;

namespace Paperbunkr.Benchmarks;

/// <summary>
/// Procedurally-built comic archives for the pipeline benchmarks (docs/superpowers/specs/
/// 2026-09-08-reader-decode-cache-prefetch-pipeline-design.md §12.1) - nothing binary is checked
/// into the repo. Pages are JPEG-encoded noise at a few real resolution profiles so decode cost is
/// representative, not a trivial solid-colour fast-path.
/// </summary>
public static class SyntheticArchive
{
    public enum Profile
    {
        /// <summary>1600x2560, manga page.</summary>
        Manga,
        /// <summary>2048x3072, US comic scan.</summary>
        Comic,
        /// <summary>800x12000, webtoon vertical strip.</summary>
        WebtoonStrip
    }

    public static (int Width, int Height) Dimensions(Profile p) => p switch
    {
        Profile.Manga => (1600, 2560),
        Profile.Comic => (2048, 3072),
        Profile.WebtoonStrip => (800, 12000),
        _ => (1600, 2560)
    };

    /// <summary>Writes a .cbz of <paramref name="pageCount"/> JPEG pages at the given profile; returns the path.</summary>
    public static string CreateCbz(string dir, Profile profile, int pageCount)
    {
        Directory.CreateDirectory(dir);
        var (w, h) = Dimensions(profile);
        string path = Path.Combine(dir, $"synthetic_{profile}_{pageCount}p_{w}x{h}.cbz");
        if (File.Exists(path))
        {
            return path;
        }

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var rng = new Random(1234);
        var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        using var jpegParams = new EncoderParameters(1);
        jpegParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);

        for (int i = 0; i < pageCount; i++)
        {
            using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)));
                // A few gradient bands so the JPEG has real high-frequency content to decode.
                for (int band = 0; band < 24; band++)
                {
                    int y = band * h / 24;
                    using var brush = new SolidBrush(Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)));
                    g.FillRectangle(brush, 0, y, w, h / 48);
                }
            }

            var entry = zip.CreateEntry($"page_{i:D4}.jpg", CompressionLevel.NoCompression);
            using var entryStream = entry.Open();
            bmp.Save(entryStream, encoder, jpegParams);
        }

        return path;
    }
}
