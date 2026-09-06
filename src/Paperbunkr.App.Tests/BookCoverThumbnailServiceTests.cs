using System.Threading;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Covers;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using SdBitmap = System.Drawing.Bitmap;
using SdColor = System.Drawing.Color;
using SdGraphics = System.Drawing.Graphics;
using SdImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="BookCoverThumbnailService"/> after the 2026-09-06 cover-durability root fix
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 2):
/// covers keyed by bare id (<c>{id}.jpg</c>), path changes never touch them, orphan cleanup attics
/// rather than deletes, custom covers in their own directory, mtime-smart verify.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookCoverThumbnailServiceTests : IDisposable
{
    private readonly string _originalDirectory;
    private readonly string _originalCustomDirectory;
    private readonly string _originalStateFile;
    private readonly string _thumbnailDirectory;
    private readonly string _customDirectory;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _sourceImagePath;

    public BookCoverThumbnailServiceTests()
    {
        _originalDirectory = BookCoverThumbnailPaths.ThumbnailDirectory;
        _originalCustomDirectory = CustomBookCoverPaths.Directory;
        _originalStateFile = CoverCacheState.FilePath;

        string root = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookthumbs_test_{Guid.NewGuid():N}");
        _thumbnailDirectory = Path.Combine(root, "book-thumbnails");
        _customDirectory = Path.Combine(root, "custom-book-covers");
        BookCoverThumbnailPaths.ThumbnailDirectory = _thumbnailDirectory;
        CustomBookCoverPaths.Directory = _customDirectory;
        CoverCacheState.FilePath = Path.Combine(root, "cover-cache-state.json");
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
        string root = Path.GetDirectoryName(_thumbnailDirectory)!;
        BookCoverThumbnailPaths.ThumbnailDirectory = _originalDirectory;
        CustomBookCoverPaths.Directory = _originalCustomDirectory;
        CoverCacheState.FilePath = _originalStateFile;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_sourceImagePath)) File.Delete(_sourceImagePath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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

    private static void Plant(int id)
    {
        using var bmp = new SdBitmap(4, 4);
        bmp.Save(BookCoverThumbnailPaths.GetCachePath(id), SdImageFormat.Jpeg);
    }

    // --- Custom cover -> its own directory, never swept ---

    [Fact]
    public void TrySetCustomCover_WritesADecodableJpeg_ToTheCustomDirectory()
    {
        int bookId = AddBook(@"C:\books\5.epub");

        bool ok = Service().TrySetCustomCover(bookId, _sourceImagePath);

        Assert.True(ok);
        Assert.True(CustomBookCoverPaths.Exists(bookId));
        using var decoded = new Avalonia.Media.Imaging.Bitmap(CustomBookCoverPaths.GetCachePath(bookId));
        Assert.True(decoded.PixelSize.Width > 0);
    }

    [Fact]
    public void TrySetCustomCover_Overwrites_UnlikeTryGenerate()
    {
        int bookId = AddBook(@"C:\books\6.epub");
        var service = Service();
        service.TrySetCustomCover(bookId, _sourceImagePath);
        var firstWrite = File.GetLastWriteTimeUtc(CustomBookCoverPaths.GetCachePath(bookId));

        Thread.Sleep(10);
        bool ok = service.TrySetCustomCover(bookId, _sourceImagePath);

        Assert.True(ok);
        Assert.True(File.GetLastWriteTimeUtc(CustomBookCoverPaths.GetCachePath(bookId)) > firstWrite);
    }

    [Fact]
    public void TrySetCustomCover_BadImagePath_ReturnsFalse_NoFile()
    {
        int bookId = AddBook(@"C:\books\7.epub");

        bool ok = Service().TrySetCustomCover(bookId, @"C:\nope\not-an-image.png");

        Assert.False(ok);
        Assert.False(CustomBookCoverPaths.Exists(bookId));
    }

    [Fact]
    public void ResetCover_Pdf_LeavesNoFile()
    {
        int bookId = AddBook(@"C:\missing\book.pdf", BookFormat.Pdf);
        var service = Service();
        service.TrySetCustomCover(bookId, _sourceImagePath);
        Assert.True(CustomBookCoverPaths.Exists(bookId));

        service.ResetCover(bookId, filePath: @"C:\missing\book.pdf", format: BookFormat.Pdf);

        Assert.False(CustomBookCoverPaths.Exists(bookId));
        Assert.False(File.Exists(BookCoverThumbnailPaths.GetCachePath(bookId)));
    }

    // --- Orphan cleanup: attic (not delete), keyed by id, never touches a live book's cover ---

    [Fact]
    public async Task GenerateAllAsync_KeepsALiveBooksCover_AndAtticsAnIdLessOne()
    {
        int bookId = AddBook(@"C:\books\current.epub");
        Plant(bookId);
        Plant(999999); // no such book row

        await Service().GenerateAllAsync(new Progress<(int Done, int Total)>());

        Assert.True(File.Exists(BookCoverThumbnailPaths.GetCachePath(bookId)));
        Assert.False(File.Exists(BookCoverThumbnailPaths.GetCachePath(999999)));
        Assert.Contains(BookCoverThumbnailPaths.EnumerateAttic(), p => Path.GetFileName(p).StartsWith("999999.", StringComparison.Ordinal));
    }

    [Fact]
    public void Get_ServesByBareId_RegardlessOfPath()
    {
        int bookId = 77;
        Plant(bookId);

        Assert.NotNull(BookCoverImageCache.Get(bookId, @"C:\books\a.epub"));
        Assert.Same(
            BookCoverImageCache.Get(bookId, @"C:\books\a.epub"),
            BookCoverImageCache.Get(bookId, @"C:\books\moved-elsewhere.epub"));
    }

    [Fact]
    public void Invalidate_DeletesEveryOnDiskFileForThatId()
    {
        int bookId = 88;
        Plant(bookId);
        using (var bmp = new SdBitmap(4, 4))
        {
            bmp.Save(BookCoverThumbnailPaths.GetCachePath("88-0badc0de"), SdImageFormat.Jpeg); // a legacy straggler
        }

        Assert.Equal(2, BookCoverThumbnailPaths.EnumerateForBook(bookId).Count());

        BookCoverImageCache.Invalidate(bookId);

        Assert.Empty(BookCoverThumbnailPaths.EnumerateForBook(bookId));
    }

    // --- mtime-smart verify ---

    [Fact]
    public void TryGenerateThumbnail_Force_OverwritesEvenWhenFileAlreadyExists()
    {
        string pdfPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookcover_verify_{Guid.NewGuid():N}.pdf");
        PdfFixture.Create(pdfPath, "Hello from page one.");
        try
        {
            int bookId = AddBook(pdfPath, BookFormat.Pdf);
            var service = Service();
            Assert.True(service.TryGenerateThumbnail(bookId, pdfPath, BookFormat.Pdf));
            var firstWrite = File.GetLastWriteTimeUtc(BookCoverThumbnailPaths.GetCachePath(bookId));

            Thread.Sleep(10);
            Assert.True(service.TryGenerateThumbnail(bookId, pdfPath, BookFormat.Pdf, force: true));

            Assert.True(File.GetLastWriteTimeUtc(BookCoverThumbnailPaths.GetCachePath(bookId)) > firstWrite);
        }
        finally
        {
            try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task VerifyAllAsync_ReDecodesOnlyWhenTheSourceIsNewer_AndAtticsOrphans()
    {
        string pdfPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookcover_verify2_{Guid.NewGuid():N}.pdf");
        PdfFixture.Create(pdfPath, "Hello from page one.");
        try
        {
            int unchangedId = AddBook(pdfPath, BookFormat.Pdf);
            Service().TryGenerateThumbnail(unchangedId, pdfPath, BookFormat.Pdf);
            string unchangedPath = BookCoverThumbnailPaths.GetCachePath(unchangedId);
            File.SetLastWriteTimeUtc(unchangedPath, DateTime.UtcNow.AddHours(1));
            var unchangedWrite = File.GetLastWriteTimeUtc(unchangedPath);

            int changedId = AddBook(pdfPath, BookFormat.Pdf);
            Service().TryGenerateThumbnail(changedId, pdfPath, BookFormat.Pdf);
            string changedPath = BookCoverThumbnailPaths.GetCachePath(changedId);
            File.SetLastWriteTimeUtc(changedPath, DateTime.UtcNow.AddHours(-2));
            var changedWriteBefore = File.GetLastWriteTimeUtc(changedPath);

            Plant(99999); // orphan

            await Service().VerifyAllAsync(new Progress<(int Done, int Total)>());

            Assert.Equal(unchangedWrite, File.GetLastWriteTimeUtc(unchangedPath));
            Assert.True(File.GetLastWriteTimeUtc(changedPath) > changedWriteBefore);
            Assert.False(File.Exists(BookCoverThumbnailPaths.GetCachePath(99999)));
        }
        finally
        {
            try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task RepairMissingAsync_RegeneratesOnlyBlankCoversWithAReadableSource()
    {
        string pdfPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookcover_repair_{Guid.NewGuid():N}.pdf");
        PdfFixture.Create(pdfPath, "Hello from page one.");
        try
        {
            int blankWithSource = AddBook(pdfPath, BookFormat.Pdf);
            int blankNoSource = AddBook(@"C:\gone\missing.pdf", BookFormat.Pdf);

            await Service().RepairMissingAsync(new Progress<(int Done, int Total)>());

            Assert.True(File.Exists(BookCoverThumbnailPaths.GetCachePath(blankWithSource)));
            Assert.False(File.Exists(BookCoverThumbnailPaths.GetCachePath(blankNoSource)));
        }
        finally
        {
            try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch (IOException) { }
        }
    }
}
