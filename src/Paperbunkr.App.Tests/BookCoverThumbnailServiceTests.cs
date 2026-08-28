using System.Threading;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using SdBitmap = System.Drawing.Bitmap;
using SdColor = System.Drawing.Color;
using SdGraphics = System.Drawing.Graphics;
using SdImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Covers the custom-cover pair added to <see cref="BookCoverThumbnailService"/> for the Book
/// Properties editor (docs/superpowers/specs/2026-08-27-book-properties-editor-design.md).
/// Redirects <see cref="BookCoverThumbnailPaths.ThumbnailDirectory"/> to a temp folder, same
/// isolation as <see cref="CoverThumbnailServiceTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookCoverThumbnailServiceTests : IDisposable
{
    private readonly string _originalThumbnailDirectory;
    private readonly string _thumbnailDirectory;
    private readonly string _sourceImagePath;

    public BookCoverThumbnailServiceTests()
    {
        _originalThumbnailDirectory = BookCoverThumbnailPaths.ThumbnailDirectory;
        _thumbnailDirectory = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookthumbs_test_{Guid.NewGuid():N}");
        BookCoverThumbnailPaths.ThumbnailDirectory = _thumbnailDirectory;

        _sourceImagePath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookcover_src_{Guid.NewGuid():N}.png");
        using var bmp = new SdBitmap(300, 450);
        using (var g = SdGraphics.FromImage(bmp))
        {
            g.Clear(SdColor.CornflowerBlue);
        }
        bmp.Save(_sourceImagePath, SdImageFormat.Png);
    }

    public void Dispose()
    {
        BookCoverThumbnailPaths.ThumbnailDirectory = _originalThumbnailDirectory;
        try
        {
            if (File.Exists(_sourceImagePath)) File.Delete(_sourceImagePath);
            if (Directory.Exists(_thumbnailDirectory)) Directory.Delete(_thumbnailDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TrySetCustomCover_WritesADecodableJpeg()
    {
        var service = new BookCoverThumbnailService();

        bool ok = service.TrySetCustomCover(bookId: 5, _sourceImagePath);

        Assert.True(ok);
        string dest = BookCoverThumbnailPaths.GetCachePath(5);
        Assert.True(File.Exists(dest));
        using var decoded = new Bitmap(dest);
        Assert.True(decoded.PixelSize.Width > 0);
    }

    [Fact]
    public void TrySetCustomCover_Overwrites_UnlikeTryGenerate()
    {
        var service = new BookCoverThumbnailService();
        service.TrySetCustomCover(bookId: 6, _sourceImagePath);
        var firstWrite = File.GetLastWriteTimeUtc(BookCoverThumbnailPaths.GetCachePath(6));

        Thread.Sleep(10);
        bool ok = service.TrySetCustomCover(bookId: 6, _sourceImagePath);

        Assert.True(ok);
        Assert.True(File.GetLastWriteTimeUtc(BookCoverThumbnailPaths.GetCachePath(6)) > firstWrite);
    }

    [Fact]
    public void TrySetCustomCover_BadImagePath_ReturnsFalse_NoFile()
    {
        var service = new BookCoverThumbnailService();

        bool ok = service.TrySetCustomCover(bookId: 7, @"C:\nope\not-an-image.png");

        Assert.False(ok);
        Assert.False(File.Exists(BookCoverThumbnailPaths.GetCachePath(7)));
    }

    [Fact]
    public void ResetCover_Pdf_LeavesNoFile()
    {
        var service = new BookCoverThumbnailService();
        service.TrySetCustomCover(bookId: 8, _sourceImagePath);
        Assert.True(File.Exists(BookCoverThumbnailPaths.GetCachePath(8)));

        // A PDF with no real file on disk: ResetCover deletes the custom cover and can't regenerate.
        service.ResetCover(bookId: 8, filePath: @"C:\missing\book.pdf", format: BookFormat.Pdf);

        Assert.False(File.Exists(BookCoverThumbnailPaths.GetCachePath(8)));
    }
}
