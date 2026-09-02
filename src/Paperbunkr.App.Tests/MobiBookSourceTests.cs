using System;
using System.IO;
using System.Linq;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Verifies <c>MobiBookSource</c>'s foundation layer (docs/superpowers/specs/2026-09-01-books-
/// format-ingestion-fb2-mobi-design.md) against a real hand-built PalmDB+MOBI6 file from
/// <see cref="MobiFixture"/> - metadata extraction, multi-record PalmDOC decompression reassembly,
/// chapter splitting on heading tags, cover extraction, and the DRM/Huffman negative-path refusals.
/// </summary>
public class MobiBookSourceTests : IDisposable
{
    private readonly string _mobiPath;
    private readonly string _drmPath;
    private readonly string _huffmanPath;

    public MobiBookSourceTests()
    {
        _mobiPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mobi_test_{Guid.NewGuid():N}.mobi");
        _drmPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mobi_drm_test_{Guid.NewGuid():N}.mobi");
        _huffmanPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mobi_huffman_test_{Guid.NewGuid():N}.mobi");
    }

    public void Dispose()
    {
        foreach (string path in new[] { _mobiPath, _drmPath, _huffmanPath })
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Metadata_ExtractsTitleAuthorAndCover_FromExth()
    {
        MobiFixture.Create(_mobiPath, title: "The Long Way Home", author: "Ada Author");

        using var source = new MobiBookSource(_mobiPath);

        Assert.Equal("The Long Way Home", source.Metadata.Title);
        Assert.Equal("Ada Author", source.Metadata.Author);
        Assert.NotNull(source.Metadata.CoverImageBytes);
        Assert.True(source.Metadata.CoverImageBytes!.Length > 0);
    }

    [Fact]
    public void MultiRecordText_DecompressesAndReassemblesAcrossRecordBoundary()
    {
        MobiFixture.Create(_mobiPath, compressed: true);

        using var source = new MobiBookSource(_mobiPath);

        string allText = string.Join(" ", source.Chapters.SelectMany(c => c.Paragraphs).Select(p => p.Text));
        Assert.Contains("It was a dark and stormy night.", allText);
        // Only present if the second PalmDOC record's content decompressed and reassembled correctly.
        Assert.Contains("And so it ended, quietly.", allText);
    }

    [Fact]
    public void Chapters_SplitOnHeadingTags_WithTitlesFromHeadingText()
    {
        MobiFixture.Create(_mobiPath);

        using var source = new MobiBookSource(_mobiPath);

        Assert.Equal(2, source.Chapters.Count);
        Assert.Equal("The Beginning", source.Chapters[0].Title);
        Assert.Equal("The End", source.Chapters[1].Title);
        // HtmlProseExtractor (shared with EpubBookSource) treats <h1> as a paragraph-break tag, not
        // something to strip - the heading's own text becomes the chapter's first paragraph too,
        // same pre-existing behavior an EPUB chapter with a leading <h1> already has.
        Assert.Contains(source.Chapters[1].Paragraphs, p => p.Text == "And so it ended, quietly.");
    }

    [Fact]
    public void UncompressedText_ReadsCorrectly_WhenCompressionTypeIsNone()
    {
        MobiFixture.Create(_mobiPath, compressed: false, htmlBody: "<html><body><h1>Only Chapter</h1><p>Plain uncompressed text.</p></body></html>");

        using var source = new MobiBookSource(_mobiPath);

        var chapter = Assert.Single(source.Chapters);
        Assert.Equal("Only Chapter", chapter.Title);
        Assert.Contains(chapter.Paragraphs, p => p.Text == "Plain uncompressed text.");
    }

    [Fact]
    public void DrmProtectedFile_RefusesCleanly_NotACrashOrGarbageText()
    {
        MobiFixture.CreateDrmProtected(_drmPath);

        var ex = Assert.Throws<NotSupportedException>(() => new MobiBookSource(_drmPath));
        Assert.Contains("DRM", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HuffmanCompressedFile_RefusesCleanly_NotACrashOrGarbageText()
    {
        MobiFixture.CreateHuffmanCompressed(_huffmanPath);

        var ex = Assert.Throws<NotSupportedException>(() => new MobiBookSource(_huffmanPath));
        Assert.Contains("compression", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

}
