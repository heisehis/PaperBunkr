using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PageExportService"/> (docs/superpowers/specs/2026-09-17-reader-save-page-
/// and-cover-picker-design.md) - PNG/JPEG round-trip from a real decoded bitmap, no reader/DB
/// dependency needed. Same <see cref="AvaloniaTestCollection"/> rationale as
/// <see cref="ArcCoverImageCacheTests"/> (Bitmap construction needs a registered
/// IPlatformRenderInterface).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PageExportServiceTests : IDisposable
{
    private readonly string _dir;

    public PageExportServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_pageexport_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Real System.Drawing-rendered source PNG, then reopened as an Avalonia Bitmap - the shape <see cref="PageExportService.TryExport"/> actually receives from the reader.</summary>
    private AvaloniaBitmap MakeSourceBitmap(int width = 64, int height = 48)
    {
        string sourcePath = Path.Combine(_dir, "source.png");
        using (var bitmap = new System.Drawing.Bitmap(width, height))
        {
            using var g = System.Drawing.Graphics.FromImage(bitmap);
            g.Clear(System.Drawing.Color.SteelBlue);
            bitmap.Save(sourcePath, System.Drawing.Imaging.ImageFormat.Png);
        }

        return new AvaloniaBitmap(sourcePath);
    }

    [Fact]
    public void TryExport_Png_ProducesReadableFile()
    {
        using var bitmap = MakeSourceBitmap();
        string path = Path.Combine(_dir, "page.png");

        bool ok = PageExportService.TryExport(bitmap, path, PageExportFormat.Png);

        Assert.True(ok);
        Assert.True(File.Exists(path));
        using var reopened = new AvaloniaBitmap(path);
        Assert.Equal(64, reopened.PixelSize.Width);
        Assert.Equal(48, reopened.PixelSize.Height);
    }

    [Fact]
    public void TryExport_Jpeg_ProducesReadableFile()
    {
        using var bitmap = MakeSourceBitmap();
        string path = Path.Combine(_dir, "page.jpg");

        bool ok = PageExportService.TryExport(bitmap, path, PageExportFormat.Jpeg);

        Assert.True(ok);
        Assert.True(File.Exists(path));
        using var reopened = new AvaloniaBitmap(path);
        Assert.Equal(64, reopened.PixelSize.Width);
        Assert.Equal(48, reopened.PixelSize.Height);
    }
}
