using System.IO.Compression;
using System.Xml.Linq;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>EPUB OPF write-back (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §7).</summary>
public class BookFileWriteBackTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public BookFileWriteBackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_writeback_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "db.sqlite");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static OpfBookData Data(string? publisher = "Ace Books", string? series = "Dune Chronicles", float? index = 2f, float? rating = 3.5f) =>
        new(publisher, "en", "978-0-441-17271-9", new[] { "Science Fiction", "Classics" }, series, index, rating, "A desert planet.");

    private static XDocument Parse(string xml) => XDocument.Parse(xml, LoadOptions.PreserveWhitespace);

    private const string Opf = """
        <?xml version="1.0" encoding="UTF-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="bookid">urn:uuid:1</dc:identifier>
            <dc:title>T</dc:title>
            <dc:publisher>Old Publisher</dc:publisher>
            <dc:subject>Old Subject</dc:subject>
            <meta name="calibre:series" content="Old Series"/>
            <meta property="belongs-to-collection" id="c1">Old Series</meta>
            <meta refines="#c1" property="group-position">1</meta>
          </metadata>
          <manifest><item id="a" href="a.xhtml" media-type="application/xhtml+xml"/></manifest>
          <spine><itemref idref="a"/></spine>
        </package>
        """;

    [Fact]
    public void Patcher_UpdatesExistingNodes_AddsMissingOnes_AndLeavesTheRestAlone()
    {
        var doc = Parse(Opf);

        bool changed = OpfMetadataPatcher.Patch(doc, Data());

        Assert.True(changed);
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace opf = "http://www.idpf.org/2007/opf";
        var meta = doc.Root!.Element(opf + "metadata")!;
        Assert.Equal("Ace Books", meta.Elements(dc + "publisher").Single().Value);
        Assert.Equal("en", meta.Element(dc + "language")!.Value);
        Assert.Equal("A desert planet.", meta.Element(dc + "description")!.Value);
        Assert.Contains(meta.Elements(dc + "identifier"), i => i.Value == "urn:isbn:9780441172719");
        Assert.Contains(meta.Elements(dc + "identifier"), i => i.Value == "urn:uuid:1");
        Assert.Equal(new[] { "Science Fiction", "Classics" }, meta.Elements(dc + "subject").Select(s => s.Value));
        Assert.Equal("Dune Chronicles", meta.Elements(opf + "meta").Single(m => (string?)m.Attribute("name") == "calibre:series").Attribute("content")!.Value);
        Assert.Equal("2.0", meta.Elements(opf + "meta").Single(m => (string?)m.Attribute("name") == "calibre:series_index").Attribute("content")!.Value);
        Assert.Equal("7", meta.Elements(opf + "meta").Single(m => (string?)m.Attribute("name") == "calibre:rating").Attribute("content")!.Value);
        Assert.Equal("Dune Chronicles", meta.Elements(opf + "meta").Single(m => (string?)m.Attribute("property") == "belongs-to-collection").Value);
        Assert.Equal("2", meta.Elements(opf + "meta").Single(m => (string?)m.Attribute("property") == "group-position").Value);
        // Untouched nodes survive byte for byte.
        Assert.Equal("T", meta.Element(dc + "title")!.Value);
        Assert.NotNull(doc.Root.Element(opf + "manifest"));
        Assert.NotNull(doc.Root.Element(opf + "spine"));
        Assert.Equal("urn:uuid:1", (string?)null ?? meta.Elements(dc + "identifier").First().Value);
    }

    [Fact]
    public void Patcher_IsIdempotent_AndNeverDeletesOnEmptyValues()
    {
        var doc = Parse(Opf);
        OpfMetadataPatcher.Patch(doc, Data());

        Assert.False(OpfMetadataPatcher.Patch(doc, Data()));

        // Blank values never remove what the file already holds.
        var again = Parse(Opf);
        bool changed = OpfMetadataPatcher.Patch(again, new OpfBookData(null, null, null, Array.Empty<string>(), null, null, null, null));
        Assert.False(changed);
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.Equal("Old Publisher", again.Descendants(dc + "publisher").Single().Value);
    }

    [Fact]
    public void Patcher_ADocumentWithoutMetadata_IsLeftAlone()
    {
        var doc = Parse("""<package xmlns="http://www.idpf.org/2007/opf"><manifest/></package>""");

        Assert.False(OpfMetadataPatcher.Patch(doc, Data()));
    }

    private string MakeEpub(string name, bool seriesInOpf = true)
    {
        string path = Path.Combine(_root, name);
        EpubFixture.Create(path, title: "Dune", seriesName: seriesInOpf ? "Old Series" : null);
        return path;
    }

    private static string ReadOpf(string epubPath)
    {
        using var zip = ZipFile.OpenRead(epubPath);
        using var reader = new StreamReader(zip.GetEntry("OEBPS/content.opf")!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void PatchEpub_WritesTheValues_KeepsTheBookReadable_AndLeavesNoScratchFile()
    {
        string path = MakeEpub("a.epub");

        var outcome = BookFileWriteBackService.PatchEpub(path, Data());

        Assert.Equal(BookWriteBackResult.Success, outcome.Result);
        using (var source = new EpubBookSource(path))
        {
            Assert.Equal("Ace Books", source.Metadata.Publisher);
            Assert.Equal("Dune Chronicles", source.Metadata.SeriesName);
            Assert.Equal(2f, source.Metadata.SeriesIndex);
            Assert.Equal(3.5f, source.Metadata.Rating);
            Assert.Equal("9780441172719", source.Metadata.ISBN);
            Assert.Equal(2, source.Chapters.Count);
        }

        Assert.DoesNotContain(Directory.GetFiles(_root), f => f.Contains(".pbwrite-"));
        Assert.Contains("Ace Books", ReadOpf(path));
    }

    [Fact]
    public void PatchEpub_AgainWithTheSameData_IsAlreadyCurrent()
    {
        string path = MakeEpub("b.epub");
        BookFileWriteBackService.PatchEpub(path, Data());
        var before = File.ReadAllBytes(path);

        var outcome = BookFileWriteBackService.PatchEpub(path, Data());

        Assert.Equal(BookWriteBackResult.SkippedAlreadyCurrent, outcome.Result);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void PatchEpub_ACorruptFile_FailsAndLeavesTheOriginalUntouched()
    {
        string path = Path.Combine(_root, "bad.epub");
        File.WriteAllText(path, "not a zip");

        var outcome = BookFileWriteBackService.PatchEpub(path, Data());

        Assert.Equal(BookWriteBackResult.Failed, outcome.Result);
        Assert.Equal("not a zip", File.ReadAllText(path));
        Assert.DoesNotContain(Directory.GetFiles(_root), f => f.Contains(".pbwrite-"));
    }

    private int AddBook(string path, BookFormat format = BookFormat.Epub, Action<Book>? configure = null)
    {
        var book = new Book { Title = "Dune", Format = format, FilePath = path, AddedTime = DateTime.UtcNow, Publisher = "Ace Books", Rating = 3.5f };
        book.Tags.Add(new BookTag { Field = BookTagField.Genre, Value = "Science Fiction" });
        configure?.Invoke(book);
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    [Fact]
    public async Task WriteAsync_UsesTheBookRow_AndSkipsOtherFormatsAndMissingFiles()
    {
        var service = new BookFileWriteBackService(() => new PaperbunkrDbContext(_dbOptions));
        string epub = MakeEpub("c.epub", seriesInOpf: false);

        int ok = AddBook(epub, configure: b => b.BookSeries = new BookSeries { Name = "Dune Chronicles" });
        int pdf = AddBook(Path.Combine(_root, "x.pdf"), BookFormat.Pdf);
        File.WriteAllText(Path.Combine(_root, "x.pdf"), "%PDF");
        int gone = AddBook(Path.Combine(_root, "gone.epub"));

        Assert.Equal(BookWriteBackResult.Success, (await service.WriteAsync(ok)).Result);
        Assert.Equal(BookWriteBackResult.SkippedUnsupportedFormat, (await service.WriteAsync(pdf)).Result);
        Assert.Equal(BookWriteBackResult.SkippedMissingFile, (await service.WriteAsync(gone)).Result);

        using var source = new EpubBookSource(epub);
        Assert.Equal("Dune Chronicles", source.Metadata.SeriesName);
        Assert.Equal("Ace Books", source.Metadata.Publisher);
    }

    [Fact]
    public async Task AutoWriteAsync_DoesNothingUnlessBothSwitchesAreOn()
    {
        var service = new BookFileWriteBackService(() => new PaperbunkrDbContext(_dbOptions));
        string epub = MakeEpub("d.epub", seriesInOpf: false);
        int id = AddBook(epub);

        Assert.Equal(0, await service.AutoWriteAsync(new[] { id }));

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var settings = context.GetOrCreateAppSettings();
            settings.WriteMetadataToFiles = true;
            context.SaveChanges();
        }

        Assert.Equal(0, await service.AutoWriteAsync(new[] { id }));

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().WriteMetadataAutomatically = true;
            context.SaveChanges();
        }

        Assert.Equal(1, await service.AutoWriteAsync(new[] { id, id }));
    }
}
