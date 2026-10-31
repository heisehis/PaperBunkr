using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>The fill-only / never-overwrite-a-user-edit rules (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §3.4, §4).</summary>
public class BookMetadataApplierTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static Book NewBook() => new() { Title = "T", FilePath = @"C:\t.epub", Format = BookFormat.Epub };

    private static Func<string, BookSeries> Resolver(Dictionary<string, BookSeries>? cache = null)
    {
        cache ??= new(StringComparer.OrdinalIgnoreCase);
        return name =>
        {
            if (!cache.TryGetValue(name, out var s))
            {
                cache[name] = s = new BookSeries { Name = name };
            }

            return s;
        };
    }

    private static BookFieldValues Values() => new()
    {
        Subtitle = "A Subtitle",
        Publisher = "Ace",
        LanguageISO = "en",
        ISBN = "9780441172719",
        SeriesName = "Dune Chronicles",
        SeriesIndex = 2f,
        Summary = "Desert planet.",
        Rating = 3.6f,
        Genres = new[] { "SF", "Classic" },
        Tags = new[] { "favorite" },
    };

    [Fact]
    public void FillEmpty_SetsEveryEmptyField_AndRecordsFileSources()
    {
        var book = NewBook();

        var changed = BookMetadataApplier.Apply(book, Values(), BookFieldSourceKind.File, null, Now, Resolver());

        Assert.Equal("A Subtitle", book.Subtitle);
        Assert.Equal("Ace", book.Publisher);
        Assert.Equal("en", book.LanguageISO);
        Assert.Equal("9780441172719", book.ISBN);
        Assert.Equal("Dune Chronicles", book.BookSeries!.Name);
        Assert.Equal(2f, book.SeriesIndex);
        Assert.Equal("Desert planet.", book.Summary);
        Assert.Equal(3.5f, book.Rating); // normalised to a 0.5 step
        Assert.Equal(new[] { "SF", "Classic" }, book.Tags.Where(t => t.Field == BookTagField.Genre).Select(t => t.Value));
        Assert.Equal(new[] { "favorite" }, book.Tags.Where(t => t.Field == BookTagField.Tags).Select(t => t.Value));

        Assert.Contains(BookFields.Publisher, changed);
        Assert.Contains(BookFields.Series, changed);
        Assert.All(book.FieldSources, s => Assert.Equal(BookFieldSourceKind.File, s.Source));
        Assert.Equal(changed.Count, book.FieldSources.Count);
        Assert.Contains(book.BookSeries.Books, b => ReferenceEquals(b, book));
    }

    [Fact]
    public void FillEmpty_NeverOverwritesExistingValues()
    {
        var book = NewBook();
        book.Publisher = "Mine";
        book.Rating = 5f;
        book.BookSeries = new BookSeries { Name = "Existing" };
        book.SeriesIndex = 9f;

        var changed = BookMetadataApplier.Apply(book, Values(), BookFieldSourceKind.File, null, Now, Resolver());

        Assert.Equal("Mine", book.Publisher);
        Assert.Equal(5f, book.Rating);
        Assert.Equal("Existing", book.BookSeries.Name);
        Assert.Equal(9f, book.SeriesIndex);
        Assert.DoesNotContain(BookFields.Publisher, changed);
        Assert.DoesNotContain(BookFields.Series, changed);
        Assert.DoesNotContain(BookFields.SeriesIndex, changed);
    }

    [Fact]
    public void FillEmpty_SkipsAFieldTheUserClearedOnPurpose()
    {
        var book = NewBook();
        BookMetadataApplier.MarkManual(book, new[] { BookFields.Publisher, BookFields.Genre }, Now);

        var changed = BookMetadataApplier.Apply(book, Values(), BookFieldSourceKind.File, null, Now, Resolver());

        Assert.Null(book.Publisher);
        Assert.DoesNotContain(BookFields.Publisher, changed);
        Assert.DoesNotContain(book.Tags, t => t.Field == BookTagField.Genre);
        Assert.Contains(book.Tags, t => t.Field == BookTagField.Tags); // other fields still fill
        Assert.Equal(BookFieldSourceKind.Manual, book.FieldSources.Single(s => s.Field == BookFields.Publisher).Source);
    }

    [Fact]
    public void Override_ReplacesOnlyTheRequestedFields_EvenManualOnes()
    {
        var book = NewBook();
        book.Publisher = "Mine";
        book.Subtitle = "Keep";
        BookMetadataApplier.MarkManual(book, new[] { BookFields.Publisher }, Now);

        var changed = BookMetadataApplier.Apply(
            book, Values(), BookFieldSourceKind.OpenLibrary, "OL123W", Now.AddHours(1), Resolver(),
            BookApplyMode.Override, new HashSet<string> { BookFields.Publisher });

        Assert.Equal(new[] { BookFields.Publisher }, changed);
        Assert.Equal("Ace", book.Publisher);
        Assert.Equal("Keep", book.Subtitle);
        var row = book.FieldSources.Single(s => s.Field == BookFields.Publisher);
        Assert.Equal(BookFieldSourceKind.OpenLibrary, row.Source);
        Assert.Equal("OL123W", row.SourceRef);
        Assert.Equal(Now.AddHours(1), row.FetchedUtc);
    }

    [Fact]
    public void SeriesIndex_WithoutADeclaredSeriesAndNoExistingOne_IsNotApplied()
    {
        var book = NewBook();
        var values = new BookFieldValues { SeriesIndex = 4f };

        var changed = BookMetadataApplier.Apply(book, values, BookFieldSourceKind.File, null, Now, Resolver());

        Assert.Null(book.SeriesIndex);
        Assert.Empty(changed);
    }

    [Fact]
    public void Tags_MergeAdditively_CaseInsensitively()
    {
        var book = NewBook();
        book.Tags.Add(new BookTag { Field = BookTagField.Genre, Value = "sf" });

        var changed = BookMetadataApplier.Apply(
            book, new BookFieldValues { Genres = new[] { "SF", "Horror" } }, BookFieldSourceKind.GoogleBooks, null, Now, Resolver());

        Assert.Equal(new[] { "sf", "Horror" }, book.Tags.Select(t => t.Value));
        Assert.Contains(BookFields.Genre, changed);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0f, null)]
    [InlineData(-1f, null)]
    [InlineData(0.2f, 0f)]
    [InlineData(3.74f, 3.5f)]
    [InlineData(3.75f, 4f)]
    [InlineData(9f, 5f)]
    public void BookRating_Normalize(float? input, float? expected)
    {
        // 0.2 rounds to 0 which is "no stars": callers treat that like unrated. The helper only guarantees the 0.5 grid and the 5 cap.
        Assert.Equal(expected is 0f ? 0f : expected, BookRating.Normalize(input));
    }
}
