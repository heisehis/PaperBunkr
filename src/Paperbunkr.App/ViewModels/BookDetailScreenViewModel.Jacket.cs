using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Books;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>A fact in the Overview tab's "About this edition" card.</summary>
public sealed record EditionFact(string Label, string Value);

/// <summary>
/// The Book Detail "jacket" and its tabs' new content (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §4): the chapter
/// ribbon, the series spines, edition facts, "More by", the latest highlight, part-grouped contents, the "Your reading" tab and Series mode's
/// stack and per-book ribbon.
/// </summary>
public partial class BookDetailScreenViewModel
{
    private TimeSpan? _timeLeft;

    /// <summary>The spine-colour key the jacket's wash and the cover flight use: this book's, or in Series mode the front book's.</summary>
    public string? JacketCoverKey => IsSeriesMode
        ? SeriesStack.FirstOrDefault()?.CoverKey
        : SpineColorCache.BookKeyPrefix + _bookId.ToString(CultureInfo.InvariantCulture);

    /// <summary>The shared-element key for the cover flight from the Books grid (redesign §7.2).</summary>
    public string JacketFlightKey => "book-cover:" + _bookId.ToString(CultureInfo.InvariantCulture);

    public ObservableCollection<RibbonSegment> RibbonSegments { get; } = new();

    [ObservableProperty]
    private string _ribbonCaption = string.Empty;

    public bool HasRibbon => RibbonSegments.Count > 0;

    /// <summary>"EPUB · 2019" over the title.</summary>
    [ObservableProperty]
    private string _overline = string.Empty;

    /// <summary>"The Orchard Cycle #2" beside the author; empty when the book stands alone.</summary>
    [ObservableProperty]
    private string _seriesPart = string.Empty;

    public ObservableCollection<EditionFact> EditionFacts { get; } = new();

    public ObservableCollection<BookCardSample> MoreByAuthor { get; } = new();

    [ObservableProperty]
    private string _moreByAuthorAllLabel = string.Empty;

    public bool HasMoreByAuthor => MoreByAuthor.Count > 0;

    /// <summary>"MORE BY MAARJA ILVES": overlines are capitals.</summary>
    public string MoreByOverline => $"More by {Author}".ToUpper(CultureInfo.CurrentCulture);

    [ObservableProperty]
    private BookNoteEntry? _latestHighlight;

    public bool HasLatestHighlight => LatestHighlight is not null;

    [ObservableProperty]
    private string _notesCountLabel = string.Empty;

    /// <summary>The book's series as spines beside the ribbon, the current book selected (so the spine draws its accent outline), ghosts for gaps.</summary>
    public ObservableCollection<BookCardSample> SeriesSpines { get; } = new();

    public bool HasSeriesSpines => SeriesSpines.Count > 1;

    /// <summary>The Contents tab, grouped by part title (a book without parts is one group with no title).</summary>
    public ObservableCollection<BookTocGroup> ChapterGroups { get; } = new();

    // --- Series mode ---

    /// <summary>Up to three covers for the fanned stack, front first.</summary>
    public ObservableCollection<BookCardSample> SeriesStack { get; } = new();

    [ObservableProperty]
    private string _seriesOverline = string.Empty;

    [ObservableProperty]
    private string _seriesContinueLabel = string.Empty;

    private BookCardSample? _seriesContinueBook;

    public bool HasSeriesContinue => _seriesContinueBook is not null;

    // --- the ⋯ menu ---

    [ObservableProperty]
    private bool _isMoreMenuOpen;

    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [RelayCommand]
    private void ToggleMoreMenu()
    {
        IsMoreMenuOpen = !IsMoreMenuOpen;
        IsDeleteConfirmOpen = false;
    }

    [RelayCommand]
    private void AskDelete() => IsDeleteConfirmOpen = true;

    // The ⋯ menu's rows live in the popup they close: closing it (or leaving the screen) while the row's click is still routing detaches the
    // row mid-event (CLAUDE.md, "don't remove/detach a control from inside a routed event"), so each row closes the menu, then acts, a tick later.
    private void FromMenu(Action action)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsMoreMenuOpen = false;
            IsDeleteConfirmOpen = false;
            action();
        });
    }

    [RelayCommand]
    private void MenuMatchMetadata() => FromMenu(() => MatchMetadataCommand.Execute(null));

    [RelayCommand]
    private void MenuReveal() => FromMenu(() => RevealInExplorerCommand.Execute(null));

    [RelayCommand]
    private void MenuToggleFinished() => FromMenu(() => ToggleFinishedCommand.Execute(null));

    [RelayCommand]
    private void ConfirmDelete() => FromMenu(() => DeleteBookCommand.Execute(null));

    /// <summary>A ribbon segment: a chapter opens the reader there, a book in a series opens its Detail.</summary>
    [RelayCommand]
    private void OpenRibbonSegment(object? tag)
    {
        switch (tag)
        {
            case BookChapterSummary chapter:
                OpenChapterCommand.Execute(chapter);
                break;
            case BookCardSample { IsGhost: false } card:
                OpenBookFromSeriesCommand.Execute(card);
                break;
        }
    }

    /// <summary>A spine or a "More by" cover: that book's Detail.</summary>
    [RelayCommand]
    private void OpenOtherBook(BookCardSample? card)
    {
        if (card is { IsGhost: false } && card.BookId != _bookId)
        {
            _goBookDetailForOther?.Invoke(card.BookId);
        }
    }

    /// <summary>Opens another book's Detail; set by MainViewModel (null in tests that do not care).</summary>
    public Action<int>? GoBookDetailForOther
    {
        get => _goBookDetailForOther;
        set => _goBookDetailForOther = value;
    }

    private Action<int>? _goBookDetailForOther;

    /// <summary>Series mode's primary button: the book in progress, else the next unread one.</summary>
    [RelayCommand]
    private void ContinueSeries()
    {
        if (_seriesContinueBook is { } card)
        {
            _goReaderForBook(card.BookId, card.Format, null);
        }
    }

    /// <summary>"n notes · see all" on the latest-highlight card.</summary>
    [RelayCommand]
    private void ShowNotes() => ActiveTab = BookDetailTab.Notes;

    /// <summary>Called at the end of every book load (from <see cref="LoadTabData"/>), with the book's tags, highlights and bookmarks loaded.</summary>
    private void LoadJacket(Book book)
    {
        Overline = string.Join(" · ", new[] { FormatBadge, book.PublishedDate?.Year.ToString(CultureInfo.CurrentCulture) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        SeriesPart = book.BookSeries is null ? string.Empty
            : book.SeriesIndex is float idx ? $"{book.BookSeries.Name} #{idx:0.##}" : book.BookSeries.Name;

        BuildChapterRibbon(book);
        BuildChapterGroups();
        BuildEditionFacts(book);
        BuildMoreByAndSpines(book);

        LatestHighlight = _allNotes.Where(n => n.IsHighlight).OrderByDescending(n => n.Created).FirstOrDefault();
        int count = _allNotes.Count;
        NotesCountLabel = count == 1 ? "1 note" : $"{count} notes";

        OnPropertyChanged(nameof(HasRibbon));
        OnPropertyChanged(nameof(HasMoreByAuthor));
        OnPropertyChanged(nameof(MoreByOverline));
        OnPropertyChanged(nameof(HasSeriesSpines));
        OnPropertyChanged(nameof(JacketCoverKey));
        OnPropertyChanged(nameof(JacketFlightKey));
    }

    partial void OnLatestHighlightChanged(BookNoteEntry? value) => OnPropertyChanged(nameof(HasLatestHighlight));

    private void BuildChapterRibbon(Book book)
    {
        RibbonSegments.Clear();
        if (Chapters.Count == 0)
        {
            // A PDF or a book without contents: one bar for the whole book.
            double progress = BookProgress.Compute(book);
            RibbonSegments.Add(new RibbonSegment { Fill = progress, Label = $"{Math.Round(progress * 100):0}% read", IsCurrent = true });
            RibbonCaption = TimeLeftSuffix(Math.Round(progress * 100) + "% read");
            return;
        }

        var weights = RibbonMath.Weights(Chapters.Select(c => c.LengthFraction).ToArray());
        int read = 0;
        int number = 0;
        foreach (var (chapter, weight) in Chapters.Zip(weights))
        {
            number++;
            double fill = chapter.IsRead ? 1 : chapter.IsCurrent ? chapter.CurrentFraction : 0;
            if (chapter.IsRead)
            {
                read++;
            }

            string state = chapter.IsRead ? "read" : chapter.IsCurrent ? $"{Math.Round(fill * 100):0}% read" : "unread";
            RibbonSegments.Add(new RibbonSegment
            {
                Weight = weight, Fill = fill, IsCurrent = chapter.IsCurrent, Tag = chapter,
                Label = $"Chapter {number}, {chapter.Title}, {state}",
            });
        }

        int current = Chapters.ToList().FindIndex(c => c.IsCurrent);
        string where = current >= 0 ? $"{current + 1} of {Chapters.Count} chapters" : $"{read} of {Chapters.Count} chapters read";
        RibbonCaption = TimeLeftSuffix(where);
    }

    private string TimeLeftSuffix(string head) =>
        (_timeLeft is { } left && left > TimeSpan.Zero ? $"{head} · about {BooksSpreadResolver.FormatDuration(left)} left" : head).ToUpperInvariant();

    private void BuildChapterGroups()
    {
        ChapterGroups.Clear();
        BookTocGroup? group = null;
        foreach (var chapter in Chapters)
        {
            if (group is null || !string.Equals(group.PartTitle, chapter.PartTitle, StringComparison.Ordinal))
            {
                group = new BookTocGroup { PartTitle = chapter.PartTitle };
                ChapterGroups.Add(group);
            }

            group.Chapters.Add(chapter);
        }
    }

    private void BuildEditionFacts(Book book)
    {
        EditionFacts.Clear();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                EditionFacts.Add(new EditionFact(label, value));
            }
        }

        Add("Publisher", book.Publisher);
        Add("Published", book.PublishedDate?.ToString("d MMMM yyyy", CultureInfo.CurrentCulture));
        Add("Language", LanguageName(book.LanguageISO));
        Add("ISBN", book.ISBN);
        Add("Format", book.ChapterCount > 0 ? $"{FormatBadge} · {book.ChapterCount} chapters" : FormatBadge);
        Add("Community", book.CommunityRating is float r
            ? book.CommunityRatingCount is int n ? $"{r:0.0} · {n:N0} ratings" : $"{r:0.0}"
            : null);
        Add("Added", book.AddedTime.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture));
    }

    private void BuildMoreByAndSpines(Book book)
    {
        MoreByAuthor.Clear();
        SeriesSpines.Clear();
        using var context = PaperbunkrDb.CreateContext();

        if (!string.IsNullOrWhiteSpace(book.Author))
        {
            var others = context.Books.AsNoTracking().Include(b => b.BookSeries)
                .Where(b => b.Author == book.Author && b.Id != book.Id)
                .OrderByDescending(b => b.LastOpenedTime).ThenBy(b => b.Title)
                .ToList();
            foreach (var other in others.Take(3))
            {
                MoreByAuthor.Add(BookCardSample.FromBook(other));
            }

            MoreByAuthorAllLabel = others.Count > 3 ? $"All {others.Count + 1}" : string.Empty;
        }

        if (book.BookSeriesId is int seriesId)
        {
            var cards = context.Books.AsNoTracking().Include(b => b.BookSeries)
                .Where(b => b.BookSeriesId == seriesId)
                .OrderBy(b => b.SeriesIndex == null).ThenBy(b => b.SeriesIndex).ThenBy(b => b.Title)
                .ToList()
                .Select(b => BookCardSample.FromBook(b))
                .ToList();
            var ghosts = BooksBrowseQuery.GhostsFor(cards);
            foreach (var card in cards)
            {
                foreach (var ghost in ghosts.Where(g => g.SeriesIndex < card.SeriesIndex && !SeriesSpines.Contains(g)))
                {
                    SeriesSpines.Add(ghost);
                }

                if (card.BookId == book.Id)
                {
                    card.IsSelected = true;
                }

                SeriesSpines.Add(card);
            }
        }
    }

    /// <summary>Called at the end of <see cref="LoadSeries"/>: the fanned stack, the per-book ribbon and the Continue target.</summary>
    private void LoadSeriesJacket()
    {
        var owned = SeriesBooks.Where(c => !c.IsGhost).ToList();
        SeriesStack.Clear();
        foreach (var card in owned.Take(3))
        {
            SeriesStack.Add(card);
        }

        int missing = SeriesBooks.Count - owned.Count;
        SeriesOverline = (missing > 0 ? $"Series · {SeriesBooks.Count} books · {missing} missing" : $"Series · {owned.Count} books").ToUpperInvariant();

        RibbonSegments.Clear();
        foreach (var card in SeriesBooks)
        {
            double fill = card.IsGhost ? 0 : card.Status == BookReadingStatus.Finished ? 1 : card.Status == BookReadingStatus.Reading ? card.Progress : 0;
            RibbonSegments.Add(new RibbonSegment
            {
                Fill = fill, IsGhost = card.IsGhost, Tag = card.IsGhost ? null : card,
                IsCurrent = card.Status == BookReadingStatus.Reading,
                Label = card.IsGhost ? card.Title : $"{card.Title}, {card.StatusCaption}".TrimEnd(',', ' '),
            });
        }

        _seriesContinueBook = owned.Where(c => c.Status == BookReadingStatus.Reading).OrderByDescending(c => c.LastOpenedTime).FirstOrDefault()
            ?? owned.FirstOrDefault(c => c.Status is BookReadingStatus.Unread or BookReadingStatus.WantToRead);
        int read = owned.Count(c => c.Status == BookReadingStatus.Finished);
        var next = owned.FirstOrDefault(c => c.Status is BookReadingStatus.Unread or BookReadingStatus.WantToRead);
        RibbonCaption = (next is null ? $"{read} of {owned.Count} read" : $"{read} of {owned.Count} read · next: {next.Title}").ToUpperInvariant();

        if (_seriesContinueBook is { } target)
        {
            int number = owned.IndexOf(target) + 1;
            SeriesContinueLabel = target.Status == BookReadingStatus.Reading
                ? $"Continue · Book {number} · {Math.Round(target.Progress * 100):0}%"
                : $"Start book {number}";
        }
        else
        {
            SeriesContinueLabel = string.Empty;
        }

        OnPropertyChanged(nameof(HasSeriesContinue));
        OnPropertyChanged(nameof(HasRibbon));
        OnPropertyChanged(nameof(JacketCoverKey));
    }
}
