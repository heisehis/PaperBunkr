using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Books;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>The "Now reading" spread and the nightstand above the Books grid (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §3.1).</summary>
public partial class BooksScreenViewModel
{
    /// <summary>Opens the reader for a book; set by MainViewModel. Null in tests that do not care.</summary>
    public Action<int, BookFormat>? GoReaderForBook { get; set; }

    /// <summary>Opens Insights from the nightstand's "This week" line; set by MainViewModel.</summary>
    public Action? GoInsights { get; set; }

    /// <summary>Clock for "This week"; tests replace it.</summary>
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    [ObservableProperty]
    private BookSpreadModel? _spread;

    public ObservableCollection<BookCardSample> Nightstand { get; } = new();

    [ObservableProperty]
    private string? _thisWeekLine;

    public bool HasNightstand => Nightstand.Count > 0;

    public bool HasThisWeek => !string.IsNullOrWhiteSpace(ThisWeekLine);

    /// <summary>Only on the All tab with nothing narrowing the list: any filter gives the grid the height a 768 px window needs.</summary>
    public bool ShowSpread =>
        Spread is not null
        && ActiveStatusTab == BooksStatusTab.All
        && !HasSearch
        && !HasActiveShelf
        && !HasAuthorFilter
        && _allCards.Count > 0;

    partial void OnSpreadChanged(BookSpreadModel? value) => OnPropertyChanged(nameof(ShowSpread));

    partial void OnThisWeekLineChanged(string? value) => OnPropertyChanged(nameof(HasThisWeek));

    /// <summary>Re-reads the spread, the nightstand and "This week" from the loaded cards and the database. Called after every load.</summary>
    private void RefreshSpread()
    {
        Nightstand.Clear();
        var pick = BooksSpreadResolver.PickSpread(_allCards);
        if (pick is not { } chosen)
        {
            Spread = null;
            ThisWeekLine = null;
            OnPropertyChanged(nameof(HasNightstand));
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.AsNoTracking().FirstOrDefault(b => b.Id == chosen.Card.BookId);
        string? newestHighlight = context.BookHighlights.AsNoTracking()
            .Where(h => h.BookId == chosen.Card.BookId)
            .OrderByDescending(h => h.Id)
            .Select(h => h.Excerpt)
            .FirstOrDefault();
        var bookEvents = context.ReadingEvents.AsNoTracking()
            .Where(e => e.ItemType == ReadingItemType.Novel)
            .ToList();

        string meta = string.Empty;
        string? quote = null;
        if (book is not null)
        {
            quote = BooksSpreadResolver.PickQuote(book.LastExcerpt, newestHighlight, Services.Books.SynopsisText.Clean(book.Summary));
            double progress = BookProgress.Compute(book);
            var stats = BookReadingStatsResolver.Compute(bookEvents.Where(e => e.ItemId == book.Id), book.CharacterCount, progress,
                book.Status == BookReadingStatus.Finished);
            meta = chosen.Mode == BookSpreadMode.NowReading
                ? BooksSpreadResolver.BuildMetaLine(book.LastChapterIndex + 1, book.ChapterCount, progress, stats.TimeLeft)
                : string.Empty;
        }

        Spread = new BookSpreadModel(chosen.Card, chosen.Mode, quote, meta);
        foreach (var card in BooksSpreadResolver.PickNightstand(_allCards, chosen.Card.BookId))
        {
            Nightstand.Add(card);
        }

        ThisWeekLine = BooksSpreadResolver.BuildThisWeekLine(bookEvents, UtcNow());
        OnPropertyChanged(nameof(HasNightstand));
    }

    /// <summary>The spread's primary button: Continue / Start reading.</summary>
    [RelayCommand]
    private void ContinueSpread()
    {
        if (Spread?.Card is { } card)
        {
            GoReaderForBook?.Invoke(card.BookId, card.Format);
        }
    }

    /// <summary>A click on the spread's cover or title, or a nightstand book: Book Detail.</summary>
    [RelayCommand]
    private void OpenSpreadBook(BookCardSample? card)
    {
        var target = card ?? Spread?.Card;
        if (target is { IsGhost: false })
        {
            _goBookDetail(target.BookId);
        }
    }

    [RelayCommand]
    private void OpenInsights() => GoInsights?.Invoke();
}
