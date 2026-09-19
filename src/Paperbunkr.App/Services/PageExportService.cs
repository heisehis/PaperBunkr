using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>Export format for <see cref="PageExportService.TryExport"/> (docs/superpowers/specs/2026-09-17-reader-save-page-and-cover-picker-design.md) - CE offers jpg/bmp/gif/tif/png; trimmed to the two anyone actually uses for a comic page, deliberately.</summary>
public enum PageExportFormat
{
    Png,
    Jpeg,
}

/// <summary>
/// Saves an already-rendered reader page/spread <see cref="Bitmap"/> to disk (docs/superpowers/
/// specs/2026-09-17-reader-save-page-and-cover-picker-design.md) - CE parity for "Save Page as"
/// (<c>_reference/ComicRackCE/ComicRack/MainForm.cs:2167,2308</c>). No decode step here - the
/// reader already holds the bitmap it's displaying; this just encodes it to a file. Same encoder
/// options shape <see cref="CoverThumbnailService"/> already uses for JPEG.
/// </summary>
public static class PageExportService
{
    private const int JpegQuality = 90;

    public static bool TryExport(Bitmap page, string destPath, PageExportFormat format)
    {
        try
        {
            if (format == PageExportFormat.Jpeg)
            {
                page.Save(destPath, new JpegBitmapEncoderOptions { Quality = JpegQuality });
            }
            else
            {
                page.Save(destPath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
