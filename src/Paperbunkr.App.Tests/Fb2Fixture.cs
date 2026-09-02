using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Builds a minimal, hand-authored valid FB2 file (two top-level sections, one with a nested
/// sub-section, inline emphasis/strong, a base64 cover binary, optional calibre-style series via
/// &lt;sequence&gt;) for testing <c>Fb2BookSource</c> against a real file rather than a mock - same
/// "generate via the real code path" precedent as <see cref="EpubFixture"/>/<see cref="CbzFixture"/>.
/// </summary>
internal static class Fb2Fixture
{
    /// <summary>A trivial 1x1 GIF's bytes, reused only as "some real base64 payload" - the test suite never decodes it as an actual image.</summary>
    private static readonly byte[] s_coverBytes = { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00 };

    public static string Create(string path, string title = "Test Novel", string author = "Ada Author", string? seriesName = null, bool zipWrapped = false, bool malformed = false)
    {
        string xml = malformed
            ? "<FictionBook><this is not valid xml"
            : BuildXml(title, author, seriesName);

        if (!zipWrapped)
        {
            File.WriteAllText(path, xml, new UTF8Encoding(false));
            return path;
        }

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("book.fb2", CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(xml);
        return path;
    }

    private static string BuildXml(string title, string author, string? seriesName)
    {
        string[] authorParts = author.Split(' ', 2);
        string firstName = authorParts[0];
        string lastName = authorParts.Length > 1 ? authorParts[1] : string.Empty;

        string sequenceMeta = seriesName is null ? string.Empty : $"""
                <sequence name="{seriesName}" number="1"/>

            """;

        string coverBase64 = Convert.ToBase64String(s_coverBytes);

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <FictionBook xmlns="http://www.gribuser.ru/xml/fictionbook/2.0" xmlns:l="http://www.w3.org/1999/xlink">
              <description>
                <title-info>
                  <book-title>{title}</book-title>
                  <author>
                    <first-name>{firstName}</first-name>
                    <last-name>{lastName}</last-name>
                  </author>
            {sequenceMeta}      <coverpage>
                    <image l:href="#cover.jpg"/>
                  </coverpage>
                </title-info>
              </description>
              <body>
                <section>
                  <title><p>The Beginning</p></title>
                  <p>It was a <strong>dark</strong> and stormy night.</p>
                  <p>She walked <emphasis>slowly</emphasis> into the fog.</p>
                  <section>
                    <title><p>A Sub Heading</p></title>
                    <p>Nested content that should flatten into the parent chapter.</p>
                  </section>
                </section>
                <section>
                  <title><p>The End</p></title>
                  <p>And so it ended, quietly.</p>
                </section>
              </body>
              <binary id="cover.jpg" content-type="image/gif">{coverBase64}</binary>
            </FictionBook>
            """;
    }
}
