using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Books;

/// <summary>Whether the Books spread shows the book being read or the next one to start.</summary>
public enum BookSpreadMode
{
    NowReading,
    UpNext,
}

/// <summary>
/// Picks what the Books screen's spread and nightstand show (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §3.1).
/// Pure functions over the cards the screen already has, plus the reading events for "This week", so each rule is testable on its own.
/// </summary>
public static class BooksSpreadResolver
{
    public const int NightstandSize = 3;

    /// <summary>The spread's book: the most recently opened Reading book; else the next book in a series being read; else the oldest
    /// Want to read; else the newest book added that is not finished or abandoned. Null for an empty library.</summary>
    public static (BookCardSample Card, BookSpreadMode Mode)? PickSpread(IReadOnlyList<BookCardSample> cards)
    {
        var real = cards.Where(c => !c.IsGhost).ToList();

        var reading = MostRecentlyReading(real).FirstOrDefault();
        if (reading is not null)
        {
            return (reading, BookSpreadMode.NowReading);
        }

        var next = NextInSeries(real).FirstOrDefault();
        if (next is not null)
        {
            return (next, BookSpreadMode.UpNext);
        }

        var wanted = real.Where(c => c.Status == BookReadingStatus.WantToRead).OrderBy(c => c.AddedTime).ThenBy(c => c.BookId).FirstOrDefault();
        if (wanted is not null)
        {
            return (wanted, BookSpreadMode.UpNext);
        }

        var newest = real
            .Where(c => c.Status is not (BookReadingStatus.Finished or BookReadingStatus.Abandoned))
            .OrderByDescending(c => c.AddedTime).ThenByDescending(c => c.BookId)
            .FirstOrDefault();
        return newest is null ? null : (newest, BookSpreadMode.UpNext);
    }

    /// <summary>Up to <see cref="NightstandSize"/> books beside the spread: other Reading books by last opened, then next-in-series books.</summary>
    public static IReadOnlyList<BookCardSample> PickNightstand(IReadOnlyList<BookCardSample> cards, int? spreadBookId)
    {
        var real = cards.Where(c => !c.IsGhost).ToList();
        var picked = new List<BookCardSample>(NightstandSize);
        foreach (var card in MostRecentlyReading(real).Concat(NextInSeries(real)))
        {
            if (picked.Count == NightstandSize)
            {
                break;
            }

            if (card.BookId != spreadBookId && picked.All(p => p.BookId != card.BookId))
            {
                picked.Add(card);
            }
        }

        return picked;
    }

    /// <summary>The pull quote: the saved excerpt, else the newest highlight, else the synopsis's first sentence; null when none exists.</summary>
    public static string? PickQuote(string? lastExcerpt, string? newestHighlight, string? summary)
    {
        if (!string.IsNullOrWhiteSpace(lastExcerpt))
        {
            return lastExcerpt.Trim();
        }

        if (QuoteExcerpt.Cut(newestHighlight) is { } highlight)
        {
            return highlight;
        }

        return QuoteExcerpt.Cut(FirstSentence(summary));
    }

    /// <summary>"Ch. 9 · 37% · about 2h 10m left"; parts that are unknown drop out.</summary>
    public static string BuildMetaLine(int chapterNumber, int chapterCount, double progress, TimeSpan? timeLeft)
    {
        var parts = new List<string>(3);
        if (chapterCount > 0 && chapterNumber > 0)
        {
            parts.Add($"Ch. {Math.Min(chapterNumber, chapterCount)} of {chapterCount}");
        }

        if (progress > 0d)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{Math.Round(Math.Clamp(progress, 0d, 1d) * 100):0}%"));
        }

        if (timeLeft is { } left && left > TimeSpan.Zero)
        {
            parts.Add($"about {FormatDuration(left)} left");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The nightstand's one-line summary of the last seven days of book reading, or null when nothing was read.</summary>
    public static string? BuildThisWeekLine(IEnumerable<ReadingEvent> events, DateTime nowUtc)
    {
        DateTime since = nowUtc - TimeSpan.FromDays(7);
        var week = events.Where(e => e.ItemType == ReadingItemType.Novel && e.TimestampUtc > since && e.TimestampUtc <= nowUtc).ToList();

        // A Finished row repeats its session's pages, so time and pages come from Opened rows only (BookReadingStatsResolver does the same).
        var opened = week.Where(e => e.Kind == ReadingEventKind.Opened).ToList();
        long seconds = opened.Sum(e => (long)(e.ActiveSeconds ?? 0));
        int pages = opened.Sum(e => e.PagesRead ?? 0);
        int finished = week.Count(e => e.Kind == ReadingEventKind.Finished);
        if (seconds <= 0 && pages <= 0 && finished == 0)
        {
            return null;
        }

        var parts = new List<string> { "This week" };
        if (seconds > 0)
        {
            parts.Add($"{FormatDuration(TimeSpan.FromSeconds(seconds))} read");
        }

        if (pages > 0)
        {
            parts.Add(pages == 1 ? "1 page" : string.Create(CultureInfo.CurrentCulture, $"{pages:N0} pages"));
        }

        if (finished > 0)
        {
            parts.Add($"{finished} finished");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>"3h 20m", "45m", "2h" - the Detail dashboard's format.</summary>
    public static string FormatDuration(TimeSpan span)
    {
        int minutes = (int)Math.Round(span.TotalMinutes);
        if (minutes < 60)
        {
            return $"{Math.Max(minutes, 1)}m";
        }

        int hours = minutes / 60;
        int rest = minutes % 60;
        return rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }

    private static IEnumerable<BookCardSample> MostRecentlyReading(IEnumerable<BookCardSample> cards) =>
        cards.Where(c => c.Status == BookReadingStatus.Reading)
            .OrderByDescending(c => c.LastOpenedTime ?? DateTime.MinValue)
            .ThenBy(c => c.BookId);

    /// <summary>For each series the reader has started (a Reading or Finished book in it), the lowest-numbered book still Unread or Want to
    /// read, ordered by when that series was last opened.</summary>
    private static IEnumerable<BookCardSample> NextInSeries(IReadOnlyList<BookCardSample> cards) =>
        cards.Where(c => c.BookSeriesId is not null)
            .GroupBy(c => c.BookSeriesId!.Value)
            .Where(g => g.Any(c => c.Status is BookReadingStatus.Reading or BookReadingStatus.Finished))
            .Select(g => new
            {
                LastOpened = g.Max(c => c.LastOpenedTime ?? DateTime.MinValue),
                Next = g.Where(c => c.Status is BookReadingStatus.Unread or BookReadingStatus.WantToRead)
                    .OrderBy(c => c.SeriesIndex ?? float.MaxValue).ThenBy(c => c.BookId)
                    .FirstOrDefault(),
            })
            .Where(x => x.Next is not null)
            .OrderByDescending(x => x.LastOpened)
            .Select(x => x.Next!);

    private static string? FirstSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();
        for (int i = 0; i < trimmed.Length - 1; i++)
        {
            if (trimmed[i] is '.' or '!' or '?' && char.IsWhiteSpace(trimmed[i + 1]))
            {
                return trimmed[..(i + 1)];
            }
        }

        return trimmed;
    }
}
