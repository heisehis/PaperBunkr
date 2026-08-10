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
    public static string Create(string path, string title = "Test Novel", string author = "Ada Author", string? seriesName = null, bool firstChapterEmpty = false)
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
              </manifest>
              <spine>
                <itemref idref="chap1"/>
                <itemref idref="chap2"/>
              </spine>
            </package>
            """);

        WriteEntry(zip, "OEBPS/nav.xhtml", """
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
        WriteEntry(zip, "OEBPS/chap1.xhtml", firstChapterEmpty
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
