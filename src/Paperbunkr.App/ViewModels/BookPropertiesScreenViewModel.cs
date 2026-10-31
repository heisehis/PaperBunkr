using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Book Properties editor overlay (docs/superpowers/specs/2026-08-27-book-properties-editor-
/// design.md, Piece B2) - the first in-app editor for book metadata (every field is scan-derived
/// and read-only otherwise). A modal popup composited in MainWindow, same buffered
/// Load/edit/Save/Cancel shape as <see cref="ReadingListPropertiesScreenViewModel"/>, plus the
/// <see cref="MetadataEditHistoryService"/> undo/redo hook <see cref="IssuePropertiesScreenViewModel"/>
/// established.
///
/// Undo/redo covers the book row only (title/author/summary/published/series-membership); the
/// series-level Author/SortName fields edit a shared <see cref="BookSeries"/> row and cover changes
/// are a file-cache write - both outside the "this book's history" model, see the design doc.
/// </summary>
public partial class BookPropertiesScreenViewModel : ViewModelBase
{
    private readonly Action _goBack;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<string, string>? _notify;
    private readonly MetadataEditHistoryService _history;
    private readonly Func<int, Task>? _writeBack;

    private int _bookId;
    private BookFormat _format;
    private string _filePath = string.Empty;

    /// <summary>Buffered until Save - a picked image never touches disk until then, so Cancel discards it for free.</summary>
    private string? _pendingCoverImagePath;
    private bool _resetCoverRequested;

    /// <summary>The book-row values as of <see cref="Load"/> - the "before" half of the undo entry <see cref="Save"/> records.</summary>
    private Dictionary<string, string?> _beforeSnapshot = new();

    /// <summary>Field buffer as of <see cref="Load"/>, for <see cref="HasUnsavedChanges"/>.</summary>
    private string _loadSignature = string.Empty;

    public BookPropertiesScreenViewModel(Action goBack, Action<string, string>? notify = null, MetadataEditHistoryService? history = null)
        : this(goBack, PaperbunkrDb.CreateContext, notify, history, id => new BookFileWriteBackService().AutoWriteAsync(new[] { id }))
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal BookPropertiesScreenViewModel(Action goBack, Func<PaperbunkrDbContext> contextFactory, Action<string, string>? notify = null, MetadataEditHistoryService? history = null, Func<int, Task>? writeBack = null)
    {
        _writeBack = writeBack;
        _goBack = goBack;
        _contextFactory = contextFactory;
        _notify = notify;
        _history = history ?? MetadataEditHistoryService.Shared;
    }

    [ObservableProperty]
    private string _headerLabel = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _publishedDate;

    [ObservableProperty]
    private string _seriesName = string.Empty;

    public bool HasSeriesName => !string.IsNullOrWhiteSpace(SeriesName);

    partial void OnSeriesNameChanged(string value) => OnPropertyChanged(nameof(HasSeriesName));

    [ObservableProperty]
    private string _seriesAuthor = string.Empty;

    [ObservableProperty]
    private string _seriesSortName = string.Empty;

    // --- Books slice 1 fields (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §6) ---

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _publisher = string.Empty;

    [ObservableProperty]
    private string _languageIso = string.Empty;

    [ObservableProperty]
    private string _isbn = string.Empty;

    /// <summary>Position within the series, typed as text so "2.5" and blank both work. Only meaningful with a series name.</summary>
    [ObservableProperty]
    private string _seriesIndexText = string.Empty;

    /// <summary>0-5 in half steps (the <c>StarRating</c> control's value); null = unrated.</summary>
    [ObservableProperty]
    private double? _rating;

    [ObservableProperty]
    private string _statusLabel = BookStatusLabels.ToLabel(BookReadingStatus.Unread);

    /// <summary>Comma-separated, edited by a multi-value SuggestBox.</summary>
    [ObservableProperty]
    private string _genres = string.Empty;

    [ObservableProperty]
    private string _tags = string.Empty;

    public IReadOnlyList<string> StatusOptions => BookStatusLabels.All;

    [ObservableProperty]
    private BookVocabulary _vocabulary = BookVocabulary.Empty;

    /// <summary>"Dune Chronicles #2" when the title looks like it belongs to a series the book is not in yet; never applied until accepted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestedSeries))]
    private string? _suggestedSeriesText;

    private BookSeriesSuggestion? _suggestedSeries;

    public bool HasSuggestedSeries => !string.IsNullOrEmpty(SuggestedSeriesText);

    /// <summary>Where each recorded value came from, for the read-only provenance chips.</summary>
    [ObservableProperty]
    private IReadOnlyList<BookSourceChip> _sourceChips = Array.Empty<BookSourceChip>();

    public bool HasSourceChips => SourceChips.Count > 0;

    partial void OnSourceChipsChanged(IReadOnlyList<BookSourceChip> value) => OnPropertyChanged(nameof(HasSourceChips));

    /// <summary>The loaded values as text, per <see cref="BookFields"/> key - compared with the saved ones to find which fields the user changed (those become Manual).</summary>
    private Dictionary<string, string> _loadedFieldValues = new();

    /// <summary>Live preview only - the pending local pick (once chosen), the book's cached cover, or null after a Reset. Nothing is written until Save.</summary>
    [ObservableProperty]
    private Bitmap? _coverPreview;

    public void Load(int bookId)
    {
        _bookId = bookId;
        _pendingCoverImagePath = null;
        _resetCoverRequested = false;

        using var context = _contextFactory();
        var book = context.Books.Include(b => b.BookSeries).Include(b => b.Tags).Include(b => b.FieldSources).FirstOrDefault(b => b.Id == bookId);
        if (book is null)
        {
            _goBack();
            return;
        }

        _format = book.Format;
        _filePath = book.FilePath;

        HeaderLabel = $"Edit “{book.Title}”";
        Title = book.Title;
        Author = book.Author ?? string.Empty;
        Summary = book.Summary ?? string.Empty;
        PublishedDate = book.PublishedDate is { } pub ? new DateTimeOffset(DateTime.SpecifyKind(pub, DateTimeKind.Utc)) : null;
        SeriesName = book.BookSeries?.Name ?? string.Empty;
        SeriesAuthor = book.BookSeries?.Author ?? string.Empty;
        SeriesSortName = book.BookSeries?.SortName ?? string.Empty;

        Subtitle = book.Subtitle ?? string.Empty;
        Publisher = book.Publisher ?? string.Empty;
        LanguageIso = book.LanguageISO ?? string.Empty;
        Isbn = book.ISBN ?? string.Empty;
        SeriesIndexText = book.SeriesIndex?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
        Rating = book.Rating;
        StatusLabel = BookStatusLabels.ToLabel(book.Status);
        Genres = BookTagText.For(book, BookTagField.Genre);
        Tags = BookTagText.For(book, BookTagField.Tags);

        Vocabulary = BookVocabulary.Load(context);
        _suggestedSeries = book.BookSeriesId is null ? BookSeriesSuggester.Suggest(book.Title, Vocabulary.SeriesNames) : null;
        SuggestedSeriesText = _suggestedSeries is { } s ? (s.Index is float i ? $"{s.SeriesName} #{i.ToString("0.###", CultureInfo.InvariantCulture)}" : s.SeriesName) : null;
        SourceChips = book.FieldSources.OrderBy(f => f.Field).Select(f => new BookSourceChip(FieldLabel(f.Field), OriginLabel(f.Source))).ToList();

        CoverPreview = BookCoverImageCache.Get(bookId, book.FilePath);

        _beforeSnapshot = BookMetadataSnapshot.Capture(book);
        _loadedFieldValues = FieldValues();
        _loadSignature = BufferSignature();
    }

    public bool HasUnsavedChanges() =>
        _pendingCoverImagePath is not null || _resetCoverRequested || BufferSignature() != _loadSignature;

    private string BufferSignature() => string.Join(
        '␟',
        Title, Author, Summary,
        PublishedDate?.UtcDateTime.ToString("O") ?? string.Empty,
        SeriesName, SeriesAuthor, SeriesSortName,
        Subtitle, Publisher, LanguageIso, Isbn, SeriesIndexText,
        Rating?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, StatusLabel, Genres, Tags);

    /// <summary>The descriptive fields as comparable text, keyed by <see cref="BookFields"/>.</summary>
    private Dictionary<string, string> FieldValues() => new()
    {
        [BookFields.Subtitle] = Subtitle.Trim(),
        [BookFields.Publisher] = Publisher.Trim(),
        [BookFields.Language] = LanguageIso.Trim(),
        [BookFields.Isbn] = Isbn.Trim(),
        [BookFields.Summary] = Summary.Trim(),
        [BookFields.Series] = SeriesName.Trim(),
        [BookFields.SeriesIndex] = SeriesIndexText.Trim(),
        [BookFields.Rating] = Rating?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        [BookFields.Genre] = BookTagText.Join(BookTagText.Split(Genres)),
        [BookFields.Tags] = BookTagText.Join(BookTagText.Split(Tags)),
    };

    private static string FieldLabel(string field) => field switch
    {
        BookFields.Language => "Language",
        BookFields.Series => "Series",
        BookFields.SeriesIndex => "Series #",
        BookFields.Isbn => "ISBN",
        _ => field,
    };

    private static string OriginLabel(BookFieldSourceKind source) => source switch
    {
        BookFieldSourceKind.File => "file",
        BookFieldSourceKind.OpenLibrary => "Open Library",
        BookFieldSourceKind.GoogleBooks => "Google Books",
        BookFieldSourceKind.Calibre => "Calibre",
        BookFieldSourceKind.Opds => "OPDS catalog",
        _ => "you",
    };

    [RelayCommand]
    private void AcceptSuggestedSeries()
    {
        if (_suggestedSeries is not { } suggestion)
        {
            return;
        }

        SeriesName = suggestion.SeriesName;
        if (suggestion.Index is float index)
        {
            SeriesIndexText = index.ToString("0.###", CultureInfo.InvariantCulture);
        }

        _suggestedSeries = null;
        SuggestedSeriesText = null;
    }

    [RelayCommand]
    private void DismissSuggestedSeries()
    {
        _suggestedSeries = null;
        SuggestedSeriesText = null;
    }

    [RelayCommand]
    private async Task ChangeCoverAsync()
    {
        string? path = await new FilePickerService().PickImageFileAsync("Choose Cover Image");
        if (path is null)
        {
            return;
        }

        try
        {
            _pendingCoverImagePath = path;
            _resetCoverRequested = false;
            CoverPreview = new Bitmap(path);
        }
        catch
        {
            _pendingCoverImagePath = null;
        }
    }

    [RelayCommand]
    private void ResetCover()
    {
        _pendingCoverImagePath = null;
        _resetCoverRequested = true;
        CoverPreview = null;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            _notify?.Invoke("Can't save", "Title can't be empty.");
            return;
        }

        float? seriesIndex = null;
        if (!string.IsNullOrWhiteSpace(SeriesIndexText))
        {
            seriesIndex = BookMetadataParsing.ParseIndex(SeriesIndexText);
            if (seriesIndex is null)
            {
                _notify?.Invoke("Can't save", "Series number must be a number such as 2 or 2.5.");
                return;
            }
        }

        if (!BookStatusLabels.TryParse(StatusLabel, out var status))
        {
            _notify?.Invoke("Can't save", $"“{StatusLabel}” isn't a reading status.");
            return;
        }

        using var context = _contextFactory();
        var book = context.Books.Include(b => b.BookSeries).Include(b => b.Tags).Include(b => b.FieldSources).FirstOrDefault(b => b.Id == _bookId);
        if (book is null)
        {
            _goBack();
            return;
        }

        int? previousSeriesId = book.BookSeriesId;

        book.Title = Title.Trim();
        book.Author = NullIfEmpty(Author);
        book.Summary = NullIfEmpty(Summary);
        book.PublishedDate = PublishedDate?.UtcDateTime;
        book.Subtitle = NullIfEmpty(Subtitle);
        book.Publisher = NullIfEmpty(Publisher);
        book.LanguageISO = NullIfEmpty(LanguageIso);
        book.ISBN = NullIfEmpty(Isbn);
        book.Rating = BookRating.Normalize((float?)Rating);
        book.Status = status;
        BookTagText.Set(book, BookTagField.Genre, BookTagText.Split(Genres));
        BookTagText.Set(book, BookTagField.Tags, BookTagText.Split(Tags));

        ResolveSeries(context, book);
        book.SeriesIndex = book.BookSeries is null ? null : seriesIndex;

        // Whatever the user changed is theirs from now on: no file refresh or provider match will overwrite it.
        var after = FieldValues();
        BookMetadataApplier.MarkManual(book, after.Where(kv => _loadedFieldValues.GetValueOrDefault(kv.Key) != kv.Value).Select(kv => kv.Key), DateTime.UtcNow);

        context.SaveChanges();

        _history.RecordBookEdit($"Edited “{book.Title}”", _bookId, _beforeSnapshot, BookMetadataSnapshot.Capture(book));
        if (_writeBack is not null)
        {
            _ = _writeBack(_bookId);
        }

        // Detaching or reassigning may have emptied the old series
        // (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-design.md, component d).
        if (previousSeriesId != book.BookSeriesId)
        {
            BookSeriesMaintenance.PruneIfEmpty(context, previousSeriesId);
        }

        ApplyCover(book.FilePath, book.Format);

        _goBack();
    }

    /// <summary>
    /// Series membership + shared-row fields (docs/superpowers/specs/2026-08-27-book-properties-
    /// editor-design.md): blank name detaches to standalone; a name matching an existing
    /// <see cref="BookSeries"/> (case-insensitive) reuses it; an unmatched name creates one. The name
    /// box never renames the book's current series. <see cref="SeriesAuthor"/>/<see cref="SeriesSortName"/>
    /// are written onto whichever row the book ends up in (so they affect its siblings too).
    /// </summary>
    private void ResolveSeries(PaperbunkrDbContext context, Book book)
    {
        string name = SeriesName.Trim();
        if (name.Length == 0)
        {
            book.BookSeriesId = null;
            book.BookSeries = null;
            return;
        }

        // Match in memory (case-insensitive) - same "load once, compare with OrdinalIgnoreCase"
        // approach BookFolderScanService.ScanAll uses for its seriesByName lookup.
        var series = context.BookSeries.ToList()
            .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        if (series is null)
        {
            series = new BookSeries { Name = name };
            context.BookSeries.Add(series);
        }

        series.Author = NullIfEmpty(SeriesAuthor);
        series.SortName = NullIfEmpty(SeriesSortName);

        book.BookSeries = series;
    }

    private void ApplyCover(string filePath, BookFormat format)
    {
        var service = new BookCoverThumbnailService();
        if (_pendingCoverImagePath is { } pending)
        {
            service.TrySetCustomCover(_bookId, pending);
        }
        else if (_resetCoverRequested)
        {
            service.ResetCover(_bookId, filePath, format);
        }
    }

    [RelayCommand]
    private void Cancel() => _goBack();

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One read-only "Publisher · file" provenance chip in Book Properties.</summary>
public sealed record BookSourceChip(string Field, string Origin)
{
    public string Text => $"{Field} · {Origin}";
}
