using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Novels reflowable reader (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md
/// §5, Phase 2) - immersive chrome, TOC navigation, font/theme controls. Pagination is computed at
/// render time via <see cref="BookPaginator"/> against the current viewport size and
/// <see cref="Settings"/>, not stored - <see cref="_position"/> (a <see cref="BookPosition"/>,
/// paragraph-boundary character offset) is what survives a resize or font-size change, not a page
/// number.
///
/// Deliberate v1 simplification, called out rather than silently dropped: paragraph bold/italic
/// spans (<see cref="BookParagraph.Spans"/>, already correctly parsed and tested in Phase 1) are
/// not yet rendered - <see cref="CurrentPageParagraphs"/> shows plain text only. Wiring real
/// <c>TextBlock.Inlines</c> per span is a rendering follow-up, not a data gap.
///
/// "Previous page" is a navigation-history stack, not true backward pagination - reverses whatever
/// this session's own forward navigation did. Simpler and more robust than exact backward-fill
/// measurement, at the cost of "Previous" being a no-op before any "Next"/chapter jump has
/// happened yet (matches how a lot of readers already behave for in-session "back").
///
/// Phase 3 (design spec §6/§7): resume position, bookmarks, in-book search. Resume/bookmarks are
/// persisted via <see cref="Book.LastChapterIndex"/>/<see cref="Book.LastCharacterOffset"/>/
/// <see cref="BookBookmark"/> - same "open a fresh context, write, SaveChanges" shape
/// <c>ReaderScreenViewModel.GoToPage</c> already uses for <c>Issue.LastPageRead</c>. Position is only
/// persisted on an actual navigation (chapter/page/bookmark/search jump), not from
/// <see cref="RecomputeCurrentPage"/> itself, since that also runs on every font/theme change and a
/// slider drag would otherwise fire a DB write per tick. Search is a linear scan over the
/// already-parsed in-memory chapters (design spec §7) - no persistent index.
/// </summary>
public partial class BookReaderScreenViewModel : ViewModelBase
{
    private readonly Action _goBack;

    private int _bookId;
    private IBookTextSource? _source;
    private Book? _book;
    private BookPosition _position;
    private Size _viewportSize;
    private readonly Stack<BookPosition> _history = new();

    public BookReaderScreenViewModel(Action goBack)
    {
        _goBack = goBack;

        // Font size/family/line spacing all feed MeasureParagraphHeight - a change means the
        // current page's paragraph range may no longer be correct for the new layout. Theme
        // doesn't affect measurement but recomputing on it too is harmless and simpler than
        // filtering which property changed.
        Settings.PropertyChanged += (_, _) => RecomputeCurrentPage();
    }

    public BookReaderSettings Settings { get; } = new();

    public ObservableCollection<BookParagraph> CurrentPageParagraphs { get; } = new();

    public ObservableCollection<BookChapterSummary> TableOfContents { get; } = new();

    public ObservableCollection<BookBookmarkSummary> Bookmarks { get; } = new();

    public ObservableCollection<BookSearchResult> SearchResults { get; } = new();

    [ObservableProperty]
    private string _bookTitle = string.Empty;

    [ObservableProperty]
    private string _chapterTitle = string.Empty;

    [ObservableProperty]
    private bool _isChromeVisible;

    [ObservableProperty]
    private bool _isTocOpen;

    [ObservableProperty]
    private bool _isFontSheetOpen;

    [ObservableProperty]
    private bool _isBookmarksOpen;

    [ObservableProperty]
    private bool _isSearchOpen;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isCurrentPositionBookmarked;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _canGoPrevious;

    partial void OnSearchQueryChanged(string value) => RunSearch(value);

    public void LoadBook(int bookId)
    {
        _source?.Dispose();
        _history.Clear();
        SearchResults.Clear();
        SearchQuery = string.Empty;

        _bookId = bookId;

        using var context = PaperbunkrDb.CreateContext();
        _book = context.Books.Include(b => b.Bookmarks).Single(b => b.Id == bookId);

        _source = _book.Format == BookFormat.Epub
            ? new EpubBookSource(_book.FilePath)
            : new PdfBookSource(_book.FilePath);

        BookTitle = _book.Title;

        TableOfContents.Clear();
        for (int i = 0; i < _source.Chapters.Count; i++)
        {
            TableOfContents.Add(new BookChapterSummary { Index = i, Title = _source.Chapters[i].Title, IsActive = i == 0 });
        }

        Bookmarks.Clear();
        foreach (var bookmark in _book.Bookmarks.OrderByDescending(b => b.CreatedTime))
        {
            Bookmarks.Add(ToSummary(bookmark));
        }

        // Resume position (design spec §6): clamp the stored chapter index in case the book
        // changed on disk since it was last saved - FindParagraphIndex already clamps a stale
        // CharacterOffset safely, so only ChapterIndex needs guarding here.
        int chapterIndex = Math.Clamp(_book.LastChapterIndex, 0, Math.Max(0, _source.Chapters.Count - 1));
        _position = new BookPosition(chapterIndex, _book.LastCharacterOffset);

        _book.LastOpenedTime = DateTime.UtcNow;
        // Lazy-populate ChapterCount (feeds Home's progress bar) + un-finish a re-read
        // (docs/superpowers/specs/2026-08-27-books-screen-chrome-and-home-strip-design.md).
        _book.ChapterCount = _source.Chapters.Count;
        _book.Finished = false;
        context.SaveChanges();

        IsChromeVisible = false;
        IsTocOpen = false;
        IsFontSheetOpen = false;
        IsBookmarksOpen = false;
        IsSearchOpen = false;
        RecomputeCurrentPage();
    }

    public void UpdateViewportSize(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0 || size == _viewportSize)
        {
            return;
        }

        _viewportSize = size;
        RecomputeCurrentPage();
    }

    [RelayCommand]
    private void ToggleChrome()
    {
        if (IsTocOpen || IsFontSheetOpen || IsBookmarksOpen || IsSearchOpen)
        {
            CloseAllOverlays();
            return;
        }

        IsChromeVisible = !IsChromeVisible;
    }

    private void CloseAllOverlays()
    {
        IsTocOpen = false;
        IsFontSheetOpen = false;
        IsBookmarksOpen = false;
        IsSearchOpen = false;
    }

    [RelayCommand]
    private void OpenToc()
    {
        CloseAllOverlays();
        IsTocOpen = true;
    }

    [RelayCommand]
    private void CloseToc() => IsTocOpen = false;

    [RelayCommand]
    private void OpenFontSheet()
    {
        CloseAllOverlays();
        IsFontSheetOpen = true;
    }

    [RelayCommand]
    private void CloseFontSheet() => IsFontSheetOpen = false;

    [RelayCommand]
    private void OpenBookmarks()
    {
        CloseAllOverlays();
        IsBookmarksOpen = true;
    }

    [RelayCommand]
    private void CloseBookmarks() => IsBookmarksOpen = false;

    [RelayCommand]
    private void OpenSearch()
    {
        CloseAllOverlays();
        IsSearchOpen = true;
    }

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOpen = false;
        SearchQuery = string.Empty;
    }

    [RelayCommand]
    private void GoToChapter(BookChapterSummary? chapter)
    {
        if (chapter is null || _source is null)
        {
            return;
        }

        _history.Push(_position);
        _position = new BookPosition(chapter.Index, 0);
        IsTocOpen = false;
        RecomputeCurrentPage();
        PersistPosition();
    }

    [RelayCommand]
    private void NextPage()
    {
        if (_source is null)
        {
            return;
        }

        var paragraphs = _source.Chapters[_position.ChapterIndex].Paragraphs;
        var (_, endExclusive) = CurrentPageRange(paragraphs);

        _history.Push(_position);

        if (endExclusive < paragraphs.Count)
        {
            _position = new BookPosition(_position.ChapterIndex, BookPaginator.ComputeParagraphOffsets(paragraphs)[endExclusive]);
        }
        else if (_position.ChapterIndex + 1 < _source.Chapters.Count)
        {
            _position = new BookPosition(_position.ChapterIndex + 1, 0);
        }
        else
        {
            // Already at the end of the book - nothing to move to, don't record a no-op history
            // entry, but do mark it finished so it leaves Home's Continue Reading — Books row.
            _history.Pop();
            PersistPosition(markFinished: true);
            return;
        }

        RecomputeCurrentPage();
        PersistPosition();
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (_history.Count == 0)
        {
            return;
        }

        _position = _history.Pop();
        RecomputeCurrentPage();
        PersistPosition();
    }

    [RelayCommand]
    private void Close() => _goBack();

    [RelayCommand]
    private void SetFontFamily(BookFontFamilyOption option) => Settings.FontFamilyOption = option;

    [RelayCommand]
    private void SetLineSpacing(BookLineSpacingOption option) => Settings.LineSpacing = option;

    [RelayCommand]
    private void SetTheme(BookTheme theme) => Settings.Theme = theme;

    /// <summary>
    /// Toggles a bookmark at the paragraph the current page starts on. Matches by
    /// (ChapterIndex, CharacterOffset) - both are paragraph-boundary values (design spec §5), so
    /// this is a stable identity across font/theme changes and window resizes, same as resume
    /// position.
    /// </summary>
    [RelayCommand]
    private void ToggleBookmark()
    {
        if (_source is null)
        {
            return;
        }

        var existing = Bookmarks.FirstOrDefault(b => b.ChapterIndex == _position.ChapterIndex && b.CharacterOffset == _position.CharacterOffset);
        if (existing is not null)
        {
            DeleteBookmark(existing);
            return;
        }

        var chapter = _source.Chapters[_position.ChapterIndex];
        int paragraphIndex = BookPaginator.FindParagraphIndex(chapter.Paragraphs, _position.CharacterOffset);
        string excerpt = paragraphIndex < chapter.Paragraphs.Count ? Truncate(chapter.Paragraphs[paragraphIndex].Text, 140) : string.Empty;

        using var context = PaperbunkrDb.CreateContext();
        var bookmark = new BookBookmark
        {
            BookId = _bookId,
            ChapterIndex = _position.ChapterIndex,
            CharacterOffset = _position.CharacterOffset,
            Excerpt = excerpt,
            CreatedTime = DateTime.UtcNow,
        };
        context.BookBookmarks.Add(bookmark);
        context.SaveChanges();

        Bookmarks.Insert(0, ToSummary(bookmark, chapter.Title));
        IsCurrentPositionBookmarked = true;
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
        if (bookmark.ChapterIndex == _position.ChapterIndex && bookmark.CharacterOffset == _position.CharacterOffset)
        {
            IsCurrentPositionBookmarked = false;
        }
    }

    [RelayCommand]
    private void GoToBookmark(BookBookmarkSummary? bookmark)
    {
        if (bookmark is null || _source is null)
        {
            return;
        }

        _history.Push(_position);
        _position = new BookPosition(bookmark.ChapterIndex, bookmark.CharacterOffset);
        IsBookmarksOpen = false;
        RecomputeCurrentPage();
        PersistPosition();
    }

    [RelayCommand]
    private void GoToSearchResult(BookSearchResult? result)
    {
        if (result is null || _source is null)
        {
            return;
        }

        _history.Push(_position);
        _position = new BookPosition(result.ChapterIndex, result.CharacterOffset);
        IsSearchOpen = false;
        SearchQuery = string.Empty;
        RecomputeCurrentPage();
        PersistPosition();
    }

    /// <summary>
    /// Linear substring scan over the already-parsed chapters (design spec §7) - no persistent
    /// index. Capped so a common word in a long novel doesn't produce thousands of rows.
    /// </summary>
    private void RunSearch(string query)
    {
        SearchResults.Clear();

        if (_source is not null && query.Trim().Length >= 2)
        {
            const int maxResults = 200;
            for (int chapterIndex = 0; chapterIndex < _source.Chapters.Count && SearchResults.Count < maxResults; chapterIndex++)
            {
                var chapter = _source.Chapters[chapterIndex];
                int[] offsets = BookPaginator.ComputeParagraphOffsets(chapter.Paragraphs);

                for (int p = 0; p < chapter.Paragraphs.Count && SearchResults.Count < maxResults; p++)
                {
                    string text = chapter.Paragraphs[p].Text;
                    int matchIndex = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (matchIndex < 0)
                    {
                        continue;
                    }

                    SearchResults.Add(new BookSearchResult
                    {
                        ChapterIndex = chapterIndex,
                        CharacterOffset = offsets[p],
                        ChapterTitle = chapter.Title,
                        Excerpt = BuildSearchExcerpt(text, matchIndex, query.Length),
                    });
                }
            }
        }

        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    /// <summary>Drives the "No matches" empty state - only true once the user has actually typed something searchable.</summary>
    public bool HasNoSearchResults => SearchQuery.Trim().Length >= 2 && SearchResults.Count == 0;

    private static string BuildSearchExcerpt(string text, int matchIndex, int matchLength)
    {
        const int context = 40;
        int start = Math.Max(0, matchIndex - context);
        int end = Math.Min(text.Length, matchIndex + matchLength + context);

        string excerpt = text[start..end];
        if (start > 0) excerpt = "…" + excerpt;
        if (end < text.Length) excerpt += "…";
        return excerpt;
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "…";

    private BookBookmarkSummary ToSummary(BookBookmark bookmark)
    {
        string chapterTitle = _source is not null && bookmark.ChapterIndex < _source.Chapters.Count
            ? _source.Chapters[bookmark.ChapterIndex].Title
            : string.Empty;
        return ToSummary(bookmark, chapterTitle);
    }

    private static BookBookmarkSummary ToSummary(BookBookmark bookmark, string chapterTitle) => new()
    {
        Id = bookmark.Id,
        ChapterIndex = bookmark.ChapterIndex,
        CharacterOffset = bookmark.CharacterOffset,
        ChapterTitle = chapterTitle,
        Excerpt = bookmark.Excerpt,
    };

    /// <summary>
    /// Reopens a fresh context per write, same shape as <c>ReaderScreenViewModel.GoToPage</c>'s
    /// <c>Issue.LastPageRead</c> persistence - called from explicit navigation only (chapter/page/
    /// bookmark/search jumps), never from <see cref="RecomputeCurrentPage"/> itself, so a font-size
    /// slider drag doesn't fire a DB write per tick.
    /// </summary>
    private void PersistPosition(bool markFinished = false)
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.FirstOrDefault(b => b.Id == _bookId);
        if (book is null)
        {
            return;
        }

        book.LastChapterIndex = _position.ChapterIndex;
        book.LastCharacterOffset = _position.CharacterOffset;
        book.LastOpenedTime = DateTime.UtcNow;
        if (markFinished)
        {
            book.Finished = true;
        }

        context.SaveChanges();
    }

    private (int Start, int EndExclusive) CurrentPageRange(IReadOnlyList<BookParagraph> paragraphs)
    {
        int startIndex = BookPaginator.FindParagraphIndex(paragraphs, _position.CharacterOffset);
        double availableHeight = Math.Max(0, _viewportSize.Height - 120); // rough allowance for top/bottom chrome margins
        double paragraphSpacing = Settings.FontSize * 0.8;

        return BookPaginator.FillPage(paragraphs, startIndex, availableHeight, paragraphSpacing, MeasureParagraphHeight);
    }

    private void RecomputeCurrentPage()
    {
        if (_source is null || _viewportSize.Width <= 0 || _viewportSize.Height <= 0)
        {
            return;
        }

        // Real EPUBs commonly lead with a cover/title-page spine item that has no prose at all
        // (confirmed against a real file: its own <guide> metadata tags that exact spine entry as
        // "cover") - landing there and stopping left the reader permanently blank. Skip forward to
        // the first chapter that actually has paragraphs, same as most e-readers already do for a
        // blank cover page. If literally nothing in the book has text, stay put - CurrentPageParagraphs
        // ends up empty, which is the honest state for a book with no readable content anywhere.
        if (_source.Chapters[_position.ChapterIndex].Paragraphs.Count == 0)
        {
            int firstWithContent = Enumerable.Range(0, _source.Chapters.Count)
                .FirstOrDefault(i => _source.Chapters[i].Paragraphs.Count > 0, -1);
            if (firstWithContent >= 0 && firstWithContent != _position.ChapterIndex)
            {
                _position = new BookPosition(firstWithContent, 0);
            }
        }

        var chapter = _source.Chapters[_position.ChapterIndex];
        var (start, endExclusive) = CurrentPageRange(chapter.Paragraphs);

        CurrentPageParagraphs.Clear();
        for (int i = start; i < endExclusive; i++)
        {
            CurrentPageParagraphs.Add(chapter.Paragraphs[i]);
        }

        ChapterTitle = chapter.Title;
        CanGoPrevious = _history.Count > 0;

        for (int i = 0; i < TableOfContents.Count; i++)
        {
            TableOfContents[i] = new BookChapterSummary
            {
                Index = TableOfContents[i].Index,
                Title = TableOfContents[i].Title,
                IsActive = TableOfContents[i].Index == _position.ChapterIndex,
            };
        }

        double chapterFraction = chapter.Paragraphs.Count > 0 ? (double)start / chapter.Paragraphs.Count : 0;
        ProgressPercent = _source.Chapters.Count > 0
            ? (_position.ChapterIndex + chapterFraction) / _source.Chapters.Count * 100
            : 0;

        IsCurrentPositionBookmarked = Bookmarks.Any(b => b.ChapterIndex == _position.ChapterIndex && b.CharacterOffset == _position.CharacterOffset);
    }

    /// <summary>
    /// Real Avalonia text-height measurement - the one part of pagination that genuinely needs the
    /// UI platform, injected into <see cref="BookPaginator.FillPage"/> as a delegate so the
    /// paragraph-fitting algorithm itself stays testable with a fake measurer.
    /// </summary>
    private double MeasureParagraphHeight(BookParagraph paragraph)
    {
        var typeface = new Typeface(Settings.ResolvedFontFamily);
        var layout = new TextLayout(
            paragraph.Text,
            typeface,
            Settings.FontSize,
            Settings.Foreground,
            textWrapping: TextWrapping.Wrap,
            maxWidth: Math.Max(1, _viewportSize.Width - 80),
            lineHeight: Settings.FontSize * Settings.LineHeightMultiplier);

        return layout.Height;
    }
}
