using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using SdBitmap = System.Drawing.Bitmap;
using SdColor = System.Drawing.Color;
using SdGraphics = System.Drawing.Graphics;
using SdImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Covers both the custom-cover pair added for the Book Properties editor (docs/superpowers/specs/
/// 2026-08-27-book-properties-editor-design.md) and the identity-validation behaviour
/// <see cref="BookCoverThumbnailService"/> gained in docs/superpowers/specs/2026-08-27-cover-
/// thumbnail-identity-validation-design.md - fingerprinted cache-file names and
/// <see cref="BookCoverThumbnailService.GenerateAllAsync"/>'s orphan GC. Custom-cover paths now
/// resolve their stem from the database (mirrors <see cref="CoverThumbnailServiceTests"/>'s
/// equivalent for comics), hence the DB-context fixture. The actual auto-cover-decode paths (EPUB
/// embedded cover / PDF page 0) are exercised by <c>EpubBookSourceTests</c> / <c>PdfBookSourceTests</c>;
/// here we plant cache files directly for the identity tests so they don't depend on a
/// cover-bearing fixture. The fingerprint itself (shared with comics) is covered by
/// <see cref="CoverFingerprintTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookCoverThumbnailServiceTests : IDisposable
{
    private readonly string _originalDirectory;
    private readonly string _thumbnailDirectory;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _sourceImagePath;

    public BookCoverThumbnailServiceTests()
    {
        _originalDirectory = BookCoverThumbnailPaths.ThumbnailDirectory;
        _thumbnailDirectory = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookthumbs_test_{Guid.NewGuid():N}");
        BookCoverThumbnailPaths.ThumbnailDirectory = _thumbnailDirectory;
        Directory.CreateDirectory(_thumbnailDirectory);

        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookcover_db_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var seed = new PaperbunkrDbContext(_dbOptions);
        seed.Database.EnsureCreated();

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
        BookCoverThumbnailPaths.ThumbnailDirectory = _originalDirectory;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_sourceImagePath)) File.Delete(_sourceImagePath);
            if (Directory.Exists(_thumbnailDirectory)) Directory.Delete(_thumbnailDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private BookCoverThumbnailService Service() => new(() => new PaperbunkrDbContext(_dbOptions));

    private int AddBook(string filePath, BookFormat format = BookFormat.Epub)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var book = new Book { Title = "T", Format = format, FilePath = filePath };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private static void Plant(string stem)
    {
        using var bmp = new SdBitmap(4, 4);
        bmp.Save(BookCoverThumbnailPaths.GetCachePath(stem), SdImageFormat.Jpeg);
    }

    // --- Custom cover (Book Properties editor) - stem resolved from the database, mirroring
    //     CoverThumbnailServiceTests' equivalent coverage for comics. ---

    [Fact]
    public void TrySetCustomCover_WritesADecodableJpeg()
    {
        int bookId = AddBook(@"C:\books\5.epub");
        var service = Service();

        bool ok = service.TrySetCustomCover(bookId, _sourceImagePath);

        Assert.True(ok);
        string dest = BookCoverThumbnailPaths.GetCachePath(CoverFingerprint.Stem(bookId, @"C:\books\5.epub", null));
        Assert.True(File.Exists(dest));
        using var decoded = new Avalonia.Media.Imaging.Bitmap(dest);
        Assert.True(decoded.PixelSize.Width > 0);
    }

    [Fact]
    public void TrySetCustomCover_Overwrites_UnlikeTryGenerate()
    {
        int bookId = AddBook(@"C:\books\6.epub");
        string stem = CoverFingerprint.Stem(bookId, @"C:\books\6.epub", null);
        var service = Service();
        service.TrySetCustomCover(bookId, _sourceImagePath);
        var firstWrite = File.GetLastWriteTimeUtc(BookCoverThumbnailPaths.GetCachePath(stem));

        Thread.Sleep(10);
        bool ok = service.TrySetCustomCover(bookId, _sourceImagePath);

        Assert.True(ok);
        Assert.True(File.GetLastWriteTimeUtc(BookCoverThumbnailPaths.GetCachePath(stem)) > firstWrite);
    }

    [Fact]
    public void TrySetCustomCover_BadImagePath_ReturnsFalse_NoFile()
    {
        int bookId = AddBook(@"C:\books\7.epub");
        string stem = CoverFingerprint.Stem(bookId, @"C:\books\7.epub", null);
        var service = Service();

        bool ok = service.TrySetCustomCover(bookId, @"C:\nope\not-an-image.png");

        Assert.False(ok);
        Assert.False(File.Exists(BookCoverThumbnailPaths.GetCachePath(stem)));
    }

    [Fact]
    public void ResetCover_Pdf_LeavesNoFile()
    {
        int bookId = AddBook(@"C:\missing\book.pdf", BookFormat.Pdf);
        string stem = CoverFingerprint.Stem(bookId, @"C:\missing\book.pdf", null);
        var service = Service();
        service.TrySetCustomCover(bookId, _sourceImagePath);
        Assert.True(File.Exists(BookCoverThumbnailPaths.GetCachePath(stem)));

        // A PDF with no real file on disk: ResetCover deletes the custom cover and can't regenerate.
        service.ResetCover(bookId, filePath: @"C:\missing\book.pdf", format: BookFormat.Pdf);

        Assert.False(File.Exists(BookCoverThumbnailPaths.GetCachePath(stem)));
    }

    // --- Identity validation (docs/superpowers/specs/2026-08-27-cover-thumbnail-identity-
    //     validation-design.md) - fingerprinted cache-file names and orphan GC. ---

    [Fact]
    public async Task GenerateAllAsync_GarbageCollectsOrphans_ButKeepsValidStems()
    {
        int bookId = AddBook(@"C:\books\current.epub");
        string validStem = CoverFingerprint.Stem(bookId, @"C:\books\current.epub", null);
        Plant(validStem);

        // Orphans: a cover for a since-reassigned id, and a pre-rework bare-id file.
        string reusedIdOrphan = CoverFingerprint.Stem(bookId, @"C:\books\old-different.epub", null);
        Plant(reusedIdOrphan);
        Plant($"{bookId}"); // legacy "{id}.jpg"

        await Service().GenerateAllAsync(new Progress<(int Done, int Total)>());

        Assert.True(File.Exists(BookCoverThumbnailPaths.GetCachePath(validStem)));
        Assert.False(File.Exists(BookCoverThumbnailPaths.GetCachePath(reusedIdOrphan)));
        Assert.False(File.Exists(Path.Combine(_thumbnailDirectory, $"{bookId}.jpg")));
    }

    [Fact]
    public void Get_ReturnsNull_WhenOnlyAMismatchedStemFileExistsForThatId()
    {
        int bookId = 77;
        Plant(CoverFingerprint.Stem(bookId, @"C:\books\a.epub", null));

        Assert.NotNull(BookCoverImageCache.Get(bookId, @"C:\books\a.epub"));
        Assert.Null(BookCoverImageCache.Get(bookId, @"C:\books\b.epub"));
    }

    [Fact]
    public void Invalidate_DeletesEveryOnDiskFileForThatId()
    {
        int bookId = 88;
        Plant(CoverFingerprint.Stem(bookId, @"C:\books\a.epub", null));
        Plant($"{bookId}-0badc0de");
        Assert.Equal(2, BookCoverThumbnailPaths.EnumerateForBook(bookId).Count());

        BookCoverImageCache.Invalidate(bookId);

        Assert.Empty(BookCoverThumbnailPaths.EnumerateForBook(bookId));
    }
}
