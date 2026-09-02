using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Books;
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

    // Chrome auto-hide (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-
    // design.md §"Chrome auto-hide") - AutoHideChromeToggle (below) mirrors the global
    // AppSettings.BookReaderAutoHideChrome column directly; not a per-book override, unlike
    // Settings' own font/spacing/theme values.
    private const double AutoHideDelaySeconds = 2.5;
    private readonly DispatcherTimer _autoHideTimer;

    /// <summary>
    /// True only while <see cref="LoadBook"/>'s settings-resolution block is assigning
    /// <see cref="Settings"/> properties from the AppSettings/Book-override chain. Real bug found
    /// after this shipped: each of those 8 assignments fires the constructor's own
    /// <c>Settings.PropertyChanged -&gt; RecomputeCurrentPage</c> subscription below, and on any
    /// LoadBook call after the very first one in this VM's lifetime, <c>_source</c> is non-null but
    /// already <em>disposed</em> (<c>_source?.Dispose()</c> at the top of <see cref="LoadBook"/> never
    /// nulled the field) while <c>_viewportSize</c> is already valid (this app's screens are
    /// constructed and attached eagerly at startup) - so <see cref="RecomputeCurrentPage"/>'s
    /// <c>_source is null</c> guard doesn't actually stop it, and it goes on to read a disposed
    /// <see cref="IBookTextSource"/> from inside a property-changed handler, which fails silently
    /// (no crash dialog, just a permanently blank reading pane - confirmed via manual testing).
    /// </summary>
    private bool _isSeedingSettings;

    public BookReaderScreenViewModel(Action goBack)
    {
        _goBack = goBack;

        // Font size/family/line spacing all feed MeasureParagraphHeight - a change means the
        // current page's paragraph range may no longer be correct for the new layout. Theme
        // doesn't affect measurement but recomputing on it too is harmless and simpler than
        // filtering which property changed. Skipped entirely while LoadBook is seeding Settings from
        // the AppSettings/Book-override chain (see _isSeedingSettings) - that's not a real page-range
        // change to react to, and recomputing then can hit a disposed _source (see that field's doc
        // comment).
        Settings.PropertyChanged += (_, _) =>
        {
            if (!_isSeedingSettings)
            {
                RecomputeCurrentPage();
            }
        };

        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoHideDelaySeconds) };
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            if (!IsAnyDrawerOpen)
            {
                IsChromeVisible = false;
            }
        };
    }

    /// <summary>Chrome (and any auto-hide countdown) must stay put while a drawer/sheet is open - same set the pre-existing <see cref="ToggleChrome"/>/<see cref="CloseAllOverlays"/> logic already checks, extended here as a shared property rather than duplicating the condition a third time.</summary>
    public bool IsAnyDrawerOpen => IsTocOpen || IsFontSheetOpen || IsBookmarksOpen || IsHighlightsOpen || IsSearchOpen;

    /// <summary>
    /// Called from <c>BookReaderScreen.axaml.cs</c> on every pointer move over the reading canvas.
    /// <paramref name="nearTopEdge"/> is the one thing that can *reveal* already-hidden chrome (per
    /// the design spec - general pointer movement elsewhere only resets the hide countdown while
    /// chrome is already visible, matching the design's "reappears on pointer-move-to-top-edge or any
    /// key press" wording rather than any movement anywhere).
    /// </summary>
    public void NotifyPointerActivity(bool nearTopEdge)
    {
        if (!AutoHideChromeToggle || IsAnyDrawerOpen)
        {
            return;
        }

        if (!IsChromeVisible)
        {
            if (nearTopEdge)
            {
                IsChromeVisible = true;
                RestartAutoHideTimer();
            }

            return;
        }

        RestartAutoHideTimer();
    }

    /// <summary>Any key press also reveals hidden chrome, same as <see cref="NotifyPointerActivity"/>'s near-top-edge case - called from <c>BookReaderScreen.axaml.cs</c>'s existing key handling.</summary>
    public void NotifyKeyActivity()
    {
        if (!AutoHideChromeToggle || IsAnyDrawerOpen || IsChromeVisible)
        {
            return;
        }

        IsChromeVisible = true;
        RestartAutoHideTimer();
    }

    private void RestartAutoHideTimer()
    {
        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    public BookReaderSettings Settings { get; } = new();

    public ObservableCollection<BookParagraphDisplay> CurrentPageParagraphs { get; } = new();

    public ObservableCollection<BookChapterSummary> TableOfContents { get; } = new();

    public ObservableCollection<BookBookmarkSummary> Bookmarks { get; } = new();

    public ObservableCollection<BookHighlightSummary> Highlights { get; } = new();

    public ObservableCollection<BookSearchResult> SearchResults { get; } = new();

    [ObservableProperty]
    private string _bookTitle = string.Empty;

    [ObservableProperty]
    private string _chapterTitle = string.Empty;

    [ObservableProperty]
    private bool _isChromeVisible;

    /// <summary>Font-sheet-bound mirror of <see cref="AppSettings.BookReaderAutoHideChrome"/> - global, not per-book, unlike <see cref="Settings"/>' font/spacing/theme values, so this writes straight through to <see cref="AppSettings"/> on every toggle rather than batching via <see cref="PersistSettingsOverride"/>.</summary>
    [ObservableProperty]
    private bool _autoHideChromeToggle = true;

    [ObservableProperty]
    private bool _isTocOpen;

    [ObservableProperty]
    private bool _isFontSheetOpen;

    [ObservableProperty]
    private bool _isBookmarksOpen;

    [ObservableProperty]
    private bool _isHighlightsOpen;

    [ObservableProperty]
    private bool _isSearchOpen;

    // --- Highlight color/note popup (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-
    // annotations-design.md §"Highlight selection UX") - kept directly on this view model rather than
    // a separate HighlightPopupViewModel, same pattern this class already uses for the font sheet's
    // own transient state (IsFontSheetOpen etc.) instead of a child view model. ---

    [ObservableProperty]
    private bool _isHighlightPopupOpen;

    /// <summary>True when the popup is editing an existing highlight (shows Delete); false for a brand-new selection (color pick only).</summary>
    [ObservableProperty]
    private bool _isEditingExistingHighlight;

    [ObservableProperty]
    private string _highlightPopupNote = string.Empty;

    /// <summary>Anchor rect for the popup's <c>PlacementRect</c>, in <c>RootGrid</c>'s coordinate space - set by <c>BookReaderScreen.axaml.cs</c>'s event handler, which is the one place with the visual-tree access needed to translate a <see cref="Views.ParagraphView"/>-local rect into it.</summary>
    [ObservableProperty]
    private Rect _highlightPopupAnchorRect;

    private BookParagraphDisplay? _pendingHighlightParagraph;
    private int _pendingHighlightLocalStart;
    private int _pendingHighlightLocalEnd;
    private int? _editingHighlightId;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isCurrentPositionBookmarked;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _canGoPrevious;

    /// <summary>
    /// Screen-reader live-region text (docs/superpowers/specs/2026-09-01-books-reader-screen-reader-
    /// accessibility-design.md) - bound in <c>BookReaderScreen.axaml</c> to a hidden
    /// <c>AutomationProperties.LiveSetting="Polite"</c> TextBlock. Updated on every page-turn/chapter
    /// change by <see cref="RecomputeCurrentPage"/> (a short chapter-title announcement) and on
    /// <c>Ctrl+Shift+W</c> by <see cref="AnnounceReadingPosition"/> (the fuller "Chapter N of M"
    /// heading trail) - both write the same property rather than two separate live regions, per that
    /// spec's decision.
    /// </summary>
    [ObservableProperty]
    private string _readingPositionAnnouncement = string.Empty;

    partial void OnSearchQueryChanged(string value) => RunSearch(value);

    partial void OnAutoHideChromeToggleChanged(bool value)
    {
        if (!value)
        {
            _autoHideTimer.Stop();
        }

        using var context = PaperbunkrDb.CreateContext();
        var appSettings = context.GetOrCreateAppSettings();
        appSettings.BookReaderAutoHideChrome = value;
        context.SaveChanges();
    }

    /// <param name="startAt">When non-null, the reader opens here (a chapter jump or a bookmark
    /// from the Book Details screen - docs/superpowers/specs/2026-08-27-book-details-screen-design.md)
    /// instead of resuming from <see cref="Book.LastChapterIndex"/>/<see cref="Book.LastCharacterOffset"/>.
    /// Nothing new is persisted until the reader itself navigates.</param>
    public void LoadBook(int bookId, BookPosition? startAt = null)
    {
        // Reused-instance edge case: if the font sheet is still open from a previously-loaded book
        // in this same reader (e.g. a chapter jump into the next book without formally closing the
        // sheet first), persist that book's pending override before _bookId gets reassigned below -
        // otherwise PersistSettingsOverride would silently write the old book's Settings values onto
        // the new book's row.
        if (IsFontSheetOpen && _bookId != 0)
        {
            PersistSettingsOverride();
        }

        _source?.Dispose();
        // Nulled explicitly, not left pointing at the disposed instance - RecomputeCurrentPage's own
        // "_source is null" guard depends on this being genuinely null, not just stale (see
        // _isSeedingSettings' doc comment for the real bug this fixes).
        _source = null;
        _history.Clear();
        SearchResults.Clear();
        SearchQuery = string.Empty;

        _bookId = bookId;

        using var context = PaperbunkrDb.CreateContext();
        _book = context.Books.Include(b => b.Bookmarks).Include(b => b.Highlights).Single(b => b.Id == bookId);

        // Reader ergonomics resolution chain (docs/superpowers/specs/2026-09-01-books-reader-
        // ergonomics-and-annotations-design.md §"Per-book override") - same null-coalescing fallback
        // shape as Issue.PageFitModeOverride ?? AppSettings.DefaultPageFitMode elsewhere in this
        // codebase. _isSeedingSettings suppresses RecomputeCurrentPage for these 8 assignments - see
        // that field's doc comment for the real (silently-blank-reader) bug this guards against.
        _isSeedingSettings = true;
        var appSettings = context.GetOrCreateAppSettings();
        Settings.FontSize = _book.FontSizeOverride ?? appSettings.BookReaderFontSize;
        Settings.FontFamilyOption = _book.FontFamilyOverride ?? appSettings.BookReaderFontFamily;
        Settings.LineSpacing = _book.LineSpacingOverride ?? appSettings.BookReaderLineSpacing;
        Settings.CharacterSpacing = _book.CharacterSpacingOverride ?? appSettings.BookReaderCharacterSpacing;
        Settings.WordSpacing = _book.WordSpacingOverride ?? appSettings.BookReaderWordSpacing;
        Settings.ParagraphSpacing = _book.ParagraphSpacingOverride ?? appSettings.BookReaderParagraphSpacing;
        Settings.PageMargin = _book.PageMarginOverride ?? appSettings.BookReaderPageMargin;
        Settings.Theme = _book.ThemeOverride ?? appSettings.BookReaderTheme;
        _isSeedingSettings = false;
        // Via the property, not the backing field directly, so the font sheet's bound checkbox
        // picks up the loaded value - OnAutoHideChromeToggleChanged firing here re-saves the same
        // value it just read, a harmless redundant write traded for not needing a manual
        // OnPropertyChanged call to keep the UI in sync.
        AutoHideChromeToggle = appSettings.BookReaderAutoHideChrome;
        _autoHideTimer.Stop();

        _source = BookTextSourceFactory.Create(_book.Format, _book.FilePath);

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

        Highlights.Clear();
        foreach (var highlight in _book.Highlights.OrderByDescending(h => h.CreatedTime))
        {
            Highlights.Add(ToSummary(highlight));
        }

        // Resume position (design spec §6): clamp the stored chapter index in case the book
        // changed on disk since it was last saved - FindParagraphIndex already clamps a stale
        // CharacterOffset safely, so only ChapterIndex needs guarding here.
        if (startAt is { } start)
        {
            int clamped = Math.Clamp(start.ChapterIndex, 0, Math.Max(0, _source.Chapters.Count - 1));
            _position = new BookPosition(clamped, start.CharacterOffset);
        }
        else
        {
            int chapterIndex = Math.Clamp(_book.LastChapterIndex, 0, Math.Max(0, _source.Chapters.Count - 1));
            _position = new BookPosition(chapterIndex, _book.LastCharacterOffset);
        }

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
        IsHighlightsOpen = false;
        IsSearchOpen = false;
        IsHighlightPopupOpen = false;
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
        if (IsAnyDrawerOpen)
        {
            CloseAllOverlays();
            return;
        }

        IsChromeVisible = !IsChromeVisible;
        if (IsChromeVisible && AutoHideChromeToggle)
        {
            RestartAutoHideTimer();
        }
        else
        {
            _autoHideTimer.Stop();
        }
    }

    private void CloseAllOverlays()
    {
        if (IsFontSheetOpen)
        {
            PersistSettingsOverride();
        }

        IsTocOpen = false;
        IsFontSheetOpen = false;
        IsBookmarksOpen = false;
        IsHighlightsOpen = false;
        IsSearchOpen = false;
        IsHighlightPopupOpen = false;
    }

    [RelayCommand]
    private void OpenToc()
    {
        CloseAllOverlays();
        IsTocOpen = true;
    }

    [RelayCommand]
    private void CloseToc() => IsTocOpen = false;

    /// <summary>
    /// "Where am I?" heading trail (docs/superpowers/specs/2026-09-01-books-reader-screen-reader-
    /// accessibility-design.md), bound to <c>Ctrl+Shift+W</c> in <c>BookReaderScreen.axaml</c>. Pure
    /// announcement, no visual UI - writes the same live-region property
    /// <see cref="RecomputeCurrentPage"/> already updates on every page-turn/chapter-change.
    /// </summary>
    [RelayCommand]
    private void AnnounceReadingPosition()
    {
        if (_source is null)
        {
            return;
        }

        var chapter = _source.Chapters[_position.ChapterIndex];
        ReadingPositionAnnouncement = $"Chapter {_position.ChapterIndex + 1} of {_source.Chapters.Count}: {chapter.Title}";
    }

    [RelayCommand]
    private void OpenFontSheet()
    {
        CloseAllOverlays();
        IsFontSheetOpen = true;
    }

    [RelayCommand]
    private void CloseFontSheet()
    {
        PersistSettingsOverride();
        IsFontSheetOpen = false;
    }

    /// <summary>Mirrors <c>BookDetailScreenViewModel.ExportAnnotations</c> - same command, same rationale for constructing <see cref="FilePickerService"/> fresh rather than injecting it.</summary>
    [RelayCommand]
    private async Task ExportAnnotations()
    {
        var filePicker = new FilePickerService();
        string? path = await filePicker.PickSaveFileWithFormatAsync(
            "Export Annotations", $"{BookTitle}.md",
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

    /// <summary>
    /// Writes the current in-memory <see cref="Settings"/> values back to <see cref="_book"/>'s
    /// override columns - called when the font sheet closes (not from <c>Settings.PropertyChanged</c>
    /// directly), same "explicit action, not every intermediate tick" rationale
    /// <see cref="PersistPosition"/> already uses elsewhere in this class - a slider drag while the
    /// sheet is open would otherwise fire a DB write per tick.
    /// </summary>
    private void PersistSettingsOverride()
    {
        if (_bookId == 0)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.FirstOrDefault(b => b.Id == _bookId);
        if (book is null)
        {
            return;
        }

        book.FontSizeOverride = Settings.FontSize;
        book.FontFamilyOverride = Settings.FontFamilyOption;
        book.LineSpacingOverride = Settings.LineSpacing;
        book.CharacterSpacingOverride = Settings.CharacterSpacing;
        book.WordSpacingOverride = Settings.WordSpacing;
        book.ParagraphSpacingOverride = Settings.ParagraphSpacing;
        book.PageMarginOverride = Settings.PageMargin;
        book.ThemeOverride = Settings.Theme;
        context.SaveChanges();
    }

    [RelayCommand]
    private void OpenBookmarks()
    {
        CloseAllOverlays();
        IsBookmarksOpen = true;
    }

    [RelayCommand]
    private void CloseBookmarks() => IsBookmarksOpen = false;

    [RelayCommand]
    private void OpenHighlights()
    {
        CloseAllOverlays();
        IsHighlightsOpen = true;
    }

    [RelayCommand]
    private void CloseHighlights() => IsHighlightsOpen = false;

    [RelayCommand]
    private void GoToHighlight(BookHighlightSummary? highlight)
    {
        if (highlight is null || _source is null)
        {
            return;
        }

        _history.Push(_position);
        _position = new BookPosition(highlight.ChapterIndex, highlight.StartOffset);
        IsHighlightsOpen = false;
        RecomputeCurrentPage();
        PersistPosition();
    }

    [RelayCommand]
    private void DeleteHighlight(BookHighlightSummary? highlight)
    {
        if (highlight is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var entity = context.BookHighlights.FirstOrDefault(h => h.Id == highlight.Id);
        if (entity is not null)
        {
            context.BookHighlights.Remove(entity);
            context.SaveChanges();
        }

        Highlights.Remove(highlight);
        RecomputeCurrentPage();
    }

    /// <summary>
    /// A drag-selection just completed inside one displayed paragraph's <see cref="Views.ParagraphView"/>
    /// (design spec §"Highlight selection UX") - opens the color-palette popup for a brand-new
    /// highlight. <paramref name="anchorRectInRootGrid"/> is already translated to <c>RootGrid</c>'s
    /// coordinate space by the caller (<c>BookReaderScreen.axaml.cs</c>), the one place with the
    /// visual-tree access needed to do that translation.
    /// </summary>
    public void OnParagraphSelectionCompleted(BookParagraphDisplay paragraph, int localStart, int localEnd, Rect anchorRectInRootGrid)
    {
        _pendingHighlightParagraph = paragraph;
        _pendingHighlightLocalStart = localStart;
        _pendingHighlightLocalEnd = localEnd;
        _editingHighlightId = null;

        IsEditingExistingHighlight = false;
        HighlightPopupNote = string.Empty;
        HighlightPopupAnchorRect = anchorRectInRootGrid;
        IsHighlightPopupOpen = true;
    }

    /// <summary>Tapping an existing highlight (no drag) reopens the same popup pre-filled, for edit/delete instead of a new selection.</summary>
    public void OnParagraphHighlightTapped(ParagraphHighlight highlight, Rect anchorRectInRootGrid)
    {
        var summary = Highlights.FirstOrDefault(h => h.ChapterIndex == _position.ChapterIndex
            && h.StartOffset <= highlight.Start && h.EndOffset >= highlight.End);
        if (summary is null)
        {
            return;
        }

        _pendingHighlightParagraph = null;
        _editingHighlightId = summary.Id;

        IsEditingExistingHighlight = true;
        HighlightPopupNote = summary.Note ?? string.Empty;
        HighlightPopupAnchorRect = anchorRectInRootGrid;
        IsHighlightPopupOpen = true;
    }

    [RelayCommand]
    private void PickHighlightColor(BookHighlightColor color)
    {
        using var context = PaperbunkrDb.CreateContext();

        if (_editingHighlightId is { } id)
        {
            var entity = context.BookHighlights.FirstOrDefault(h => h.Id == id);
            if (entity is not null)
            {
                entity.Color = color;
                entity.Note = string.IsNullOrWhiteSpace(HighlightPopupNote) ? null : HighlightPopupNote;
                context.SaveChanges();

                int index = Highlights.ToList().FindIndex(h => h.Id == id);
                if (index >= 0)
                {
                    Highlights[index] = ToSummary(entity);
                }
            }
        }
        else if (_pendingHighlightParagraph is { } paragraph)
        {
            int globalStart = paragraph.GlobalOffset + _pendingHighlightLocalStart;
            int globalEnd = paragraph.GlobalOffset + _pendingHighlightLocalEnd;
            string excerpt = Truncate(paragraph.Paragraph.Text[_pendingHighlightLocalStart.._pendingHighlightLocalEnd], 140);

            var entity = new BookHighlight
            {
                BookId = _bookId,
                ChapterIndex = _position.ChapterIndex,
                StartOffset = globalStart,
                EndOffset = globalEnd,
                Color = color,
                Note = string.IsNullOrWhiteSpace(HighlightPopupNote) ? null : HighlightPopupNote,
                Excerpt = excerpt,
                CreatedTime = DateTime.UtcNow,
            };
            context.BookHighlights.Add(entity);
            context.SaveChanges();

            Highlights.Insert(0, ToSummary(entity));
        }

        IsHighlightPopupOpen = false;
        _pendingHighlightParagraph = null;
        _editingHighlightId = null;
        RecomputeCurrentPage();
    }

    [RelayCommand]
    private void DeleteHighlightFromPopup()
    {
        if (_editingHighlightId is { } id)
        {
            var summary = Highlights.FirstOrDefault(h => h.Id == id);
            if (summary is not null)
            {
                DeleteHighlight(summary);
            }
        }

        IsHighlightPopupOpen = false;
        _editingHighlightId = null;
    }

    [RelayCommand]
    private void CancelHighlightPopup()
    {
        IsHighlightPopupOpen = false;
        _pendingHighlightParagraph = null;
        _editingHighlightId = null;
    }

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
        CreatedTime = bookmark.CreatedTime,
    };

    private BookHighlightSummary ToSummary(BookHighlight highlight)
    {
        string chapterTitle = _source is not null && highlight.ChapterIndex < _source.Chapters.Count
            ? _source.Chapters[highlight.ChapterIndex].Title
            : string.Empty;
        return new BookHighlightSummary
        {
            Id = highlight.Id,
            ChapterIndex = highlight.ChapterIndex,
            StartOffset = highlight.StartOffset,
            EndOffset = highlight.EndOffset,
            Color = highlight.Color,
            Note = highlight.Note,
            ChapterTitle = chapterTitle,
            Excerpt = highlight.Excerpt,
            CreatedTime = highlight.CreatedTime,
        };
    }

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
        int[] paragraphOffsets = BookPaginator.ComputeParagraphOffsets(chapter.Paragraphs);

        CurrentPageParagraphs.Clear();
        for (int i = start; i < endExclusive; i++)
        {
            var paragraph = chapter.Paragraphs[i];
            int globalOffset = paragraphOffsets[i];
            int globalEnd = globalOffset + paragraph.Text.Length;

            var localHighlights = Highlights
                .Where(h => h.ChapterIndex == _position.ChapterIndex && h.StartOffset < globalEnd && h.EndOffset > globalOffset)
                .Select(h => new ParagraphHighlight(
                    Math.Max(0, h.StartOffset - globalOffset),
                    Math.Min(paragraph.Text.Length, h.EndOffset - globalOffset),
                    h.Color))
                .ToList();

            CurrentPageParagraphs.Add(new BookParagraphDisplay
            {
                Paragraph = paragraph,
                GlobalOffset = globalOffset,
                Highlights = localHighlights,
            });
        }

        ChapterTitle = chapter.Title;
        ReadingPositionAnnouncement = chapter.Title;
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
