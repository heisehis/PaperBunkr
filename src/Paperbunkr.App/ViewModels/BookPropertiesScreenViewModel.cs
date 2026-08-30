using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
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
        : this(goBack, PaperbunkrDb.CreateContext, notify, history)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal BookPropertiesScreenViewModel(Action goBack, Func<PaperbunkrDbContext> contextFactory, Action<string, string>? notify = null, MetadataEditHistoryService? history = null)
    {
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

    /// <summary>Live preview only - the pending local pick (once chosen), the book's cached cover, or null after a Reset. Nothing is written until Save.</summary>
    [ObservableProperty]
    private Bitmap? _coverPreview;

    public void Load(int bookId)
    {
        _bookId = bookId;
        _pendingCoverImagePath = null;
        _resetCoverRequested = false;

        using var context = _contextFactory();
        var book = context.Books.Include(b => b.BookSeries).FirstOrDefault(b => b.Id == bookId);
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

        CoverPreview = BookCoverImageCache.Get(bookId, book.FilePath);

        _beforeSnapshot = BookMetadataSnapshot.Capture(book);
        _loadSignature = BufferSignature();
    }

    public bool HasUnsavedChanges() =>
        _pendingCoverImagePath is not null || _resetCoverRequested || BufferSignature() != _loadSignature;

    private string BufferSignature() => string.Join(
        '␟',
        Title, Author, Summary,
        PublishedDate?.UtcDateTime.ToString("O") ?? string.Empty,
        SeriesName, SeriesAuthor, SeriesSortName);

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

        using var context = _contextFactory();
        var book = context.Books.Include(b => b.BookSeries).FirstOrDefault(b => b.Id == _bookId);
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

        ResolveSeries(context, book);

        context.SaveChanges();

        _history.RecordBookEdit($"Edited “{book.Title}”", _bookId, _beforeSnapshot, BookMetadataSnapshot.Capture(book));

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
