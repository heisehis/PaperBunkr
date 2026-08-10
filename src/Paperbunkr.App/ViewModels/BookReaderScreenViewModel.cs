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
/// </summary>
public partial class BookReaderScreenViewModel : ViewModelBase
{
    private readonly Action _goBack;

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
    private double _progressPercent;

    [ObservableProperty]
    private bool _canGoPrevious;

    public void LoadBook(int bookId)
    {
        _source?.Dispose();
        _history.Clear();

        using var context = PaperbunkrDb.CreateContext();
        _book = context.Books.Single(b => b.Id == bookId);

        _source = _book.Format == BookFormat.Epub
            ? new EpubBookSource(_book.FilePath)
            : new PdfBookSource(_book.FilePath);

        BookTitle = _book.Title;

        TableOfContents.Clear();
        for (int i = 0; i < _source.Chapters.Count; i++)
        {
            TableOfContents.Add(new BookChapterSummary { Index = i, Title = _source.Chapters[i].Title, IsActive = i == 0 });
        }

        _position = BookPosition.Start;
        IsChromeVisible = false;
        IsTocOpen = false;
        IsFontSheetOpen = false;
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
        if (IsTocOpen || IsFontSheetOpen)
        {
            IsTocOpen = false;
            IsFontSheetOpen = false;
            return;
        }

        IsChromeVisible = !IsChromeVisible;
    }

    [RelayCommand]
    private void OpenToc()
    {
        IsFontSheetOpen = false;
        IsTocOpen = true;
    }

    [RelayCommand]
    private void CloseToc() => IsTocOpen = false;

    [RelayCommand]
    private void OpenFontSheet()
    {
        IsTocOpen = false;
        IsFontSheetOpen = true;
    }

    [RelayCommand]
    private void CloseFontSheet() => IsFontSheetOpen = false;

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
            _history.Pop(); // already at the end of the book - nothing to move to, don't record a no-op history entry
            return;
        }

        RecomputeCurrentPage();
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
    }

    [RelayCommand]
    private void Close() => _goBack();

    [RelayCommand]
    private void SetFontFamily(BookFontFamilyOption option) => Settings.FontFamilyOption = option;

    [RelayCommand]
    private void SetLineSpacing(BookLineSpacingOption option) => Settings.LineSpacing = option;

    [RelayCommand]
    private void SetTheme(BookTheme theme) => Settings.Theme = theme;

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
