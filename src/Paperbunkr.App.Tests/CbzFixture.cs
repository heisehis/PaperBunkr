using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Builds a small synthetic .cbz (a handful of solid-color PNG pages, zipped) for testing
/// <see cref="Paperbunkr.App.Services.PageImageDecoder"/> against a real archive rather than a
/// mock - same "generate via the real code path" precedent as
/// Paperbunkr.Data.Tests.CeLibraryMigratorTests.
/// </summary>
internal static class CbzFixture
{
    public static string Create(string path, int pageCount)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var colors = new[] { Color.Firebrick, Color.SteelBlue, Color.Goldenrod, Color.SeaGreen, Color.Orchid };

        for (int i = 0; i < pageCount; i++)
        {
            var entry = zip.CreateEntry($"page_{i:D3}.png", CompressionLevel.Fastest);
            using var bitmap = new Bitmap(64, 96);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(colors[i % colors.Length]);
            }

            using var entryStream = entry.Open();
            bitmap.Save(entryStream, ImageFormat.Png);
        }

        return path;
    }
}
