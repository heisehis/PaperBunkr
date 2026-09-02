using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Book Details screen (docs/superpowers/specs/2026-08-27-book-details-screen-design.md, Piece B1) -
/// sits between the Books grid and the reader. One VM, two <see cref="BookDetailMode"/>s: a single
/// book (cover, metadata, reading progress, summary, chapter list, bookmarks) and a book series
/// (name/author + the grid of its books). Mirrors <see cref="DetailScreenViewModel"/>'s
/// series/single-issue split rather than being two screens.
///
/// Metadata editing is Piece B2 (a separate spec) - the "Edit" button is present but disabled here.
/// Opens a fresh <see cref="PaperbunkrDbContext"/> per load with no caching, same as the comic
/// Detail screens (single-item, low-volume). For an EPUB it parses the file once in
/// <see cref="LoadBook"/> to fill the chapter list and resolve bookmark chapter titles, then
/// disposes the source immediately - only row snapshots are kept, never a live handle.
/// </summary>
public partial class BookDetailScreenViewModel : ViewModelBase, IDetailHeaderSource, IContextMenuProvider
{
    /// <summary>Right-click/Menu-key menus for the bookmark list and series-mode book grid
    /// (docs/superpowers/specs/2026-08-31-keyboard-operability-design.md) - delegated out, same
    /// pattern as <see cref="LibraryScreenViewModel"/>'s own <see cref="IContextMenuProvider"/>
    /// implementation.</summary>
    IReadOnlyList<ContextMenuEntry>? IContextMenuProvider.BuildContextMenu(object? target) =>
        new BookDetailContextMenuBuilder(this).Build(target);

    private const int SynopsisCollapseThreshold = 280;

    private readonly Action _goBooks;
    private readonly Action<int, BookFormat, BookPosition?> _goReaderForBook;
    private readonly Action<int> _goEditBook;
    private readonly Action<IReadOnlyList<int>> _goBulkEdit;
    private readonly Action<int> _goEditSeries;

    private int _bookId;
    private int? _bookSeriesId;
    private BookFormat _format;

    /// <summary>Set when Book mode was reached via the "Part of {series}" link, so the back link
    /// returns to that series' Series mode instead of the Books grid. Cleared by every other
    /// <see cref="LoadBook"/> entry.</summary>
    private int? _cameFromSeriesId;

    public BookDetailScreenViewModel(Action goBooks, Action<int, BookFormat, BookPosition?> goReaderForBook,
        Action<int>? goEditBook = null, Action<IReadOnlyList<int>>? goBulkEdit = null, Action<int>? goEditSeries = null)
    {
        _goBooks = goBooks;
        _goReaderForBook = goReaderForBook;
        _goEditBook = goEditBook ?? (_ => { });
        _goBulkEdit = goBulkEdit ?? (_ => { });
        _goEditSeries = goEditSeries ?? (_ => { });
        CoverBrush = SeriesCardSample.CoverBrushFor(string.Empty);
        Band = new DetailBandViewModel();
    }

    /// <summary>"Lite" band - inline meta + synopsis only, no metadata groups (books carry none).</summary>
    public DetailBandViewModel Band { get; }

    // --- IDetailHeaderSource ---

    [ObservableProperty]
    private Bitmap? _backdropImage;

    [ObservableProperty]
    private string _metaLine = string.Empty;

    public string HeaderTitle => IsSeriesMode ? SeriesName : Title;
    string? IDetailHeaderSource.SecondaryTitle => null;
    DetailHeroProgress? IDetailHeaderSource.TrackerProgress => null;

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(HeaderTitle));
    partial void OnSeriesNameChanged(string value) => OnPropertyChanged(nameof(HeaderTitle));

    public IReadOnlyList<DetailHeroAction> Actions => IsSeriesMode
        ? new[]
        {
            new DetailHeroAction("Edit series", EditSeriesCommand),
            new DetailHeroAction("Edit all books", EditAllSeriesBooksCommand),
        }
        : new[]
        {
            new DetailHeroAction(ContinueLabel, ContinueCommand, IsPrimary: true),
            new DetailHeroAction("Edit", EditCommand),
            new DetailHeroAction("Reveal in Explorer", RevealInExplorerCommand),
            new DetailHeroAction("Export Annotations", ExportAnnotationsCommand),
        };

    private void RaiseHeaderChanged()
    {
        OnPropertyChanged(nameof(Actions));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(MetaLine));
    }

    public ObservableCollection<BookChapterSummary> Chapters { get; } = new();

    public ObservableCollection<BookBookmarkSummary> Bookmarks { get; } = new();

    public ObservableCollection<BookCardSample> SeriesBooks { get; } = new();

    // --- mode ---

    [ObservableProperty]
    private BookDetailMode _mode = BookDetailMode.Book;

    public bool IsBookMode => Mode == BookDetailMode.Book;
    public bool IsSeriesMode => Mode == BookDetailMode.Series;

    partial void OnModeChanged(BookDetailMode value)
    {
        OnPropertyChanged(nameof(IsBookMode));
        OnPropertyChanged(nameof(IsSeriesMode));
        RaiseHeaderChanged();
    }

    // --- book mode: header / meta ---

    [ObservableProperty]
    private Bitmap? _coverImage;

    [ObservableProperty]
    private IBrush _coverBrush;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);

    partial void OnAuthorChanged(string value) => OnPropertyChanged(nameof(HasAuthor));

    [ObservableProperty]
    private string _formatBadge = string.Empty;

    [ObservableProperty]
    private string _seriesLinkLabel = string.Empty;

    [ObservableProperty]
    private bool _hasSeries;

    [ObservableProperty]
    private string _publishedLabel = string.Empty;

    [ObservableProperty]
    private bool _hasPublished;

    [ObservableProperty]
    private string _addedLabel = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _backLabel = "← Books";

    // --- book mode: actions / progress ---

    [ObservableProperty]
    private string _continueLabel = "Start reading";

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private string _progressLabel = string.Empty;

    [ObservableProperty]
    private bool _hasChapterProgress;

    [ObservableProperty]
    private string _lastOpenedLabel = string.Empty;

    [ObservableProperty]
    private bool _isFinished;

    public string FinishedToggleLabel => IsFinished ? "Mark as unread" : "Mark as finished";

    partial void OnIsFinishedChanged(bool value) => OnPropertyChanged(nameof(FinishedToggleLabel));

    // --- book mode: summary ---

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _isSynopsisExpanded;

    [ObservableProperty]
    private bool _isSynopsisToggleVisible;

    public string SynopsisToggleLabel => IsSynopsisExpanded ? "Show less ▲" : "Show more ▼";

    partial void OnIsSynopsisExpandedChanged(bool value) => OnPropertyChanged(nameof(SynopsisToggleLabel));

    // --- book mode: chapters / bookmarks ---

    [ObservableProperty]
    private bool _hasChapters;

    [ObservableProperty]
    private bool _chaptersUnavailable;

    [ObservableProperty]
    private bool _hasBookmarks;

    // --- series mode ---

    [ObservableProperty]
    private string _seriesName = string.Empty;

    [ObservableProperty]
    private string _seriesAuthor = string.Empty;

    [ObservableProperty]
    private bool _hasSeriesAuthor;

    [ObservableProperty]
    private string _seriesBookCountLabel = string.Empty;

    // --- loading ---

    /// <summary>Loads a single book into Book mode. <paramref name="fromSeriesId"/> is set only by
    /// the internal "Part of series" / series-card paths so the back link can return there.</summary>
    public void LoadBook(int bookId, int? fromSeriesId = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books
            .Include(b => b.BookSeries)
            .Include(b => b.Bookmarks)
            .FirstOrDefault(b => b.Id == bookId);
        if (book is null)
        {
            _goBooks();
            return;
        }

        _bookId = bookId;
        _bookSeriesId = book.BookSeriesId;
        _format = book.Format;
        _cameFromSeriesId = fromSeriesId;

        Title = book.Title;
        Author = book.Author ?? string.Empty;
        FormatBadge = book.Format switch
        {
            BookFormat.Epub => "EPUB",
            BookFormat.Fb2 => "FB2",
            BookFormat.Mobi => "MOBI", // AZW3 shares this format tag - see BookFormat.Mobi's own doc comment.
            _ => "PDF",
        };
        CoverBrush = SeriesCardSample.CoverBrushFor(book.Title);
        CoverImage = BookCoverImageCache.Get(bookId);
        BackdropImage = CoverImage is not null ? BackdropBlurRenderer.Render(CoverImage, new PixelSize(1600, 680)) : null;

        HasSeries = book.BookSeries is not null;
        SeriesLinkLabel = book.BookSeries is null ? string.Empty : $"Part of {book.BookSeries.Name} ▸";

        HasPublished = book.PublishedDate is not null;
        PublishedLabel = book.PublishedDate is { } pub ? $"Published {pub:MMM d, yyyy}" : string.Empty;
        AddedLabel = $"Added {book.AddedTime:MMM d, yyyy}";
        FilePath = book.FilePath;

        IsFinished = book.Finished;
        bool started = book.LastOpenedTime is not null;
        LastOpenedLabel = book.LastOpenedTime is { } opened
            ? $"Last opened {opened.ToLocalTime():MMM d, yyyy}"
            : "Not started";

        HasChapterProgress = book.Format != BookFormat.Pdf && book.ChapterCount > 0;
        if (HasChapterProgress)
        {
            ProgressFraction = Math.Clamp((double)book.LastChapterIndex / Math.Max(1, book.ChapterCount - 1), 0, 1);
            ProgressLabel = $"Chapter {book.LastChapterIndex + 1} of {book.ChapterCount}";
        }
        else
        {
            ProgressFraction = 0;
            ProgressLabel = string.Empty;
        }

        ContinueLabel = book.Finished
            ? "Read again"
            : !started
                ? "Start reading"
                : HasChapterProgress
                    ? $"Continue — Chapter {book.LastChapterIndex + 1}"
                    : "Continue reading";

        Summary = string.IsNullOrWhiteSpace(book.Summary) ? "No summary available." : book.Summary!;
        IsSynopsisExpanded = false;
        IsSynopsisToggleVisible = !string.IsNullOrWhiteSpace(book.Summary) && book.Summary!.Length > SynopsisCollapseThreshold;

        MetaLine = string.Join("  ·  ", new[]
        {
            Author,
            FormatBadge,
            book.Finished ? "FINISHED" : string.Empty,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        Band.Summary = Summary;
        Band.IsSynopsisExpanded = false;
        Band.StatusText = FormatBadge;
        Band.PublisherText = Author;
        Band.YearText = book.PublishedDate is { } pd ? pd.Year.ToString() : string.Empty;
        RaiseHeaderChanged();

        BackLabel = _cameFromSeriesId is not null && book.BookSeries is not null
            ? $"← {book.BookSeries.Name}"
            : "← Books";

        LoadChaptersAndBookmarks(book);

        Mode = BookDetailMode.Book;
    }

    /// <summary>Reflowable formats only (EPUB/FB2/MOBI): parse the file once for the TOC + bookmark
    /// chapter titles, then dispose. PDF has neither concept (both sections stay hidden). A parse
    /// failure leaves the chapter list empty with <see cref="ChaptersUnavailable"/> set; bookmarks
    /// still render off their stored excerpts with a "Chapter N" title fallback.</summary>
    private void LoadChaptersAndBookmarks(Book book)
    {
        Chapters.Clear();
        Bookmarks.Clear();
        ChaptersUnavailable = false;

        string[] chapterTitles = Array.Empty<string>();

        if (book.Format != BookFormat.Pdf)
        {
            try
            {
                using var source = BookTextSourceFactory.Create(book.Format, book.FilePath);
                chapterTitles = source.Chapters.Select(c => c.Title).ToArray();

                if (chapterTitles.Length > 1)
                {
                    for (int i = 0; i < chapterTitles.Length; i++)
                    {
                        Chapters.Add(new BookChapterSummary
                        {
                            Index = i,
                            Title = string.IsNullOrWhiteSpace(chapterTitles[i]) ? $"Chapter {i + 1}" : chapterTitles[i],
                            IsActive = i == book.LastChapterIndex,
                        });
                    }
                }
            }
            catch
            {
                ChaptersUnavailable = true;
            }
        }

        foreach (var bookmark in book.Bookmarks.OrderByDescending(b => b.CreatedTime))
        {
            string chapterTitle = bookmark.ChapterIndex >= 0 && bookmark.ChapterIndex < chapterTitles.Length
                && !string.IsNullOrWhiteSpace(chapterTitles[bookmark.ChapterIndex])
                    ? chapterTitles[bookmark.ChapterIndex]
                    : $"Chapter {bookmark.ChapterIndex + 1}";

            Bookmarks.Add(new BookBookmarkSummary
            {
                Id = bookmark.Id,
                ChapterIndex = bookmark.ChapterIndex,
                CharacterOffset = bookmark.CharacterOffset,
                ChapterTitle = chapterTitle,
                Excerpt = bookmark.Excerpt,
                CreatedTime = bookmark.CreatedTime,
            });
        }

        HasChapters = Chapters.Count > 0;
        HasBookmarks = Bookmarks.Count > 0;
    }

    /// <summary>Loads a book series into Series mode.</summary>
    public void LoadSeries(int bookSeriesId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.BookSeries
            .Include(s => s.Books)
            .FirstOrDefault(s => s.Id == bookSeriesId);
        if (series is null)
        {
            _goBooks();
            return;
        }

        _bookSeriesId = bookSeriesId;

        SeriesName = series.Name;
        SeriesAuthor = series.Author ?? string.Empty;
        HasSeriesAuthor = !string.IsNullOrWhiteSpace(series.Author);
        SeriesBookCountLabel = series.Books.Count == 1 ? "1 book" : $"{series.Books.Count} books";

        var representative = series.Books.OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        CoverBrush = SeriesCardSample.CoverBrushFor(series.Name);
        CoverImage = representative is not null ? BookCoverImageCache.Get(representative.Id) : null;
        BackdropImage = CoverImage is not null ? BackdropBlurRenderer.Render(CoverImage, new PixelSize(1600, 680)) : null;
        MetaLine = string.Join("  ·  ", new[] { SeriesAuthor, SeriesBookCountLabel }.Where(s => !string.IsNullOrWhiteSpace(s)));
        Band.Summary = string.Empty;

        // EF relationship fixup already back-populates each book.BookSeries from the Include above,
        // so BookCardSample.FromBook's series line resolves without a second query.
        SeriesBooks.Clear();
        foreach (var book in series.Books.OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase))
        {
            SeriesBooks.Add(BookCardSample.FromBook(book));
        }

        BackLabel = "← Books";
        Mode = BookDetailMode.Series;
    }

    /// <summary>Re-runs <see cref="LoadBook"/> for the current book - used after a write (mark
    /// finished/unread, bookmark delete) and when the reader hands control back, so the screen
    /// reflects changes without a navigation round-trip.</summary>
    public void ReloadCurrentBook()
    {
        if (_bookId != 0)
        {
            LoadBook(_bookId, _cameFromSeriesId);
        }
    }

    /// <summary>Reloads whichever mode is active - Book or Series. Used when an overlay that could
    /// have edited either (bulk editor, series editor) closes over this screen.</summary>
    public void ReloadCurrent()
    {
        if (Mode == BookDetailMode.Series && _bookSeriesId is int seriesId)
        {
            LoadSeries(seriesId);
        }
        else
        {
            ReloadCurrentBook();
        }
    }

    // --- commands ---

    [RelayCommand]
    private void GoBack()
    {
        if (_cameFromSeriesId is int seriesId)
        {
            LoadSeries(seriesId);
        }
        else
        {
            _goBooks();
        }
    }

    [RelayCommand]
    private void Continue() => _goReaderForBook(_bookId, _format, null);

    [RelayCommand]
    private void OpenChapter(BookChapterSummary? chapter)
    {
        if (chapter is not null)
        {
            _goReaderForBook(_bookId, _format, new BookPosition(chapter.Index, 0));
        }
    }

    [RelayCommand]
    private void OpenBookmark(BookBookmarkSummary? bookmark)
    {
        if (bookmark is not null)
        {
            _goReaderForBook(_bookId, _format, new BookPosition(bookmark.ChapterIndex, bookmark.CharacterOffset));
        }
    }

    [RelayCommand]
    private void DeleteBookmark(BookBookmarkSummary? bookmark)
    {
        if (bookmark is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var entity = context.BookBookmarks.FirstOrDefault(b => b.Id == bookmark.Id);
        if (entity is not null)
        {
            context.BookBookmarks.Remove(entity);
            context.SaveChanges();
        }

        Bookmarks.Remove(bookmark);
        HasBookmarks = Bookmarks.Count > 0;
    }

    /// <summary>"Part of {series} ▸" link - switches to Series mode; the back link from any book
    /// opened out of it returns here.</summary>
    [RelayCommand]
    private void OpenSeriesFromLink()
    {
        if (_bookSeriesId is int seriesId)
        {
            LoadSeries(seriesId);
        }
    }

    [RelayCommand]
    private void OpenBookFromSeries(BookCardSample? card)
    {
        if (card is not null)
        {
            LoadBook(card.BookId, _bookSeriesId);
        }
    }

    /// <summary>Series-mode card context menu's "Edit…" - opens the Book Properties overlay for that
    /// book (docs/superpowers/specs/2026-08-27-book-properties-editor-design.md).</summary>
    [RelayCommand]
    private void EditBookInSeries(BookCardSample? card)
    {
        if (card is not null)
        {
            _goEditBook(card.BookId);
        }
    }

    /// <summary>Series-mode "Edit all N books" - opens the bulk editor over the series' books
    /// (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-design.md).</summary>
    [RelayCommand]
    private void EditAllSeriesBooks()
    {
        var ids = SeriesBooks.Select(c => c.BookId).ToList();
        if (ids.Count > 0)
        {
            _goBulkEdit(ids);
        }
    }

    /// <summary>Series-mode "Edit series" - opens the BookSeries properties overlay.</summary>
    [RelayCommand]
    private void EditSeries()
    {
        if (_bookSeriesId is int id)
        {
            _goEditSeries(id);
        }
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.Find(_bookId);
        if (book is not null)
        {
            RevealInExplorerHelper.RevealBook(book);
        }
    }

    /// <summary>
    /// Per-book export (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-
    /// design.md §"Export"). The OS save dialog's own "Save as type" combo is the "format dropdown"
    /// the design calls for - <see cref="FilePickerService.PickSaveFileWithFormatAsync"/> offers all
    /// three, and the returned path's own extension picks which <see cref="AnnotationExportService"/>
    /// method runs. <see cref="FilePickerService"/> constructed fresh here rather than injected - this
    /// VM has no DI container wiring for it, same "no DI container" precedent
    /// <see cref="FilePickerService.PickImageFileAsync"/>'s own doc comment already established.
    /// </summary>
    [RelayCommand]
    private async Task ExportAnnotations()
    {
        var filePicker = new FilePickerService();
        string? path = await filePicker.PickSaveFileWithFormatAsync(
            "Export Annotations", $"{Title}.md",
            ("md", "Markdown"), ("csv", "CSV"), ("json", "JSON"));
        if (path is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".csv":
                AnnotationExportService.ExportCsv(context, _bookId, path);
                break;
            case ".json":
                AnnotationExportService.ExportJson(context, _bookId, path);
                break;
            default:
                AnnotationExportService.ExportMarkdown(context, _bookId, path);
                break;
        }
    }

    [RelayCommand]
    private void ToggleSynopsis() => IsSynopsisExpanded = !IsSynopsisExpanded;

    /// <summary>Mark as finished / unread. "Unread" also resets the resume position - a
    /// "mark as unread" that left you a third of the way in would be a lie. <c>LastOpenedTime</c>
    /// is kept (it's a history fact, not a progress fact).</summary>
    [RelayCommand]
    private void ToggleFinished()
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.Find(_bookId);
        if (book is null)
        {
            return;
        }

        if (book.Finished)
        {
            book.Finished = false;
            book.LastChapterIndex = 0;
            book.LastCharacterOffset = 0;
        }
        else
        {
            book.Finished = true;
        }

        context.SaveChanges();
        ReloadCurrentBook();
    }

    [RelayCommand]
    private void DeleteBook()
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.Find(_bookId);
        if (book is not null)
        {
            int? seriesId = book.BookSeriesId;
            RecycleBinHelper.SendToRecycleBin(book.FilePath);
            context.Books.Remove(book);
            context.SaveChanges();
            BookCoverImageCache.Invalidate(_bookId);
            BookSeriesMaintenance.PruneIfEmpty(context, seriesId);
        }

        _goBooks();
    }

    /// <summary>Book metadata editor - Piece B2 (docs/superpowers/specs/2026-08-27-book-details-
    /// screen-design.md). Wired to a no-op until that ships; the button is disabled in XAML.</summary>
    [RelayCommand]
    private void Edit() => _goEditBook(_bookId);
}
