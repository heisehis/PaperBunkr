using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
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
/// §5, Phase 2; rebuilt on a WebView per docs/superpowers/specs/2026-09-02-books-reflow-reader-
/// webview-redesign-design.md) - immersive chrome, TOC navigation, font/theme controls. Reading
/// content itself is a real HTML document (<see cref="CurrentChapterHtml"/>, from the chapter's own
/// <c>Html</c>) rendered by <c>BookReaderScreen.axaml.cs</c>'s <c>NativeWebView</c> - this ViewModel
/// no longer paginates or measures text itself; pagination is chapter-granular
/// (<see cref="_position"/>, a <see cref="BookPosition"/>) with within-chapter page-turns and
/// progress handled by the WebView's own scroll (see <c>OnNextPageButtonClick</c>/
/// <c>ApplyScrollResult</c> in the code-behind). <see cref="BookPaginator"/>'s character-offset math
/// (<c>ComputeParagraphOffsets</c>/<c>FindParagraphIndex</c>) is still used here, but only for
/// bookmark excerpts and in-book search - not for rendering.
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

        // Any settings change re-runs RecomputeCurrentPage (cheap - it no longer measures text, just
        // re-derives chapter title/progress/bookmark state; the WebView's own typography CSS
        // re-injection is a separate PropertyChanged subscription in BookReaderScreen.axaml.cs).
        // Skipped entirely while LoadBook is seeding Settings from the AppSettings/Book-override
        // chain (see _isSeedingSettings) - that's not a real navigation to react to, and recomputing
        // then can hit a disposed _source (see that field's doc comment).
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

    public ObservableCollection<BookChapterSummary> TableOfContents { get; } = new();

    /// <summary>TOC drawer's grouped view of <see cref="TableOfContents"/> (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md) - rebuilt alongside it by <see cref="RebuildTocGroups"/>, never mutated directly.</summary>
    public ObservableCollection<BookTocGroup> TocGroups { get; } = new();

    /// <summary>
    /// Groups <see cref="TableOfContents"/> into consecutive runs sharing the same
    /// <see cref="BookChapterSummary.PartTitle"/> - a chapter with a null PartTitle still gets its own
    /// single-chapter group (see <see cref="BookTocGroup"/>'s own doc comment for why), always expanded
    /// (there's no header to toggle it from, so it must never end up collapsed-and-hidden). Reuses each
    /// *grouped* part's existing <see cref="BookTocGroup.IsExpanded"/> before rebuilding, keyed by its
    /// first chapter's <see cref="BookChapterSummary.Index"/> (not the part title string - two
    /// different parts could share a title, e.g. two "Appendix" sections, which would collide as a
    /// dictionary key; a chapter index never collides) - a user-collapsed group doesn't silently
    /// re-expand every time this runs (on every page/chapter change, since <see cref="TableOfContents"/>
    /// items are replaced wholesale each time to refresh their <c>IsActive</c> flag).
    /// </summary>
    private void RebuildTocGroups()
    {
        var previousExpandedByFirstChapterIndex = TocGroups
            .Where(g => g.PartTitle is not null && g.Chapters.Count > 0)
            .ToDictionary(g => g.Chapters[0].Index, g => g.IsExpanded);
        TocGroups.Clear();

        BookTocGroup? current = null;
        foreach (var chapter in TableOfContents)
        {
            bool startsNewGroup = chapter.PartTitle is null || current is null || current.PartTitle != chapter.PartTitle;
            if (startsNewGroup)
            {
                current = new BookTocGroup
                {
                    PartTitle = chapter.PartTitle,
                    IsExpanded = chapter.PartTitle is null
                        || !previousExpandedByFirstChapterIndex.TryGetValue(chapter.Index, out bool wasExpanded)
                        || wasExpanded,
                };
                TocGroups.Add(current);
            }

            current!.Chapters.Add(chapter);
        }
    }

    public ObservableCollection<BookBookmarkSummary> Bookmarks { get; } = new();

    public ObservableCollection<BookHighlightSummary> Highlights { get; } = new();

    /// <summary>Highlights belonging to whichever chapter is currently displayed - <c>BookReaderScreen.axaml.cs</c> re-renders these into the WebView (docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-redesign-design.md) after every chapter load and whenever <see cref="Highlights"/> changes.</summary>
    public IReadOnlyList<BookHighlightSummary> GetCurrentChapterHighlights() =>
        Highlights.Where(h => h.ChapterIndex == _position.ChapterIndex).ToList();

    public ObservableCollection<BookSearchResult> SearchResults { get; } = new();

    [ObservableProperty]
    private string _bookTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressLabel))]
    private string _chapterTitle = string.Empty;

    /// <summary>Current chapter's real markup (docs/superpowers/specs/2026-09-02-books-reflow-reader-
    /// webview-redesign-design.md) - <see cref="Views.BookReaderScreen.axaml.cs"/> pushes this into
    /// the <c>NativeWebView</c> via <c>NavigateToString</c> whenever it changes. Null for PDF.</summary>
    [ObservableProperty]
    private string? _currentChapterHtml;

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

    /// <summary>Anchor rect for the popup's <c>PlacementRect</c>, in <c>RootGrid</c>'s coordinate space - set by <c>BookReaderScreen.axaml.cs</c>'s <c>OnReaderWebMessageReceived</c>, which is the one place with the visual-tree access needed to translate a WebView-local rect (via <c>ReaderWebView.TranslatePoint</c>) into it.</summary>
    [ObservableProperty]
    private Rect _highlightPopupAnchorRect;

    /// <summary>A just-completed WebView text selection awaiting a color pick (docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-redesign-design.md) - null when the popup is instead editing an existing highlight (see <see cref="_editingHighlightId"/>).</summary>
    private (string BlockId, int StartOffset, int Length, string Excerpt)? _pendingHighlightSelection;
    private int? _editingHighlightId;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isCurrentPositionBookmarked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    [NotifyPropertyChangedFor(nameof(ProgressLabel))]
    private double _progressPercent;

    /// <summary>0-1 view of <see cref="ProgressPercent"/> - <c>ReaderChrome</c>'s <c>ProgressFraction</c> (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md) takes a fraction, matching the PDF reader's own <c>ProgressFraction</c> shape, while this VM's own math (chapter-start fraction, WebView scroll reporting) stayed percent-based from the WebView redesign - this is just the display-unit conversion between the two.</summary>
    public double ProgressFraction => ProgressPercent / 100.0;

    /// <summary>Bottom-bar label for <c>ReaderChrome</c> - "{Chapter title} · {percent}%".</summary>
    public string ProgressLabel => $"{ChapterTitle} · {ProgressPercent:F0}%";

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
            TableOfContents.Add(new BookChapterSummary
            {
                Index = i,
                Title = _source.Chapters[i].Title,
                IsActive = i == 0,
                PartTitle = _source.Chapters[i].PartTitle,
            });
        }

        RebuildTocGroups();

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
        // Jumps to the chapter start, not the precise highlight position - BookPosition's
        // CharacterOffset is still the pre-redesign global-flattened-text scheme (Step 8 replaces it
        // with the same BlockId-based locator BookHighlight now uses); a real known simplification
        // until that lands, not a silently-wrong value.
        _position = new BookPosition(highlight.ChapterIndex, 0);
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
    /// A WebView text selection just completed (docs/superpowers/specs/2026-09-02-books-reflow-
    /// reader-webview-redesign-design.md) - opens the color-palette popup for a brand-new highlight.
    /// <paramref name="anchorRectInRootGrid"/> is already translated to <c>RootGrid</c>'s coordinate
    /// space by the caller (<c>BookReaderScreen.axaml.cs</c>'s <c>WebMessageReceived</c> handler),
    /// the one place with the visual-tree access needed to do that translation. Single-block
    /// selections only - a selection spanning multiple <c>BlockIdInjector</c> blocks is a real,
    /// documented limitation of the JS selection-capture script, not silently mishandled here.
    /// </summary>
    public void OnWebViewSelectionCompleted(string blockId, int startOffset, int length, string excerpt, Rect anchorRectInRootGrid)
    {
        _pendingHighlightSelection = (blockId, startOffset, length, Truncate(excerpt, 140));
        _editingHighlightId = null;

        IsEditingExistingHighlight = false;
        HighlightPopupNote = string.Empty;
        HighlightPopupAnchorRect = anchorRectInRootGrid;
        IsHighlightPopupOpen = true;
    }

    /// <summary>Tapping an existing highlight span in the WebView reopens the same popup pre-filled, for edit/delete instead of a new selection.</summary>
    public void OnWebViewHighlightTapped(int highlightId, Rect anchorRectInRootGrid)
    {
        var summary = Highlights.FirstOrDefault(h => h.Id == highlightId);
        if (summary is null)
        {
            return;
        }

        _pendingHighlightSelection = null;
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
        else if (_pendingHighlightSelection is { } selection)
        {
            var entity = new BookHighlight
            {
                BookId = _bookId,
                ChapterIndex = _position.ChapterIndex,
                BlockId = selection.BlockId,
                StartOffset = selection.StartOffset,
                Length = selection.Length,
                Color = color,
                Note = string.IsNullOrWhiteSpace(HighlightPopupNote) ? null : HighlightPopupNote,
                Excerpt = selection.Excerpt,
                CreatedTime = DateTime.UtcNow,
            };
            context.BookHighlights.Add(entity);
            context.SaveChanges();

            Highlights.Insert(0, ToSummary(entity));
        }

        IsHighlightPopupOpen = false;
        _pendingHighlightSelection = null;
        _editingHighlightId = null;
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
        _pendingHighlightSelection = null;
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

    /// <summary>
    /// Advances one whole chapter (docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-
    /// redesign-design.md, Step 5) - "page" no longer means a paragraph-fitted screenful the way it
    /// did under the old <c>BookPaginator</c> pipeline, since CSS multi-column layout inside the
    /// WebView now owns within-chapter pagination entirely (<c>BookReaderScreen.axaml.cs</c>'s
    /// <c>OnNextPageButtonClick</c> scrolls the WebView directly and only falls back to this command
    /// once it reports there's no further column to scroll to). Kept as a real command (not folded
    /// entirely into code-behind) so it stays independently callable - keyboard shortcuts, tests -
    /// without needing a live WebView.
    /// </summary>
    [RelayCommand]
    private void NextPage()
    {
        if (_source is null)
        {
            return;
        }

        _history.Push(_position);

        if (_position.ChapterIndex + 1 < _source.Chapters.Count)
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

    /// <summary>Retreats one whole chapter - see <see cref="NextPage"/>'s doc comment for why this is chapter-, not column-, granularity at the ViewModel level now.</summary>
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
    /// Toggles a bookmark for the current chapter. Matches (and creates) by <c>ChapterIndex</c> alone
    /// now - a real, disclosed granularity narrowing from the WebView redesign (docs/superpowers/
    /// specs/2026-09-02-books-reflow-reader-webview-redesign-design.md): <see cref="BookPosition.CharacterOffset"/>
    /// no longer tracks a real within-chapter position (Step 5 made page-turn chapter-granular; the
    /// design's own precise block-anchored locator - the same one <c>BookHighlight</c> now uses - is
    /// a deferred follow-up for bookmarks/resume-position specifically, not yet wired here). One
    /// bookmark per chapter is an honest reflection of what's actually trackable right now, not a
    /// silently-narrower version of the old per-paragraph behavior.
    /// </summary>
    [RelayCommand]
    private void ToggleBookmark()
    {
        if (_source is null)
        {
            return;
        }

        var existing = Bookmarks.FirstOrDefault(b => b.ChapterIndex == _position.ChapterIndex);
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
            BlockId = highlight.BlockId,
            StartOffset = highlight.StartOffset,
            Length = highlight.Length,
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
        // blank cover page. If literally nothing in the book has text, stay put - CurrentChapterHtml
        // ends up whatever that chapter's (empty) markup is, which is the honest state for a book
        // with no readable content anywhere.
        //
        // Real regression found 2026-09-02, after EpubBookSource started correctly inlining SVG-
        // wrapped cover images (Dune/Ender's Game/Red Queen from a real user library, all confirmed):
        // this check only looked at Paragraphs, so a cover page whose only content is that now-working
        // image still had zero paragraphs and got skipped anyway - on every RecomputeCurrentPage call,
        // not just the initial load, so even an explicit TOC click on "Chapter 1" bounced straight back
        // off it. The image pipeline was never broken; the user just could never reach a page that had
        // one. Now only true dead weight (no text AND no *resolved* image) gets skipped - checking for
        // "data:image" rather than a bare "<img"/"<image" tag deliberately, since a chapter can carry
        // an unresolved reference to a file that was never actually embedded (EpubBookSource leaves
        // that src untouched rather than inventing a broken data URI) - that's still nothing real to
        // show, same as no image reference at all.
        bool CurrentChapterHasNothingToShow(int index)
        {
            var chapter = _source.Chapters[index];
            return chapter.Paragraphs.Count == 0
                && (chapter.Html is null || !chapter.Html.Contains("data:image", StringComparison.OrdinalIgnoreCase));
        }

        if (CurrentChapterHasNothingToShow(_position.ChapterIndex))
        {
            int firstWithContent = Enumerable.Range(0, _source.Chapters.Count)
                .FirstOrDefault(i => !CurrentChapterHasNothingToShow(i), -1);
            if (firstWithContent >= 0 && firstWithContent != _position.ChapterIndex)
            {
                _position = new BookPosition(firstWithContent, 0);
            }
        }

        var chapter = _source.Chapters[_position.ChapterIndex];

        ChapterTitle = chapter.Title;
        ReadingPositionAnnouncement = chapter.Title;
        CanGoPrevious = _history.Count > 0;
        // Books reflow reader WebView redesign (docs/superpowers/specs/2026-09-02-books-reflow-
        // reader-webview-redesign-design.md, Step 4) - drives BookReaderScreen.axaml.cs's
        // NativeWebView.NavigateToString via a PropertyChanged subscription. Null for PDF (which has
        // no Html) or a source that hasn't populated it yet.
        CurrentChapterHtml = chapter.Html;

        for (int i = 0; i < TableOfContents.Count; i++)
        {
            TableOfContents[i] = new BookChapterSummary
            {
                Index = TableOfContents[i].Index,
                Title = TableOfContents[i].Title,
                IsActive = TableOfContents[i].Index == _position.ChapterIndex,
                PartTitle = TableOfContents[i].PartTitle,
            };
        }

        RebuildTocGroups();

        // Chapter-start fraction only - within-chapter progress is the WebView's own concern now
        // (BookReaderScreen.axaml.cs's ApplyScrollResult overwrites this from real scrollTop/
        // scrollHeight once the page has actually rendered and the user starts scrolling/paging).
        ProgressPercent = _source.Chapters.Count > 0
            ? (double)_position.ChapterIndex / _source.Chapters.Count * 100
            : 0;

        // Chapter-only match - see ToggleBookmark's doc comment for why CharacterOffset no longer
        // meaningfully distinguishes positions within a chapter.
        IsCurrentPositionBookmarked = Bookmarks.Any(b => b.ChapterIndex == _position.ChapterIndex);
    }
}
