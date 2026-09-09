using cYo.Projects.ComicRack.Engine.IO.Provider.Readers;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests.Reader;

/// <summary>
/// Step 3 of the reader-pipeline plan: <see cref="PdfComicProvider.TryOpenReaderSession"/> holding
/// one <c>PdfDocument</c> open for the session (§4.3), and the pipeline routing PDF page reads
/// through it instead of a per-page <c>new PdfDocument</c> reopen.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PdfPipelineSessionTests : IDisposable
{
    private readonly string _pdfPath = Path.Combine(Path.GetTempPath(), $"pb_pdf_session_{Guid.NewGuid():N}.pdf");

    public void Dispose()
    {
        try { if (File.Exists(_pdfPath)) File.Delete(_pdfPath); } catch (IOException) { }
    }

    [Fact]
    public void PdfProvider_OpensSession_AndReadsEveryPage()
    {
        PdfFixture.Create(_pdfPath, "page one", "page two", "page three");
        var provider = PageDecodeCore.TryOpenProvider(_pdfPath);
        Assert.NotNull(provider);
        var pdf = Assert.IsType<PdfComicProvider>(provider);

        using var session = pdf.TryOpenReaderSession();
        Assert.NotNull(session);
        Assert.Equal(3, session!.Count);

        for (int i = 0; i < 3; i++)
        {
            var bytes = session.ReadEntryBytes(i.ToString());
            Assert.NotNull(bytes);
            Assert.True(bytes!.Length > 0);
            // JPEG magic - the PDFium render path re-encodes to JPEG.
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xD8, bytes[1]);
        }

        Assert.Null(session.ReadEntryBytes("99"));
        Assert.Null(session.ReadEntryBytes("notanumber"));
    }

    [Fact]
    public void Pipeline_OpensPdfViaSession_AndDecodes()
    {
        PdfFixture.Create(_pdfPath, "alpha", "bravo");
        using var pipeline = ReaderImagePipeline.TryOpen(_pdfPath);
        Assert.NotNull(pipeline);
        Assert.Equal(2, pipeline!.PageCount);

        var page = pipeline.GetPage(0);
        Assert.NotNull(page);
        Assert.True(page.PixelSize.Width > 0 && page.PixelSize.Height > 0);
    }
}
