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
}
