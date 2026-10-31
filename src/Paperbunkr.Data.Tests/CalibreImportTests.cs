using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Calibre;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Calibre import (docs/superpowers/specs/2026-10-27-books-slice4a-calibre-import-design.md). The fixture builds a library with the real
/// <c>metadata.db</c> table layout (checked against a real Calibre library) and tiny placeholder format files.
/// </summary>
public class CalibreImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_calibre_{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private SqliteConnection? _calibre;

    public CalibreImportTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_calibre_app_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _calibre = new SqliteConnection($"Data Source={Path.Combine(_root, "metadata.db")};Pooling=False");
        _calibre.Open();
        Exec("""
            create table books (id integer primary key, title text, sort text, timestamp text, pubdate text, series_index real, author_sort text, path text, uuid text, has_cover bool, last_modified text);
            create table authors (id integer primary key, name text, sort text, link text);
            create table books_authors_link (id integer primary key, book integer, author integer);
            create table series (id integer primary key, name text, sort text, link text);
            create table books_series_link (id integer primary key, book integer, series integer);
            create table tags (id integer primary key, name text, link text);
            create table books_tags_link (id integer primary key, book integer, tag integer);
            create table publishers (id integer primary key, name text, sort text, link text);
            create table books_publishers_link (id integer primary key, book integer, publisher integer);
            create table languages (id integer primary key, lang_code text, link text);
            create table books_languages_link (id integer primary key, book integer, lang_code integer, item_order integer);
            create table ratings (id integer primary key, rating integer, link text);
            create table books_ratings_link (id integer primary key, book integer, rating integer);
            create table comments (id integer primary key, book integer, text text);
            create table identifiers (id integer primary key, book integer, type text, val text);
            create table data (id integer primary key, book integer, format text, uncompressed_size integer, name text);
            create table custom_columns (id integer primary key, label text, name text, datatype text, mark_for_delete bool default 0, editable bool, display text, is_multiple bool, normalized bool);
            create table library_id (id integer primary key, uuid text);
            insert into library_id (uuid) values ('lib-uuid-1');
            """);
    }

    public void Dispose()
    {
        _calibre?.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private void Exec(string sql)
    {
        using var command = _calibre!.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Q(string? value) => value is null ? "null" : "'" + value.Replace("'", "''") + "'";

    /// <summary>Adds one Calibre book with its format files on disk.</summary>
    private void AddBook(
        int id, string title, string author, string[]? formats = null, string? series = null, double index = 1, string[]? tags = null,
        string? publisher = null, string? language = null, string? comments = null, string? isbn = null, int rating = 0, bool cover = false,
        string pubdate = "2011-03-09 00:00:00+00:00", bool createFiles = true)
    {
        formats ??= ["EPUB"];
        string folder = $"{author}/{title} ({id})";
        Exec($"insert into books (id, title, sort, pubdate, series_index, path, has_cover) values ({id}, {Q(title)}, {Q(title)}, {Q(pubdate)}, {index.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {Q(folder)}, {(cover ? 1 : 0)})");
        Exec($"insert or ignore into authors (id, name) values ({id}, {Q(author)}); insert into books_authors_link (book, author) values ({id}, {id})");
        string dir = Path.Combine(_root, author, $"{title} ({id})");
        Directory.CreateDirectory(dir);
        foreach (string format in formats)
        {
            Exec($"insert into data (book, format, uncompressed_size, name) values ({id}, {Q(format)}, 1, {Q(title + " - " + author)})");
            if (createFiles)
            {
                File.WriteAllText(Path.Combine(dir, $"{title} - {author}.{format.ToLowerInvariant()}"), "x");
            }
        }

        if (cover)
        {
            File.WriteAllText(Path.Combine(dir, "cover.jpg"), "x");
        }

        if (series is not null)
        {
            Exec($"insert or ignore into series (id, name) values ({Math.Abs(series.GetHashCode()) % 100000}, {Q(series)}); insert into books_series_link (book, series) values ({id}, {Math.Abs(series.GetHashCode()) % 100000})");
        }

        foreach (string tag in tags ?? [])
        {
            int tagId = Math.Abs(tag.GetHashCode()) % 100000;
            Exec($"insert or ignore into tags (id, name) values ({tagId}, {Q(tag)}); insert into books_tags_link (book, tag) values ({id}, {tagId})");
        }

        if (publisher is not null)
        {
            Exec($"insert or ignore into publishers (id, name) values ({id}, {Q(publisher)}); insert into books_publishers_link (book, publisher) values ({id}, {id})");
        }

        if (language is not null)
        {
            Exec($"insert or ignore into languages (id, lang_code) values ({id}, {Q(language)}); insert into books_languages_link (book, lang_code, item_order) values ({id}, {id}, 0)");
        }

        if (comments is not null)
        {
            Exec($"insert into comments (book, text) values ({id}, {Q(comments)})");
        }

        if (isbn is not null)
        {
            Exec($"insert into identifiers (book, type, val) values ({id}, 'isbn', {Q(isbn)})");
        }

        if (rating > 0)
        {
            Exec($"insert or ignore into ratings (id, rating) values ({rating}, {rating}); insert into books_ratings_link (book, rating) values ({id}, {rating})");
        }
    }

    private CalibreImporter Importer(Action<int, string>? cover = null) => new(() => new PaperbunkrDbContext(_dbOptions), cover);

    private PaperbunkrDbContext Db() => new(_dbOptions);

    [Fact]
    public void Reader_ReadsTheRealTableLayout()
    {
        AddBook(1, "Dune", "Frank Herbert", series: "Dune Saga", index: 1, tags: ["Sci-Fi", "Classic"], publisher: "Chilton", language: "eng",
            comments: "<p>Spice.</p><p>Worms.</p>", isbn: "9780441172719", rating: 8);
        _calibre!.Dispose();
        _calibre = null;

        using var reader = CalibreLibraryReader.Open(_root);
        var book = Assert.Single(reader.ReadBooks());

        Assert.Equal("lib-uuid-1", reader.LibraryId);
        Assert.Equal("Dune", book.Title);
        Assert.Equal(["Frank Herbert"], book.Authors);
        Assert.Equal("Dune Saga", book.Series);
        Assert.Equal(["Sci-Fi", "Classic"], book.Tags);
        Assert.Equal("Chilton", book.Publisher);
        Assert.Equal("eng", book.Language);
        Assert.Equal("9780441172719", book.Isbn);
        Assert.Equal(8, book.Rating10);
        Assert.Equal(new DateTime(2011, 3, 9), book.PubDate!.Value.Date);
        Assert.Equal("EPUB", Assert.Single(book.Formats).Format);
    }

    [Fact]
    public void Reader_TreatsCalibresYear101AsNoDate()
    {
        Assert.Null(CalibreLibraryReader.ParsePubDate("0101-01-01 00:00:00+00:00"));
        Assert.Null(CalibreLibraryReader.ParsePubDate(null));
        Assert.NotNull(CalibreLibraryReader.ParsePubDate("2020-05-01 00:00:00+00:00"));
    }

    [Fact]
    public void Mapper_ChoosesEpubThenFb2ThenMobiThenPdf()
    {
        static CalibreFormat F(string f) => new(f, "n");

        Assert.Equal("EPUB", CalibreMapper.ChooseFormat([F("PDF"), F("EPUB")])!.Value.Format.Format);
        Assert.Equal("FB2", CalibreMapper.ChooseFormat([F("PDF"), F("FB2"), F("MOBI")])!.Value.Format.Format);
        Assert.Equal(BookFormat.Mobi, CalibreMapper.ChooseFormat([F("PDF"), F("AZW3")])!.Value.BookFormat);
        Assert.Equal(BookFormat.Pdf, CalibreMapper.ChooseFormat([F("PDF")])!.Value.BookFormat);
        Assert.Null(CalibreMapper.ChooseFormat([F("DOCX"), F("TXT")]));
    }

    [Fact]
    public void Mapper_MapsRatingLanguageAuthorsAndHtml()
    {
        Assert.Equal(5f, CalibreMapper.MapRating(10));
        Assert.Equal(3.5f, CalibreMapper.MapRating(7));
        Assert.Null(CalibreMapper.MapRating(0));
        Assert.Equal("en", CalibreMapper.MapLanguage("eng"));
        Assert.Equal("fr", CalibreMapper.MapLanguage("fra"));
        Assert.Equal("xyz", CalibreMapper.MapLanguage("xyz"));
        Assert.Equal("Martin, George R. R., Other", CalibreMapper.JoinAuthors(["Martin| George R. R.", "Other"]));
        Assert.Equal("One\n\nTwo & three", CalibreMapper.HtmlToText("<p>One</p><p>Two &amp; <b>three</b></p>"));
        Assert.Null(CalibreMapper.HtmlToText("<p> </p>"));
    }

    [Fact]
    public void Import_AddsBooksWithTheirMetadataAndProvenance()
    {
        AddBook(1, "Dune", "Frank Herbert", formats: ["PDF", "EPUB"], series: "Dune Saga", index: 1, tags: ["Sci-Fi"], publisher: "Chilton",
            language: "eng", comments: "<p>Spice.</p>", isbn: "9780441172719", rating: 8);
        _calibre!.Dispose();
        _calibre = null;

        var result = Importer().Import(_root, new CalibreImportOptions());

        Assert.Equal(1, result.Added);
        using var db = Db();
        var book = db.Books.Include(b => b.Tags).Include(b => b.FieldSources).Include(b => b.BookSeries).Single();
        Assert.Equal("Dune", book.Title);
        Assert.Equal("Frank Herbert", book.Author);
        Assert.Equal(BookFormat.Epub, book.Format);
        Assert.EndsWith(".epub", book.FilePath);
        Assert.Equal("Chilton", book.Publisher);
        Assert.Equal("en", book.LanguageISO);
        Assert.Equal("9780441172719", book.ISBN);
        Assert.Equal("Spice.", book.Summary);
        Assert.Equal(4f, book.Rating);
        Assert.Equal("Dune Saga", book.BookSeries!.Name);
        Assert.Equal(1f, book.SeriesIndex);
        Assert.Contains(book.Tags, t => t.Value == "Sci-Fi");
        Assert.Equal(1, book.CalibreBookId);
        Assert.Equal("lib-uuid-1", book.CalibreLibraryId);
        Assert.All(book.FieldSources.Where(s => s.Field == BookFields.Publisher), s => Assert.Equal(BookFieldSourceKind.Calibre, s.Source));
    }

    [Fact]
    public void Import_SecondRunAddsNothingAndIsIdempotent()
    {
        AddBook(1, "A", "Au");
        AddBook(2, "B", "Au");
        _calibre!.Dispose();
        _calibre = null;
        var importer = Importer();

        Assert.Equal(2, importer.Import(_root, new CalibreImportOptions()).Added);
        var second = importer.Import(_root, new CalibreImportOptions());

        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(2, Db().Books.Count());
    }

    [Fact]
    public void Import_SkipsMissingFilesAndUnsupportedFormats()
    {
        AddBook(1, "Gone", "Au", createFiles: false);
        AddBook(2, "Doc", "Au", formats: ["DOCX"]);
        AddBook(3, "Fine", "Au");
        _calibre!.Dispose();
        _calibre = null;

        var result = Importer().Import(_root, new CalibreImportOptions());

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.FileNotFound);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Import_NeverTouchesAManualFieldInEitherMode()
    {
        AddBook(1, "Dune", "Frank Herbert", publisher: "Chilton", comments: "Calibre text");
        _calibre!.Dispose();
        _calibre = null;
        Importer().Import(_root, new CalibreImportOptions());
        using (var db = Db())
        {
            var book = db.Books.Include(b => b.FieldSources).Single();
            book.Publisher = "My Edit";
            book.Summary = "My summary";
            BookMetadataApplier.MarkManual(book, [BookFields.Publisher, BookFields.Summary], DateTime.UtcNow);
            db.SaveChanges();
        }

        Importer().Import(_root, new CalibreImportOptions(RefreshExisting: true));

        using var check = Db();
        var after = check.Books.Single();
        Assert.Equal("My Edit", after.Publisher);
        Assert.Equal("My summary", after.Summary);
    }

    [Fact]
    public void Import_RefreshUpdatesNonManualFieldsAndFillOnlyDoesNot()
    {
        AddBook(1, "Dune", "Frank Herbert", publisher: "Chilton");
        _calibre!.Dispose();
        _calibre = null;
        Importer().Import(_root, new CalibreImportOptions());
        using (var db = Db())
        {
            db.Books.Single().Publisher = "Changed Elsewhere";
            db.SaveChanges();
        }

        Importer().Import(_root, new CalibreImportOptions());
        Assert.Equal("Changed Elsewhere", Db().Books.Single().Publisher);

        var refreshed = Importer().Import(_root, new CalibreImportOptions(RefreshExisting: true));
        Assert.Equal("Chilton", Db().Books.Single().Publisher);
        Assert.Equal(1, refreshed.Updated);
    }

    [Fact]
    public void Import_MatchesAnExistingBookByFilePath()
    {
        AddBook(1, "Dune", "Frank Herbert", publisher: "Chilton");
        string file = Path.Combine(_root, "Frank Herbert", "Dune (1)", "Dune - Frank Herbert.epub");
        using (var db = Db())
        {
            db.Books.Add(new Book { Title = "Dune (mine)", FilePath = file, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow });
            db.SaveChanges();
        }

        _calibre!.Dispose();
        _calibre = null;

        var result = Importer().Import(_root, new CalibreImportOptions());

        Assert.Equal(0, result.Added);
        using var check = Db();
        var book = check.Books.Single();
        Assert.Equal("Dune (mine)", book.Title);
        Assert.Equal(1, book.CalibreBookId);
        Assert.Equal("Chilton", book.Publisher);
    }

    [Fact]
    public void Import_OffersSameIsbnAndSameTitleAuthorForReviewInsteadOfDuplicating()
    {
        AddBook(1, "Dune", "Frank Herbert", isbn: "978-0-441-17271-9");
        AddBook(2, "Emma", "Jane Austen");
        AddBook(3, "Other", "Someone");
        string elsewhere = Path.Combine(Path.GetTempPath(), "elsewhere");
        using (var db = Db())
        {
            db.Books.Add(new Book { Title = "Dune!", ISBN = "9780441172719", FilePath = Path.Combine(elsewhere, "a.epub"), Format = BookFormat.Epub, AddedTime = DateTime.UtcNow });
            db.Books.Add(new Book { Title = "EMMA", Author = "jane austen", FilePath = Path.Combine(elsewhere, "b.epub"), Format = BookFormat.Epub, AddedTime = DateTime.UtcNow });
            db.SaveChanges();
        }

        _calibre!.Dispose();
        _calibre = null;

        var result = Importer().Import(_root, new CalibreImportOptions());

        Assert.Equal(1, result.Added);
        Assert.Equal(2, result.NeedsReview);
        Assert.Contains(result.Review, r => r.Reason == "Same ISBN" && r.Title == "Dune");
        Assert.Contains(result.Review, r => r.Reason == "Same title and author" && r.Title == "Emma");
        Assert.Equal(3, Db().Books.Count()); // the two existing books plus "Other"; no duplicates of the reviewed ones
    }

    [Fact]
    public void Link_TurnsAnExistingBookIntoTheCalibreBook()
    {
        AddBook(1, "Dune", "Frank Herbert", isbn: "9780441172719", publisher: "Chilton");
        int existingId;
        using (var db = Db())
        {
            var existing = new Book { Title = "Dune!", ISBN = "9780441172719", FilePath = "elsewhere.epub", Format = BookFormat.Epub, AddedTime = DateTime.UtcNow };
            db.Books.Add(existing);
            db.SaveChanges();
            existingId = existing.Id;
        }

        _calibre!.Dispose();
        _calibre = null;
        var importer = Importer();
        var item = importer.Import(_root, new CalibreImportOptions()).Review.Single();

        importer.Link(_root, item, new CalibreImportOptions());

        using var check = Db();
        var linked = check.Books.Single(b => b.Id == existingId);
        Assert.Equal(1, linked.CalibreBookId);
        Assert.Equal("Chilton", linked.Publisher);
        Assert.Equal(1, check.Books.Count());
    }

    [Fact]
    public void Import_CountsBooksNoLongerInCalibreWithoutDeletingThem()
    {
        AddBook(1, "A", "Au");
        AddBook(2, "B", "Au");
        _calibre!.Dispose();
        _calibre = null;
        var importer = Importer();
        importer.Import(_root, new CalibreImportOptions());

        using (var calibre = new SqliteConnection($"Data Source={Path.Combine(_root, "metadata.db")};Pooling=False"))
        {
            calibre.Open();
            using var command = calibre.CreateCommand();
            command.CommandText = "delete from books where id = 2; delete from data where book = 2;";
            command.ExecuteNonQuery();
        }

        var result = importer.Import(_root, new CalibreImportOptions());

        Assert.Equal(1, result.NoLongerInCalibre);
        Assert.Equal(2, Db().Books.Count());
    }

    [Fact]
    public void Import_ReadStatusColumnMarksBooksFinished()
    {
        AddBook(1, "Read", "Au");
        AddBook(2, "Unread", "Au");
        Exec("insert into custom_columns (id, label, name, datatype) values (1, 'read', 'Read', 'bool');"
            + "create table custom_column_1 (id integer primary key, book integer, value bool);"
            + "insert into custom_column_1 (book, value) values (1, 1), (2, 0);");
        _calibre!.Dispose();
        _calibre = null;

        Importer().Import(_root, new CalibreImportOptions(ReadStatusColumn: "read"));

        using var db = Db();
        Assert.Equal(BookReadingStatus.Finished, db.Books.Single(b => b.Title == "Read").Status);
        Assert.Equal(BookReadingStatus.Unread, db.Books.Single(b => b.Title == "Unread").Status);
    }

    [Fact]
    public void Import_SetsCoversOnlyForNewBooks()
    {
        AddBook(1, "Pic", "Au", cover: true);
        AddBook(2, "NoPic", "Au");
        _calibre!.Dispose();
        _calibre = null;
        var covers = new List<(int Id, string Path)>();
        var importer = Importer((id, path) => covers.Add((id, path)));

        importer.Import(_root, new CalibreImportOptions());
        importer.Import(_root, new CalibreImportOptions());

        var only = Assert.Single(covers);
        Assert.EndsWith("cover.jpg", only.Path);
    }

    [Fact]
    public void Reader_FallsBackToACopyWhenTheDatabaseCannotBeOpenedInPlace()
    {
        AddBook(1, "Locked", "Au");
        _calibre!.Dispose();
        _calibre = null;
        string db = Path.Combine(_root, "metadata.db");

        // An exclusive hold on the file makes the in-place open fail on some platforms; the copy path must still read it.
        using var hold = new FileStream(db, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = CalibreLibraryReader.Open(_root);

        Assert.Equal("Locked", Assert.Single(reader.ReadBooks()).Title);
    }

    /// <summary>
    /// A smoke test against the developer's own Calibre library when it exists (otherwise a no-op): read-only on Calibre's side, imported into
    /// this test's scratch database, never the real one.
    /// </summary>
    [Fact]
    public void RealLibrary_ImportsIntoAScratchDatabase()
    {
        string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Calibre Library");
        if (!CalibreLibraryReader.IsCalibreLibrary(real))
        {
            return;
        }

        int expected;
        using (var reader = CalibreLibraryReader.Open(real))
        {
            expected = reader.ReadBooks().Count(b => CalibreMapper.ChooseFormat(b.Formats) is not null);
        }

        var result = Importer().Import(real, new CalibreImportOptions());

        Assert.Equal(expected, result.Added);
        Assert.Equal(0, result.FileNotFound);
        using var db = Db();
        Assert.Equal(expected, db.Books.Count());
        Assert.All(db.Books.ToList(), b => Assert.False(string.IsNullOrWhiteSpace(b.Title)));
        Assert.Equal(0, Importer().Import(real, new CalibreImportOptions()).Added);
    }

    [Fact]
    public void Open_RejectsAFolderWithoutMetadataDb()
    {
        string empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        Assert.False(CalibreLibraryReader.IsCalibreLibrary(empty));
        Assert.Throws<FileNotFoundException>(() => CalibreLibraryReader.Open(empty));
    }
}
