using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Verifies <c>EpubBookSource</c> resolves real EPUB in-body <c>&lt;img&gt;</c> references to
/// <c>data:</c> URIs (docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-redesign-design.md)
/// - a real EPUB's image paths are relative to the *containing chapter document's own location
/// within the zip*, which means nothing to <c>NativeWebView.NavigateToString</c> (no base URL, no
/// archive filesystem) unless rewritten. Builds its own minimal EPUB directly (not via
/// <see cref="EpubFixture"/>, which has no image manifest entries) with a real nested
/// Text/Images directory split, since that's exactly the structure the relative-path resolution
/// (`../Images/...`) needs to be exercised against.
/// </summary>
public class EpubBookSourceImageInliningTests : IDisposable
{
    private readonly string _epubPath;

    public EpubBookSourceImageInliningTests()
    {
        _epubPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_epub_image_test_{Guid.NewGuid():N}.epub");
    }

    public void Dispose()
    {
        if (File.Exists(_epubPath)) File.Delete(_epubPath);
    }

    [Fact]
    public void Html_InlinesRelativeImageReference_AsDataUri_WithRealContentType()
    {
        byte[] pngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        string expectedBase64 = Convert.ToBase64String(pngBytes);
        BuildEpub(pngBytes);

        using var source = new EpubBookSource(_epubPath);

        string html = source.Chapters[0].Html ?? string.Empty;
        Assert.Contains($"src=\"data:image/png;base64,{expectedBase64}\"", html);
        Assert.DoesNotContain("../Images/illustration.png", html);
    }

    /// <summary>
    /// Real Calibre-converted EPUBs (confirmed 2026-09-02 against the user's own library - Dune,
    /// Ender's Game, Red Queen all do this for their cover, which is the very first page a reader
    /// opens) wrap the cover image in an SVG element to lock its aspect ratio, instead of a plain
    /// &lt;img&gt;: &lt;svg&gt;&lt;image xlink:href="cover.jpeg"/&gt;&lt;/svg&gt;. This was a real,
    /// silent gap in the original &lt;img src&gt;-only inlining logic.
    /// </summary>
    [Fact]
    public void Html_InlinesSvgImageXlinkHrefCover_AsDataUri()
    {
        byte[] jpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        string expectedBase64 = Convert.ToBase64String(jpegBytes);
        BuildSvgCoverEpub(jpegBytes);

        using var source = new EpubBookSource(_epubPath);

        string html = source.Chapters[0].Html ?? string.Empty;
        Assert.Contains($"xlink:href=\"data:image/jpeg;base64,{expectedBase64}\"", html);
        Assert.DoesNotContain("cover.jpeg\"", html);
    }

    private void BuildSvgCoverEpub(byte[] imageBytes)
    {
        using var zip = ZipFile.Open(_epubPath, ZipArchiveMode.Create);

        var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = mimetype.Open())
        using (var w = new StreamWriter(s, new UTF8Encoding(false)))
        {
            w.Write("application/epub+zip");
        }

        WriteEntry(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        WriteEntry(zip, "OEBPS/content.opf", """
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">urn:uuid:00000000-0000-0000-0000-000000000003</dc:identifier>
                <dc:title>Svg Cover Novel</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                <item id="titlepage" href="titlepage.xhtml" media-type="application/xhtml+xml"/>
                <item id="cover" href="cover.jpeg" media-type="image/jpeg"/>
              </manifest>
              <spine>
                <itemref idref="titlepage"/>
              </spine>
            </package>
            """);

        WriteEntry(zip, "OEBPS/nav.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Table of Contents</title></head>
            <body>
              <nav epub:type="toc">
                <ol><li><a href="titlepage.xhtml">Cover</a></li></ol>
              </nav>
            </body>
            </html>
            """);

        WriteEntry(zip, "OEBPS/titlepage.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head><title>Cover</title></head>
            <body>
              <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="100%" height="100%" viewBox="0 0 600 800">
                <image width="600" height="800" xlink:href="cover.jpeg"/>
              </svg>
            </body>
            </html>
            """);

        var imageEntry = zip.CreateEntry("OEBPS/cover.jpeg", CompressionLevel.Fastest);
        using (var stream = imageEntry.Open())
        {
            stream.Write(imageBytes, 0, imageBytes.Length);
        }
    }

    private void BuildEpub(byte[] imageBytes)
    {
        using var zip = ZipFile.Open(_epubPath, ZipArchiveMode.Create);

        var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = mimetype.Open())
        using (var w = new StreamWriter(s, new UTF8Encoding(false)))
        {
            w.Write("application/epub+zip");
        }

        WriteEntry(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        // Real-world directory split (Text/ and Images/ as siblings under OEBPS/) - the chapter
        // document references the image via "../Images/..." specifically to exercise the ".."
        // relative-path resolution, not just a same-directory reference.
        WriteEntry(zip, "OEBPS/content.opf", """
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">urn:uuid:00000000-0000-0000-0000-000000000002</dc:identifier>
                <dc:title>Illustrated Novel</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="nav" href="Text/nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                <item id="chap1" href="Text/chap1.xhtml" media-type="application/xhtml+xml"/>
                <item id="illustration" href="Images/illustration.png" media-type="image/png"/>
              </manifest>
              <spine>
                <itemref idref="chap1"/>
              </spine>
            </package>
            """);

        WriteEntry(zip, "OEBPS/Text/nav.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Table of Contents</title></head>
            <body>
              <nav epub:type="toc">
                <ol><li><a href="chap1.xhtml">Chapter One</a></li></ol>
              </nav>
            </body>
            </html>
            """);

        WriteEntry(zip, "OEBPS/Text/chap1.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head><title>Chapter One</title></head>
            <body>
              <h1>Chapter One</h1>
              <p>Some text before the picture.</p>
              <img src="../Images/illustration.png" alt="An illustration" />
              <p>Some text after the picture.</p>
            </body>
            </html>
            """);

        var imageEntry = zip.CreateEntry("OEBPS/Images/illustration.png", CompressionLevel.Fastest);
        using (var stream = imageEntry.Open())
        {
            stream.Write(imageBytes, 0, imageBytes.Length);
        }
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
