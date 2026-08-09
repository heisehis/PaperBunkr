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
    /// <summary>
    /// <paramref name="comicInfo"/> optionally embeds a real ComicInfo.xml entry (serialized via
    /// the engine's own <c>ComicInfo.ToArray()</c>, the same writer CE itself uses), for tests
    /// exercising embedded-metadata reading - same "generate via the real code path" precedent as
    /// the rest of this fixture.
    /// </summary>
    public static string Create(string path, int pageCount, cYo.Projects.ComicRack.Engine.ComicInfo? comicInfo = null)
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

        if (comicInfo is not null)
        {
            byte[] xml = comicInfo.ToArray();
            var infoEntry = zip.CreateEntry("ComicInfo.xml", CompressionLevel.Fastest);
            using var infoStream = infoEntry.Open();
            infoStream.Write(xml, 0, xml.Length);
        }

        return path;
    }
}
