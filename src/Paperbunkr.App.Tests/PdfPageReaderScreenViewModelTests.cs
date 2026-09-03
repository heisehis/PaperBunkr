using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PdfPageReaderScreenViewModel"/> (the comic-panel-style PDF reader,
/// docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §5 follow-up - reflowed
/// text extraction for PDFs proved unreliable enough in manual testing to route PDFs through the
/// existing comic page-image pipeline instead).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PdfPageReaderScreenViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _pdfPath;

    public PdfPageReaderScreenViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pdf_reader_vm_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _pdfPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pdf_reader_vm_test_{Guid.NewGuid():N}.pdf");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        PaperbunkrDbContext.DatabasePathOverride = null;
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_pdfPath)) File.Delete(_pdfPath);
        }
        catch (IOException)
        {
        }
    }

    private int AddBook(int pageCount)
    {
        PdfFixture.Create(_pdfPath, Enumerable.Range(1, pageCount).Select(i => $"Page {i} text.").ToArray());
        using var context = new PaperbunkrDbContext(_dbOptions);
        var book = new Book { Title = "Test PDF", FilePath = _pdfPath, Format = BookFormat.Pdf, AddedTime = DateTime.UtcNow };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private PdfPageReaderScreenViewModel CreateViewModel(int bookId)
    {
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var vm = new PdfPageReaderScreenViewModel(() => { });
        vm.LoadBook(bookId);
        return vm;
    }

    [Fact]
    public void LoadBook_RealPdf_ReportsCorrectPageCountAndFirstPage()
    {
        int bookId = AddBook(pageCount: 3);

        var vm = CreateViewModel(bookId);

        Assert.False(vm.HasError);
        Assert.Equal(3, vm.PageCount);
        Assert.Equal(0, vm.PageIndex);
        Assert.NotNull(vm.CurrentPage);
    }

    [Fact]
    public void GoRight_AdvancesPage_ClampsAtLastPage()
    {
        int bookId = AddBook(pageCount: 2);
        var vm = CreateViewModel(bookId);

        vm.GoRightCommand.Execute(null);
        Assert.Equal(1, vm.PageIndex);

        vm.GoRightCommand.Execute(null); // already at last page - should clamp, not throw/overflow
        Assert.Equal(1, vm.PageIndex);
    }

    [Fact]
    public void GoLeft_AtFirstPage_StaysClamped()
    {
        int bookId = AddBook(pageCount: 2);
        var vm = CreateViewModel(bookId);

        vm.GoLeftCommand.Execute(null);

        Assert.Equal(0, vm.PageIndex);
    }

    [Fact]
    public void PageTurn_ResetsZoom()
    {
        int bookId = AddBook(pageCount: 2);
        var vm = CreateViewModel(bookId);
        vm.ZoomLevel = 3.0;

        vm.GoRightCommand.Execute(null);

        Assert.Equal(1.0, vm.ZoomLevel);
    }

    // --- PDF area capture (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-
    // annotations-design.md §"PDF area capture"). CaptureRegion writes a real file under the actual
    // %AppData%\Paperbunkr\annotations\ folder (not a test-isolated temp path, by design - that's the
    // real production location), so these tests explicitly delete whatever they create. ---

    [Fact]
    public void CaptureRegion_SavesAnImageFileAndAPersistedRow()
    {
        int bookId = AddBook(pageCount: 1);
        var vm = CreateViewModel(bookId);
        string? savedPath = null;

        try
        {
            vm.CaptureRegion(new Avalonia.Rect(0.1, 0.1, 0.3, 0.3));

            Assert.Single(vm.AnnotationImages);
            savedPath = vm.AnnotationImages[0].ImagePath;
            Assert.True(File.Exists(savedPath));

            using var context = new PaperbunkrDbContext(_dbOptions);
            var entity = context.BookAnnotationImages.Single();
            Assert.Equal(0, entity.PageIndex);
            Assert.Equal(0.1, entity.RectX, 3);
            Assert.Equal(0.3, entity.RectWidth, 3);
        }
        finally
        {
            if (savedPath is not null && File.Exists(savedPath))
            {
                File.Delete(savedPath);
            }
        }
    }

    [Fact]
    public void DeleteCapture_RemovesTheFileAndTheRow()
    {
        int bookId = AddBook(pageCount: 1);
        var vm = CreateViewModel(bookId);
        vm.CaptureRegion(new Avalonia.Rect(0.1, 0.1, 0.3, 0.3));
        string savedPath = vm.AnnotationImages[0].ImagePath;

        vm.DeleteCaptureCommand.Execute(vm.AnnotationImages[0]);

        Assert.Empty(vm.AnnotationImages);
        Assert.False(File.Exists(savedPath));
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.BookAnnotationImages);
    }

    [Fact]
    public void LoadBook_ReloadsPersistedCaptures()
    {
        int bookId = AddBook(pageCount: 1);
        var firstVm = CreateViewModel(bookId);
        firstVm.CaptureRegion(new Avalonia.Rect(0.1, 0.1, 0.3, 0.3));
        string savedPath = firstVm.AnnotationImages[0].ImagePath;

        try
        {
            var reopened = CreateViewModel(bookId);

            Assert.Single(reopened.AnnotationImages);
            Assert.Equal(savedPath, reopened.AnnotationImages[0].ImagePath);
        }
        finally
        {
            if (File.Exists(savedPath))
            {
                File.Delete(savedPath);
            }
        }
    }

    // --- PDF theme (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md) - reuses
    // Book.ThemeOverride/AppSettings.BookReaderTheme, the exact same columns BookReaderScreenViewModel's
    // own Settings.Theme resolution already uses (both already format-agnostic, no schema change). ---

    [Fact]
    public void Theme_NoOverride_FallsBackToAppSettingsDefault()
    {
        int bookId = AddBook(pageCount: 1);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().BookReaderTheme = BookTheme.Sepia;
            context.SaveChanges();
        }

        var vm = CreateViewModel(bookId);

        Assert.Equal(BookTheme.Sepia, vm.Theme);
    }

    [Fact]
    public void Theme_BookHasOverride_UsesOverrideNotAppSettingsDefault()
    {
        int bookId = AddBook(pageCount: 1);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().BookReaderTheme = BookTheme.Light;
            context.Books.First(b => b.Id == bookId).ThemeOverride = BookTheme.OledBlack;
            context.SaveChanges();
        }

        var vm = CreateViewModel(bookId);

        Assert.Equal(BookTheme.OledBlack, vm.Theme);
    }

    [Fact]
    public void CloseFontTheme_PersistsTheChosenThemeAsBookOverride()
    {
        int bookId = AddBook(pageCount: 1);
        var vm = CreateViewModel(bookId);

        vm.SetThemeCommand.Execute(BookTheme.Dark);
        vm.CloseFontThemeCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(BookTheme.Dark, context.Books.First(b => b.Id == bookId).ThemeOverride);
    }

    [Fact]
    public void SetTheme_UpdatesCanvasBackground()
    {
        int bookId = AddBook(pageCount: 1);
        var vm = CreateViewModel(bookId);

        vm.SetThemeCommand.Execute(BookTheme.Light);

        var expected = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(
            Paperbunkr.App.Models.BookThemeBrushes.ContentBackground(BookTheme.Light));
        var actual = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(vm.CanvasBackground);
        Assert.Equal(expected.Color, actual.Color);
    }
}
