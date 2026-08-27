using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using SdBitmap = System.Drawing.Bitmap;
using SdImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Covers the identity-validation behaviour <see cref="BookCoverThumbnailService"/> gained in
/// docs/superpowers/specs/2026-08-27-cover-thumbnail-identity-validation-design.md - fingerprinted
/// cache-file names and <see cref="BookCoverThumbnailService.GenerateAllAsync"/>'s orphan GC. The
/// actual cover-decode paths (EPUB embedded cover / PDF page 0) are exercised by
/// <c>EpubBookSourceTests</c> / <c>PdfBookSourceTests</c>; here we plant cache files directly so
/// the tests don't depend on a cover-bearing fixture. The fingerprint itself (shared with comics)
/// is covered by <see cref="CoverFingerprintTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookCoverThumbnailServiceTests : IDisposable
{
    private readonly string _originalDirectory;
    private readonly string _thumbnailDirectory;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

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
    }

    public void Dispose()
    {
        BookCoverThumbnailPaths.ThumbnailDirectory = _originalDirectory;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_thumbnailDirectory)) Directory.Delete(_thumbnailDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private BookCoverThumbnailService Service() => new(() => new PaperbunkrDbContext(_dbOptions));

    private int AddBook(string filePath)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var book = new Book { Title = "T", Format = BookFormat.Epub, FilePath = filePath };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private static void Plant(string stem)
    {
        using var bmp = new SdBitmap(4, 4);
        bmp.Save(BookCoverThumbnailPaths.GetCachePath(stem), SdImageFormat.Jpeg);
    }

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
