extern alias thumbs;

using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Paperbunkr.App.Services;
using thumbs::Paperbunkr.ShellThumbnails;
using thumbs::Paperbunkr.ShellThumbnails.Com;
using thumbs::Paperbunkr.ShellThumbnails.Interop;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The File Explorer thumbnail handler's cover finders, renderers and COM class
/// (docs/superpowers/specs/2026-09-30-explorer-cover-thumbnails-design.md), exercised as a plain managed assembly -
/// the Native AOT build is the same code.
/// </summary>
public class ShellThumbnailsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pb-shellthumbs-" + Guid.NewGuid().ToString("N"));

    public ShellThumbnailsTests()
    {
        Directory.CreateDirectory(_dir);
        CoInitializeEx(0, 0); // WIC needs COM on this thread; harmless if it's already initialised
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ---------- cover choice: same rule as the library (decision 5) ----------

    [Fact]
    public void PickCover_UsesNaturalSort_AndSkipsNonImagesAndMacJunk()
    {
        string? cover = CoverImageRules.PickCover(new[]
        {
            "ComicInfo.xml", "__MACOSX/page01.jpg", "page10.jpg", "page2.jpg", "Thumbs.db",
        });

        Assert.Equal("page2.jpg", cover);
    }

    [Fact]
    public void PickCover_NoImages_ReturnsNull() =>
        Assert.Null(CoverImageRules.PickCover(new[] { "ComicInfo.xml", "notes.txt" }));

    [Fact]
    public void ArchiveCover_MatchesTheLibrarysPageZero()
    {
        string path = Path.Combine(_dir, "parity.cbz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            // Deliberately out of order, with a folder and mixed case - the engine's natural sort decides.
            AddPng(zip, "Issue/p10.png", 30, 40);
            AddPng(zip, "Issue/P9.png", 31, 41);
            AddPng(zip, "Issue/p1b.png", 32, 42);
            AddPng(zip, "__MACOSX/Issue/._p0.png", 33, 43);
        }

        byte[]? ours;
        using (var stream = File.OpenRead(path))
        {
            ours = ArchiveCoverFinder.FindCover(stream);
        }

        using var provider = PageDecodeCore.TryOpenProvider(path);
        Assert.NotNull(provider);
        Assert.Equal(provider!.GetByteImage(0), ours);
    }

    [Fact]
    public void ArchiveCover_ArchiveWithoutImages_IsNull()
    {
        string path = Path.Combine(_dir, "text.cbz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("ComicInfo.xml").Open());
            writer.Write("<ComicInfo/>");
        }

        using var stream = File.OpenRead(path);
        Assert.Null(ArchiveCoverFinder.FindCover(stream));
    }

    // ---------- EPUB (decision 10) ----------

    [Fact]
    public void Epub_DeclaredCoverImage_IsUsed()
    {
        byte[] cover = Png(20, 30);
        string path = WriteEpub(
            manifest: """<item id="c" href="images/cover.png" media-type="image/png" properties="cover-image"/><item id="t" href="text.xhtml" media-type="application/xhtml+xml"/>""",
            spine: """<itemref idref="t"/>""",
            files: new() { ["OEBPS/images/cover.png"] = cover, ["OEBPS/text.xhtml"] = Html("<p>hello</p>") });

        using var stream = File.OpenRead(path);
        Assert.Equal(cover, EpubCoverFinder.FindCover(stream));
    }

    [Fact]
    public void Epub_Epub2MetaCover_IsUsed()
    {
        byte[] cover = Png(21, 31);
        string path = WriteEpub(
            manifest: """<item id="cov" href="cover.png" media-type="image/png"/><item id="t" href="text.xhtml" media-type="application/xhtml+xml"/>""",
            spine: """<itemref idref="t"/>""",
            files: new() { ["OEBPS/cover.png"] = cover, ["OEBPS/text.xhtml"] = Html("<p>hello</p>") },
            metadata: """<meta name="cover" content="cov"/>""");

        using var stream = File.OpenRead(path);
        Assert.Equal(cover, EpubCoverFinder.FindCover(stream));
    }

    [Fact]
    public void Epub_NoDeclaredCover_UsesFirstImageInReadingOrder()
    {
        byte[] first = Png(22, 32), later = Png(23, 33);
        string path = WriteEpub(
            manifest: """<item id="a" href="text/a.xhtml" media-type="application/xhtml+xml"/><item id="b" href="text/b.xhtml" media-type="application/xhtml+xml"/>""",
            spine: """<itemref idref="a"/><itemref idref="b"/>""",
            files: new()
            {
                ["OEBPS/text/a.xhtml"] = Html("""<p>no picture here</p>"""),
                ["OEBPS/text/b.xhtml"] = Html("""<img src="../img/first.png"/><img src="../img/later.png"/>"""),
                ["OEBPS/img/first.png"] = first,
                ["OEBPS/img/later.png"] = later,
            });

        using var stream = File.OpenRead(path);
        Assert.Equal(first, EpubCoverFinder.FindCover(stream));
    }

    [Fact]
    public void Epub_TextOnly_IsNull()
    {
        string path = WriteEpub(
            manifest: """<item id="t" href="text.xhtml" media-type="application/xhtml+xml"/>""",
            spine: """<itemref idref="t"/>""",
            files: new() { ["OEBPS/text.xhtml"] = Html("<p>words only</p>") });

        using var stream = File.OpenRead(path);
        Assert.Null(EpubCoverFinder.FindCover(stream));
    }

    // ---------- MOBI (decisions 10-11, 15) ----------

    [Fact]
    public void Mobi_UsesTheExthCoverRecord()
    {
        string path = MobiFixture.Create(Path.Combine(_dir, "book.mobi"));

        using var stream = File.OpenRead(path);
        byte[]? cover = MobiCoverFinder.FindCover(stream);

        Assert.NotNull(cover);
        Assert.True(MobiCoverFinder.IsImage(cover!));
    }

    [Fact]
    public void Mobi_Drm_IsNull()
    {
        string path = MobiFixture.CreateDrmProtected(Path.Combine(_dir, "drm.azw"));

        using var stream = File.OpenRead(path);
        Assert.Null(MobiCoverFinder.FindCover(stream));
    }

    // ---------- rendering ----------

    [Fact]
    public void Wic_ScalesToFitTheRequestedSquare()
    {
        var image = Wic.DecodeScaled(Png(1000, 500), 256);

        Assert.Equal(256, image.Width);
        Assert.Equal(128, image.Height);
        Assert.Equal(256 * 128 * 4, image.Pixels.Length);
    }

    [Fact]
    public void Wic_NeverEnlargesASmallCover()
    {
        var image = Wic.DecodeScaled(Png(40, 60), 256);

        Assert.Equal((40, 60), (image.Width, image.Height));
    }

    [Fact]
    public void Pdf_RendersPageOneWithinTheSquare()
    {
        string path = PdfFixture.Create(Path.Combine(_dir, "doc.pdf"), "first page", "second page");

        using var stream = File.OpenRead(path);
        var image = Pdfium.RenderFirstPage(stream, 200);

        Assert.NotNull(image);
        Assert.True(image!.Width <= 200 && image.Height <= 200);
        Assert.True(image.Width == 200 || image.Height == 200);
    }

    [Fact]
    public void Render_DamagedFile_GivesNoThumbnail()
    {
        string path = Path.Combine(_dir, "broken.cbz");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        using var stream = File.OpenRead(path);
        Assert.Null(ThumbnailRenderer.Render(stream, ".cbz", 256, new Deadline(Deadline.DefaultBudget)));
    }

    [Fact]
    public void Render_OutOfTime_GivesNoThumbnail()
    {
        string path = CbzFixture.Create(Path.Combine(_dir, "late.cbz"), 2);

        using var stream = File.OpenRead(path);
        Assert.Null(ThumbnailRenderer.Render(stream, ".cbz", 256, new Deadline(TimeSpan.Zero)));
    }

    [Fact]
    public void Sniffer_TellsFormatsApartWithoutAName()
    {
        using var pdf = File.OpenRead(PdfFixture.Create(Path.Combine(_dir, "x.pdf"), "p"));
        using var cbz = File.OpenRead(CbzFixture.Create(Path.Combine(_dir, "x.cbz"), 1));
        using var epub = File.OpenRead(WriteEpub("""<item id="t" href="t.xhtml" media-type="application/xhtml+xml"/>""", """<itemref idref="t"/>""", new() { ["OEBPS/t.xhtml"] = Html("") }));
        using var mobi = File.OpenRead(MobiFixture.Create(Path.Combine(_dir, "x.mobi")));

        Assert.Equal(".pdf", FormatSniffer.Sniff(pdf));
        Assert.Equal(".cbz", FormatSniffer.Sniff(cbz));
        Assert.Equal(".epub", FormatSniffer.Sniff(epub));
        Assert.Equal(".mobi", FormatSniffer.Sniff(mobi));
    }

    // ---------- COM surface ----------

    /// <summary>Windows asks for these exact IIDs. A wrong one fails silently in the real surrogate while managed
    /// calls still work - it happened once: IInitializeWithStream had IInitializeWithFile's IID (2026-09-30).</summary>
    [Fact]
    public void ComInterfaces_UseWindowsOwnIids()
    {
        Assert.Equal(new Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), typeof(IInitializeWithStream).GUID);
        Assert.Equal(new Guid("e357fccd-a995-4576-b01f-234630154e96"), typeof(IThumbnailProvider).GUID);
        Assert.Equal(new Guid("00000001-0000-0000-C000-000000000046"), typeof(IClassFactory).GUID);
    }

    [Fact]
    public void AppAndHandler_AgreeOnTheClsid() =>
        Assert.Equal("{" + ThumbnailProvider.ClsidString + "}", ThumbnailHandlerService.HandlerClsid, ignoreCase: true);

    [Fact]
    public void ThumbnailProvider_EndToEnd_ReturnsAnArgbBitmap()
    {
        byte[] bytes = File.ReadAllBytes(CbzFixture.Create(Path.Combine(_dir, "e2e.cbz"), 3, pageSize: _ => new Size(600, 900)));
        nint stream = SHCreateMemStream(bytes, (uint)bytes.Length);
        Assert.NotEqual(0, stream);

        var provider = new ThumbnailProvider();
        try
        {
            Assert.Equal(0, provider.Initialize(stream, 0));
            Assert.Equal(0, provider.GetThumbnail(256, out nint bitmap, out int alpha));

            Assert.NotEqual(0, bitmap);
            Assert.Equal(2, alpha); // WTSAT_ARGB
            DeleteObject(bitmap);
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    [Fact]
    public void ThumbnailProvider_FileWithNoCover_Fails()
    {
        byte[] bytes = File.ReadAllBytes(WriteEpub("""<item id="t" href="t.xhtml" media-type="application/xhtml+xml"/>""", """<itemref idref="t"/>""", new() { ["OEBPS/t.xhtml"] = Html("<p>text</p>") }));
        nint stream = SHCreateMemStream(bytes, (uint)bytes.Length);

        var provider = new ThumbnailProvider();
        try
        {
            provider.Initialize(stream, 0);
            Assert.NotEqual(0, provider.GetThumbnail(256, out nint bitmap, out _));
            Assert.Equal(0, bitmap);
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    [Fact]
    public void ClassFactory_HandsOutTheThumbnailProvider()
    {
        var iid = new Guid("e357fccd-a995-4576-b01f-234630154e96");
        int hr = new ThumbnailProviderFactory().CreateInstance(0, iid, out nint instance);

        Assert.Equal(0, hr);
        Assert.NotEqual(0, instance);
        Marshal.Release(instance);
    }

    // ---------- helpers ----------

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(255, (width * 7) % 255, (height * 13) % 255, 90));
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static void AddPng(ZipArchive zip, string name, int width, int height)
    {
        using var s = zip.CreateEntry(name).Open();
        s.Write(Png(width, height));
    }

    private static byte[] Html(string body) =>
        System.Text.Encoding.UTF8.GetBytes($"""<?xml version="1.0"?><html xmlns="http://www.w3.org/1999/xhtml"><body>{body}</body></html>""");

    private string WriteEpub(string manifest, string spine, Dictionary<string, byte[]> files, string metadata = "")
    {
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".epub");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, "mimetype", System.Text.Encoding.ASCII.GetBytes("application/epub+zip"));
        Write(zip, "META-INF/container.xml", System.Text.Encoding.UTF8.GetBytes(
            """<?xml version="1.0"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>"""));
        Write(zip, "OEBPS/content.opf", System.Text.Encoding.UTF8.GetBytes(
            $"""<?xml version="1.0"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0"><metadata>{metadata}</metadata><manifest>{manifest}</manifest><spine>{spine}</spine></package>"""));
        foreach (var (name, data) in files)
        {
            Write(zip, name, data);
        }

        return path;
    }

    private static void Write(ZipArchive zip, string name, byte[] data)
    {
        using var s = zip.CreateEntry(name).Open();
        s.Write(data);
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("shlwapi.dll")]
    private static extern nint SHCreateMemStream(byte[] data, uint size);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);
}
