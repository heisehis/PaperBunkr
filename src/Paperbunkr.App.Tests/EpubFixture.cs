using System.IO;
using System.IO.Compression;
using System.Text;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Builds a minimal, hand-authored valid EPUB3 file (two chapters, real nav/TOC, optional
/// calibre:series metadata) for testing <c>EpubBookSource</c> against a real archive rather than a
/// mock - same "generate via the real code path" precedent as <see cref="CbzFixture"/>.
/// </summary>
internal static class EpubFixture
{
    public static string Create(string path, string title = "Test Novel", string author = "Ada Author", string? seriesName = null, bool firstChapterEmpty = false, bool firstChapterCoverImageOnly = false, bool withParts = false)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        // mimetype must be the first entry, stored (uncompressed), per the EPUB spec.
        var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = mimetype.Open())
        using (var w = new StreamWriter(s, new UTF8Encoding(false)))
        {
            w.Write("application/epub+zip");
        }

        WriteEntry(zip, "META-INF/container.xml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        string seriesMeta = seriesName is null ? string.Empty : $"""
                <meta name="calibre:series" content="{seriesName}"/>

            """;

        string coverManifestItem = firstChapterCoverImageOnly
            ? "    <item id=\"cover\" href=\"cover.jpg\" media-type=\"image/jpeg\"/>\n"
            : string.Empty;

        // withParts: a 3rd chapter + a nested nav <ol> (chap1 standalone, chap2/chap3 grouped under
        // "Part One") - real EPUB3 nav structure, exercising EpubBookSource.FlattenNavigationParts
        // (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md, TOC grouping)
        // against an actual archive rather than a synthetic EpubNavigationItem tree.
        string chap3ManifestItem = withParts ? "    <item id=\"chap3\" href=\"chap3.xhtml\" media-type=\"application/xhtml+xml\"/>\n" : string.Empty;
        string chap3SpineItem = withParts ? "    <itemref idref=\"chap3\"/>\n" : string.Empty;

        WriteEntry(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">urn:uuid:00000000-0000-0000-0000-000000000001</dc:identifier>
                <dc:title>{title}</dc:title>
                <dc:creator>{author}</dc:creator>
                <dc:language>en</dc:language>
            {seriesMeta}  </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                <item id="chap1" href="chap1.xhtml" media-type="application/xhtml+xml"/>
                <item id="chap2" href="chap2.xhtml" media-type="application/xhtml+xml"/>
            {chap3ManifestItem}{coverManifestItem}  </manifest>
              <spine>
                <itemref idref="chap1"/>
                <itemref idref="chap2"/>
            {chap3SpineItem}  </spine>
            </package>
            """);

        WriteEntry(zip, "OEBPS/nav.xhtml", withParts ? """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Table of Contents</title></head>
            <body>
              <nav epub:type="toc">
                <ol>
                  <li><a href="chap1.xhtml">The Beginning</a></li>
                  <li><span>Part One</span>
                    <ol>
                      <li><a href="chap2.xhtml">The End</a></li>
                      <li><a href="chap3.xhtml">The Real End</a></li>
                    </ol>
                  </li>
                </ol>
              </nav>
            </body>
            </html>
            """ : """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Table of Contents</title></head>
            <body>
              <nav epub:type="toc">
                <ol>
                  <li><a href="chap1.xhtml">The Beginning</a></li>
                  <li><a href="chap2.xhtml">The End</a></li>
                </ol>
              </nav>
            </body>
            </html>
            """);

        // Real EPUBs commonly lead with a cover/title-page spine item with no <p>/<h1>/etc at all
        // (image-only) - firstChapterEmpty simulates that for testing the reader's chapter-skip
        // behavior, since HtmlProseExtractor correctly yields zero paragraphs for markup like this.
        WriteEntry(zip, "OEBPS/chap1.xhtml", firstChapterEmpty || firstChapterCoverImageOnly
            ? """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head><title>Cover</title></head>
            <body>
              <img src="cover.jpg" alt="Cover" />
            </body>
            </html>
            """
            : """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head><title>The Beginning</title></head>
            <body>
              <h1>The Beginning</h1>
              <p>It was a <b>dark</b> and stormy night.</p>
              <p>She walked <i>slowly</i> into the fog.</p>
            </body>
            </html>
            """);

        WriteEntry(zip, "OEBPS/chap2.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head><title>The End</title></head>
            <body>
              <h1>The End</h1>
              <p>And so it ended, quietly.</p>
            </body>
            </html>
            """);

        if (withParts)
        {
            WriteEntry(zip, "OEBPS/chap3.xhtml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml">
                <head><title>The Real End</title></head>
                <body>
                  <h1>The Real End</h1>
                  <p>No, really, it ended here.</p>
                </body>
                </html>
                """);
        }

        // Unlike firstChapterEmpty's cover.jpg (a deliberately *unresolved* reference - no matching
        // manifest item or binary entry - EpubBookSource.InlineImages correctly leaves an unresolved
        // src untouched, never inventing a broken data URI), this one is real: a manifest item plus an
        // actual embedded binary, so the resulting chapter Html genuinely contains a "data:image" URI.
        // Tests BookReaderScreenViewModel's chapter-skip logic distinguishing "no text and nothing to
        // show" (still skipped) from "no text but a real image" (must NOT be skipped) - a real
        // regression: a cover page whose only content was its now-working image still got skipped
        // because the skip check only ever looked at Paragraphs.
        if (firstChapterCoverImageOnly)
        {
            byte[] jpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
            var coverEntry = zip.CreateEntry("OEBPS/cover.jpg", CompressionLevel.Fastest);
            using var coverStream = coverEntry.Open();
            coverStream.Write(jpegBytes, 0, jpegBytes.Length);
        }

        return path;
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
