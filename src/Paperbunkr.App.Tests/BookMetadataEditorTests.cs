using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The Books slice 1 fields in the single-book and bulk editors (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §6).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookMetadataEditorTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public BookMetadataEditorTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookmeta_editor_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private static BookPropertiesScreenViewModel Single(
        MetadataEditHistoryService? history = null, Action<string, string>? notify = null, Func<int, Task>? writeBack = null) =>
        new(() => { }, PaperbunkrDb.CreateContext, notify, history ?? new MetadataEditHistoryService(), writeBack);

    private static BulkBookPropertiesScreenViewModel Bulk(MetadataEditHistoryService? history = null, Func<IReadOnlyList<int>, Task>? writeBack = null) =>
        new(() => { }, PaperbunkrDb.CreateContext, null, history ?? new MetadataEditHistoryService(), writeBack);

    private static int AddBook(string title, Action<Book>? configure = null)
    {
        var book = new Book { Title = title, Format = BookFormat.Epub, FilePath = $@"C:\b\{title}.epub", AddedTime = DateTime.UtcNow };
        configure?.Invoke(book);
        using var context = PaperbunkrDb.CreateContext();
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private static Book Get(int id)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Books.Include(b => b.Tags).Include(b => b.FieldSources).Include(b => b.BookSeries).Single(b => b.Id == id);
    }

    [Fact]
    public void Load_ShowsTheNewFields_AndProvenanceChips()
    {
        int id = AddBook("Dune", b =>
        {
            b.Publisher = "Ace";
            b.ISBN = "9780441172719";
            b.Rating = 4.5f;
            b.Status = BookReadingStatus.WantToRead;
            b.SeriesIndex = 2.5f;
            b.BookSeries = new BookSeries { Name = "Dune Chronicles" };
            b.Tags.Add(new BookTag { Field = BookTagField.Genre, Value = "SF" });
            b.Tags.Add(new BookTag { Field = BookTagField.Tags, Value = "classic" });
            b.FieldSources.Add(new BookFieldSource { Field = BookFields.Publisher, Source = BookFieldSourceKind.File, FetchedUtc = DateTime.UtcNow });
            b.FieldSources.Add(new BookFieldSource { Field = BookFields.Rating, Source = BookFieldSourceKind.Manual, FetchedUtc = DateTime.UtcNow });
        });
        var vm = Single();

        vm.Load(id);

        Assert.Equal("Ace", vm.Publisher);
        Assert.Equal("9780441172719", vm.Isbn);
        Assert.Equal(4.5, vm.Rating);
        Assert.Equal("Want to read", vm.StatusLabel);
        Assert.Equal("2.5", vm.SeriesIndexText);
        Assert.Equal("SF", vm.Genres);
        Assert.Equal("classic", vm.Tags);
        Assert.Equal(new[] { "Publisher · file", "Rating · you" }, vm.SourceChips.Select(c => c.Text));
        Assert.False(vm.HasUnsavedChanges());
    }

    [Fact]
    public void Save_RoundTripsTheFields_AndMarksOnlyChangedOnesManual()
    {
        int id = AddBook("Dune", b =>
        {
            b.Publisher = "Ace";
            b.FieldSources.Add(new BookFieldSource { Field = BookFields.Publisher, Source = BookFieldSourceKind.File, FetchedUtc = DateTime.UtcNow });
        });
        var vm = Single();
        vm.Load(id);

        vm.Subtitle = "Book One";
        vm.LanguageIso = "en";
        vm.Rating = 3.7; // snapped by the control; the VM normalises again on save
        vm.StatusLabel = "Finished";
        vm.Genres = "SF, Classics";
        vm.Tags = "desert";
        vm.SeriesName = "Dune Chronicles";
        vm.SeriesIndexText = "1";
        vm.SaveCommand.Execute(null);

        var book = Get(id);
        Assert.Equal("Ace", book.Publisher);
        Assert.Equal("Book One", book.Subtitle);
        Assert.Equal("en", book.LanguageISO);
        Assert.Equal(3.5f, book.Rating);
        Assert.Equal(BookReadingStatus.Finished, book.Status);
        Assert.True(book.Finished);
        Assert.Equal(new[] { "Classics", "SF" }, book.Tags.Where(t => t.Field == BookTagField.Genre).Select(t => t.Value).OrderBy(v => v));
        Assert.Equal("desert", book.Tags.Single(t => t.Field == BookTagField.Tags).Value);
        Assert.Equal("Dune Chronicles", book.BookSeries!.Name);
        Assert.Equal(1f, book.SeriesIndex);

        var sources = book.FieldSources.ToDictionary(s => s.Field, s => s.Source);
        Assert.Equal(BookFieldSourceKind.File, sources[BookFields.Publisher]); // untouched stays File
        Assert.Equal(BookFieldSourceKind.Manual, sources[BookFields.Language]);
        Assert.Equal(BookFieldSourceKind.Manual, sources[BookFields.Rating]);
        Assert.Equal(BookFieldSourceKind.Manual, sources[BookFields.Genre]);
        Assert.Equal(BookFieldSourceKind.Manual, sources[BookFields.Series]);
        Assert.DoesNotContain(BookFields.Isbn, sources.Keys);
    }

    [Fact]
    public void ClearingARating_AndTheSeries_ClearsTheirValues()
    {
        int id = AddBook("Dune", b =>
        {
            b.Rating = 5f;
            b.SeriesIndex = 3f;
            b.BookSeries = new BookSeries { Name = "S" };
        });
        var vm = Single();
        vm.Load(id);

        vm.Rating = null;
        vm.SeriesName = string.Empty;
        vm.SaveCommand.Execute(null);

        var book = Get(id);
        Assert.Null(book.Rating);
        Assert.Null(book.BookSeriesId);
        Assert.Null(book.SeriesIndex);
    }

    [Fact]
    public void BadSeriesNumber_OrStatus_BlocksSave()
    {
        int id = AddBook("Dune");
        var messages = new List<string>();
        var vm = Single(notify: (_, m) => messages.Add(m));
        vm.Load(id);

        vm.SeriesIndexText = "two";
        vm.SaveCommand.Execute(null);
        vm.SeriesIndexText = string.Empty;
        vm.StatusLabel = "Binge-reading";
        vm.SaveCommand.Execute(null);

        Assert.Equal(2, messages.Count);
        Assert.Contains("number", messages[0]);
        Assert.Contains("reading status", messages[1]);
        Assert.Equal(BookReadingStatus.Unread, Get(id).Status);
    }

    [Fact]
    public void SuggestedSeries_AppearsForAMarkedTitle_AcceptFillsTheFields_AndNeverForABookInASeries()
    {
        int loose = AddBook("Dune Messiah (Dune Chronicles #2)");
        int inSeries = AddBook("Other (Foo #1)", b => b.BookSeries = new BookSeries { Name = "Mine" });

        var vm = Single();
        vm.Load(loose);
        Assert.True(vm.HasSuggestedSeries);
        Assert.Equal("Dune Chronicles #2", vm.SuggestedSeriesText);
        Assert.Equal(string.Empty, vm.SeriesName);

        vm.AcceptSuggestedSeriesCommand.Execute(null);
        Assert.Equal("Dune Chronicles", vm.SeriesName);
        Assert.Equal("2", vm.SeriesIndexText);
        Assert.False(vm.HasSuggestedSeries);

        var other = Single();
        other.Load(inSeries);
        Assert.False(other.HasSuggestedSeries);

        var dismissed = Single();
        dismissed.Load(loose);
        dismissed.DismissSuggestedSeriesCommand.Execute(null);
        Assert.False(dismissed.HasSuggestedSeries);
        Assert.Equal(string.Empty, dismissed.SeriesName);
    }

    [Fact]
    public void UndoAndRedo_RestoreRatingStatusTagsAndPublisher()
    {
        int id = AddBook("Dune", b =>
        {
            b.Publisher = "Ace";
            b.Rating = 2f;
            b.Tags.Add(new BookTag { Field = BookTagField.Tags, Value = "old" });
        });
        var history = new MetadataEditHistoryService();
        var vm = Single(history);
        vm.Load(id);

        vm.Publisher = "Tor";
        vm.Rating = 5;
        vm.StatusLabel = "Abandoned";
        vm.Tags = "new, newer";
        vm.SaveCommand.Execute(null);

        history.Undo(PaperbunkrDb.CreateContext);
        var undone = Get(id);
        Assert.Equal("Ace", undone.Publisher);
        Assert.Equal(2f, undone.Rating);
        Assert.Equal(BookReadingStatus.Unread, undone.Status);
        Assert.Equal(new[] { "old" }, undone.Tags.Select(t => t.Value));

        history.Redo(PaperbunkrDb.CreateContext);
        var redone = Get(id);
        Assert.Equal("Tor", redone.Publisher);
        Assert.Equal(5f, redone.Rating);
        Assert.Equal(BookReadingStatus.Abandoned, redone.Status);
        Assert.Equal(new[] { "new", "newer" }, redone.Tags.Select(t => t.Value).OrderBy(v => v));
    }

    [Fact]
    public void Save_AsksForAFileWriteBack_OfThatBook()
    {
        int id = AddBook("Dune");
        var asked = new List<int>();
        var vm = Single(writeBack: bookId => { asked.Add(bookId); return Task.CompletedTask; });
        vm.Load(id);

        vm.Publisher = "Ace";
        vm.SaveCommand.Execute(null);

        Assert.Equal(new[] { id }, asked);
    }

    [Fact]
    public void Vocabulary_ListsWhatTheLibraryAlreadyHolds()
    {
        AddBook("A", b =>
        {
            b.Publisher = "Tor";
            b.Tags.Add(new BookTag { Field = BookTagField.Genre, Value = "Fantasy" });
            b.Tags.Add(new BookTag { Field = BookTagField.Tags, Value = "dragons" });
            b.BookSeries = new BookSeries { Name = "Wheel" };
        });
        int id = AddBook("B");
        var vm = Single();

        vm.Load(id);

        Assert.Contains("Tor", vm.Vocabulary.Publishers);
        Assert.Contains("Fantasy", vm.Vocabulary.Genres);
        Assert.Contains("dragons", vm.Vocabulary.Tags);
        Assert.Contains("Wheel", vm.Vocabulary.SeriesNames);
        Assert.Contains("en", vm.Vocabulary.Languages);
    }

    // ---- Bulk ----

    [Fact]
    public void Bulk_OnlyTickedFieldsAreWritten_AndTagsAddAndRemove()
    {
        int a = AddBook("A", b =>
        {
            b.Publisher = "Keep A";
            b.Rating = 2f;
            b.Tags.Add(new BookTag { Field = BookTagField.Tags, Value = "old" });
            b.Tags.Add(new BookTag { Field = BookTagField.Genre, Value = "old" });
        });
        int c = AddBook("C", b => b.Publisher = "Keep C");
        var vm = Bulk();
        vm.Load(new[] { a, c });

        vm.StatusLabel = "Reading";
        vm.AddGenres = "Fantasy";
        vm.AddTags = "series-night";
        vm.RemoveTags = "old";
        vm.SaveCommand.Execute(null);

        var first = Get(a);
        var second = Get(c);
        Assert.Equal(BookReadingStatus.Reading, first.Status);
        Assert.Equal(BookReadingStatus.Reading, second.Status);
        Assert.Equal("Keep A", first.Publisher);
        Assert.Equal("Keep C", second.Publisher);
        Assert.Equal(2f, first.Rating);
        Assert.Equal(new[] { "Fantasy" }, first.Tags.Where(t => t.Field == BookTagField.Genre).Select(t => t.Value));
        Assert.Equal(new[] { "series-night" }, first.Tags.Where(t => t.Field == BookTagField.Tags).Select(t => t.Value));
        Assert.Equal(BookFieldSourceKind.Manual, first.FieldSources.Single(s => s.Field == BookFields.Genre).Source);
        Assert.DoesNotContain(first.FieldSources, s => s.Field == BookFields.Publisher);
    }

    [Fact]
    public void Bulk_TickedRatingWithNoStars_ClearsIt_AndPublisherApplies()
    {
        int a = AddBook("A", b => b.Rating = 4f);
        int c = AddBook("C", b => b.Rating = 1f);
        var vm = Bulk();
        vm.Load(new[] { a, c });

        vm.Rating = 3;
        vm.Rating = null; // ticking happens on any change; null with the box ticked means clear
        vm.Publisher = "Tor";
        vm.SaveCommand.Execute(null);

        Assert.Null(Get(a).Rating);
        Assert.Null(Get(c).Rating);
        Assert.Equal("Tor", Get(a).Publisher);
        Assert.Equal("Tor", Get(c).Publisher);
    }

    [Fact]
    public void Bulk_NothingStaged_IsNotUnsaved_AndUndoRestoresEverything()
    {
        int a = AddBook("A", b => b.Rating = 4f);
        var history = new MetadataEditHistoryService();
        var vm = Bulk(history);
        vm.Load(new[] { a });
        Assert.False(vm.HasUnsavedChanges());

        vm.Rating = 1;
        vm.StatusLabel = "Finished";
        Assert.True(vm.HasUnsavedChanges());
        vm.SaveCommand.Execute(null);
        Assert.Equal(1f, Get(a).Rating);

        history.Undo(PaperbunkrDb.CreateContext);

        var restored = Get(a);
        Assert.Equal(4f, restored.Rating);
        Assert.Equal(BookReadingStatus.Unread, restored.Status);
    }

    // ---- Quick edits (grid menu + Detail) ----

    private static BookQuickEditService Quick(MetadataEditHistoryService history, List<int>? asked = null) =>
        new(PaperbunkrDb.CreateContext, history, ids => { asked?.AddRange(ids); return Task.CompletedTask; });

    [Fact]
    public void QuickRate_WritesAtOnce_MarksManual_RecordsUndo_AndAsksForWriteBack()
    {
        int id = AddBook("Dune", b => b.Rating = 2f);
        var history = new MetadataEditHistoryService();
        var asked = new List<int>();

        Assert.True(Quick(history, asked).SetRating(new[] { id }, 4.4f));

        var book = Get(id);
        Assert.Equal(4.5f, book.Rating);
        Assert.Equal(BookFieldSourceKind.Manual, book.FieldSources.Single(s => s.Field == BookFields.Rating).Source);
        Assert.Equal(new[] { id }, asked);

        history.Undo(PaperbunkrDb.CreateContext);
        Assert.Equal(2f, Get(id).Rating);
    }

    [Fact]
    public void QuickRate_NoChange_DoesNothing_AndZeroClears()
    {
        int id = AddBook("Dune", b => b.Rating = 3f);
        var history = new MetadataEditHistoryService();
        var asked = new List<int>();
        var quick = Quick(history, asked);

        Assert.False(quick.SetRating(new[] { id }, 3f));
        Assert.Empty(asked);
        Assert.False(history.CanUndo);

        Assert.True(quick.SetRating(new[] { id }, 0f));
        Assert.Null(Get(id).Rating);
    }

    [Fact]
    public void QuickStatus_SetsEveryBook_InOneUndoEntry()
    {
        int a = AddBook("A");
        int c = AddBook("C", b => b.Status = BookReadingStatus.Finished);
        var history = new MetadataEditHistoryService();

        Assert.True(Quick(history).SetStatus(new[] { a, c }, BookReadingStatus.Abandoned));

        Assert.Equal(BookReadingStatus.Abandoned, Get(a).Status);
        Assert.Equal(BookReadingStatus.Abandoned, Get(c).Status);

        history.Undo(PaperbunkrDb.CreateContext);
        Assert.Equal(BookReadingStatus.Unread, Get(a).Status);
        Assert.Equal(BookReadingStatus.Finished, Get(c).Status);
    }

    [Fact]
    public void Detail_ShowsRatingStatusInfoAndTags_AndEditsWriteAtOnce()
    {
        int id = AddBook("Dune", b =>
        {
            b.Publisher = "Ace Books";
            b.LanguageISO = "en";
            b.ISBN = "9780441172719";
            b.Rating = 3.5f;
            b.Status = BookReadingStatus.WantToRead;
            b.SeriesIndex = 1f;
            b.BookSeries = new BookSeries { Name = "Dune Chronicles" };
            b.Tags.Add(new BookTag { Field = BookTagField.Genre, Value = "SF" });
            b.Tags.Add(new BookTag { Field = BookTagField.Tags, Value = "desert" });
        });
        var vm = new BookDetailScreenViewModel(() => { }, (_, _, _) => { });

        vm.LoadBook(id);

        Assert.Equal(3.5, vm.Rating);
        Assert.Equal("Want to read", vm.StatusLabel);
        Assert.Equal("Ace Books  ·  English  ·  ISBN 9780441172719  ·  #1 in Dune Chronicles", vm.InfoLine);
        Assert.Equal(new[] { "SF", "desert" }, vm.TagChips);
        Assert.True(vm.HasInfoLine);
        Assert.True(vm.HasTagChips);
        Assert.Equal(3.5f, Get(id).Rating); // loading wrote nothing

        vm.Rating = 5;
        Assert.Equal(5f, Get(id).Rating);

        vm.StatusLabel = "Finished";
        Assert.Equal(BookReadingStatus.Finished, Get(id).Status);
        Assert.True(vm.IsFinished);
    }

    [Fact]
    public void Detail_MarkAsUnread_SetsUnread_NotReading()
    {
        int id = AddBook("Dune", b => { b.Status = BookReadingStatus.Finished; b.LastChapterIndex = 3; });
        var vm = new BookDetailScreenViewModel(() => { }, (_, _, _) => { });
        vm.LoadBook(id);

        vm.ToggleFinishedCommand.Execute(null);

        var book = Get(id);
        Assert.Equal(BookReadingStatus.Unread, book.Status);
        Assert.Equal(0, book.LastChapterIndex);
    }

    [Fact]
    public void Bulk_Save_AsksForFileWriteBack_OfEveryBook()
    {
        int a = AddBook("A");
        int c = AddBook("C");
        var asked = new List<int>();
        var vm = Bulk(writeBack: ids => { asked.AddRange(ids); return Task.CompletedTask; });
        vm.Load(new[] { a, c });

        vm.Publisher = "Tor";
        vm.SaveCommand.Execute(null);

        Assert.Equal(new[] { a, c }.OrderBy(i => i), asked.OrderBy(i => i));
    }
}
