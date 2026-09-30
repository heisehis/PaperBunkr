using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.History;

/// <summary>
/// Builds the Insights History list (docs/superpowers/specs/2026-09-29-insights-reading-history-design.md §2):
/// one row per comic series / book series / standalone book, at its most recent non-hidden reading event,
/// newest first (Mihon-style - a series appears once, however often it was read).
///
/// Lives in App rather than Data because "next issue" must use the reader's own <see cref="IssueOrdering.OrderByNumber"/>
/// (<c>ReadingOrderResolver</c>'s series branch). Every live lookup goes through <c>IgnoreQueryFilters()</c> so
/// remote-library items resolve while their source is connected. Search and type filtering are the caller's job.
/// </summary>
public static class ReadingHistoryResolver
{
    private sealed record Ev(int Id, ReadingItemType ItemType, int ItemId, ReadingEventKind Kind, DateTime TimestampUtc, int? SeriesId, string? SeriesTitle, string? ItemLabel);

    public static IReadOnlyList<ReadingHistoryRow> Resolve(PaperbunkrDbContext context)
    {
        var events = context.ReadingEvents.AsNoTracking()
            .Where(e => !e.HiddenFromHistory)
            .Select(e => new Ev(e.Id, e.ItemType, e.ItemId, e.Kind, e.TimestampUtc, e.SeriesId, e.SeriesTitle, e.ItemLabel))
            .ToList();
        if (events.Count == 0)
        {
            return Array.Empty<ReadingHistoryRow>();
        }

        // A comic row written without a SeriesId (shouldn't happen from the readers, but the column is nullable)
        // is attributed to its issue's live series; if that's unknowable too the row can't be grouped and is skipped.
        var orphanIssueIds = events.Where(e => e.ItemType == ReadingItemType.Comic && e.SeriesId is null).Select(e => e.ItemId).Distinct().ToList();
        var orphanSeriesByIssue = orphanIssueIds.Count == 0
            ? new Dictionary<int, int>()
            : context.Issues.IgnoreQueryFilters().AsNoTracking()
                .Where(i => orphanIssueIds.Contains(i.Id))
                .Select(i => new { i.Id, i.SeriesId })
                .ToDictionary(i => i.Id, i => i.SeriesId);

        var groups = new Dictionary<ReadingHistoryGroupKey, List<Ev>>();
        foreach (var e in events)
        {
            int? seriesId = e.SeriesId;
            if (e.ItemType == ReadingItemType.Comic && seriesId is null)
            {
                if (!orphanSeriesByIssue.TryGetValue(e.ItemId, out int live))
                {
                    continue;
                }

                seriesId = live;
            }

            var key = ReadingHistoryGroupKey.For(e.ItemType, e.ItemId, seriesId);
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = new List<Ev>();
            }

            list.Add(e);
        }

        var comicSeriesIds = groups.Keys.Where(k => k.Kind == ReadingHistoryGroupKind.ComicSeries).Select(k => k.Id).ToList();
        var seriesById = comicSeriesIds.Count == 0
            ? new Dictionary<int, Series>()
            : context.Series.IgnoreQueryFilters().AsNoTracking()
                .Where(s => comicSeriesIds.Contains(s.Id))
                .Include(s => s.Issues).ThenInclude(i => i.MetadataProposals)
                .AsSplitQuery()
                .ToDictionary(s => s.Id);

        var bookSeriesIds = groups.Keys.Where(k => k.Kind == ReadingHistoryGroupKind.BookSeries).Select(k => k.Id).ToList();
        var bookSeriesById = bookSeriesIds.Count == 0
            ? new Dictionary<int, BookSeries>()
            : context.BookSeries.AsNoTracking().Where(s => bookSeriesIds.Contains(s.Id)).ToDictionary(s => s.Id);

        var bookIds = events.Where(e => e.ItemType == ReadingItemType.Novel).Select(e => e.ItemId).Distinct().ToList();
        var booksById = bookIds.Count == 0
            ? new Dictionary<int, Book>()
            : context.Books.AsNoTracking().Where(b => bookIds.Contains(b.Id)).ToDictionary(b => b.Id);

        var rows = new List<ReadingHistoryRow>(groups.Count);
        foreach (var (key, groupEvents) in groups)
        {
            var newestFirst = groupEvents.OrderByDescending(e => e.TimestampUtc).ThenByDescending(e => e.Id).ToList();
            var row = key.Kind switch
            {
                ReadingHistoryGroupKind.ComicSeries => seriesById.TryGetValue(key.Id, out var series)
                    ? ComicRow(key, series, newestFirst)
                    : GreyedRow(key, newestFirst, ReadingHistoryContentKind.Comic),
                ReadingHistoryGroupKind.BookSeries => bookSeriesById.TryGetValue(key.Id, out var bookSeries)
                    ? BookSeriesRow(key, bookSeries, newestFirst, booksById)
                    : GreyedRow(key, newestFirst, ReadingHistoryContentKind.Book),
                _ => booksById.TryGetValue(key.Id, out var book)
                    ? BookRow(key, book, null, newestFirst[0].TimestampUtc)
                    : GreyedRow(key, newestFirst, ReadingHistoryContentKind.Book),
            };

            if (row is not null)
            {
                rows.Add(row);
            }
        }

        return rows.OrderByDescending(r => r.LastReadUtc).ToList();
    }

    private static ReadingHistoryRow ComicRow(ReadingHistoryGroupKey key, Series series, List<Ev> newestFirst)
    {
        var issuesById = series.Issues.ToDictionary(i => i.Id);
        var ordered = series.Issues.OrderByNumber().ToList();
        var coverIssue = series.Issues.FirstOrDefault(i => i.Id == series.CoverIssueId) ?? ordered.FirstOrDefault();
        var kind = ContentTypeFamily.IsManga(series.ContentType) ? ReadingHistoryContentKind.Manga : ReadingHistoryContentKind.Comic;
        string? coverKey = coverIssue is null ? null : CoverFingerprint.Stem(coverIssue.Id, coverIssue.FilePath, coverIssue.FileSize);

        // Newest read issue still in this series (design Q17: a deleted last-read issue falls back to an earlier one).
        var issue = newestFirst.Select(e => issuesById.GetValueOrDefault(e.ItemId)).FirstOrDefault(i => i is not null);
        if (issue is null)
        {
            return new ReadingHistoryRow(key, series.Name, newestFirst[0].ItemLabel, newestFirst[0].TimestampUtc, coverKey, kind,
                IsInLibrary: true, IsFinished: false, IsCaughtUp: false, ProgressText: null, ReadingHistoryAction.None,
                TargetId: null, TargetLabel: null, BookFormat: null, DetailSeriesId: series.Id, DetailBookId: null);
        }

        string? label = ReadingHistoryLabels.IssueLabel(issue);
        bool finished = issue.HasBeenRead();
        if (!finished)
        {
            string? progress = issue.LastPageRead is > 0 && issue.PageCount is > 0
                ? $"page {Math.Min(issue.LastPageRead.Value + 1, issue.PageCount.Value)} of {issue.PageCount.Value}"
                : null;
            return new ReadingHistoryRow(key, series.Name, label, newestFirst[0].TimestampUtc, coverKey, kind,
                IsInLibrary: true, IsFinished: false, IsCaughtUp: false, progress, ReadingHistoryAction.Resume,
                issue.Id, label, BookFormat: null, series.Id, DetailBookId: null);
        }

        int index = ordered.FindIndex(i => i.Id == issue.Id);
        var next = ordered.Skip(index + 1).FirstOrDefault(i => !i.FileIsMissing);
        return next is null
            ? new ReadingHistoryRow(key, series.Name, label, newestFirst[0].TimestampUtc, coverKey, kind,
                IsInLibrary: true, IsFinished: true, IsCaughtUp: true, ProgressText: null, ReadingHistoryAction.None,
                TargetId: null, TargetLabel: null, BookFormat: null, series.Id, DetailBookId: null)
            : new ReadingHistoryRow(key, series.Name, label, newestFirst[0].TimestampUtc, coverKey, kind,
                IsInLibrary: true, IsFinished: true, IsCaughtUp: false, ProgressText: null, ReadingHistoryAction.ReadNext,
                next.Id, ReadingHistoryLabels.IssueLabel(next), BookFormat: null, series.Id, DetailBookId: null);
    }

    private static ReadingHistoryRow BookSeriesRow(ReadingHistoryGroupKey key, BookSeries series, List<Ev> newestFirst, Dictionary<int, Book> booksById)
    {
        // Newest read book still in this book series.
        var book = newestFirst
            .Select(e => booksById.GetValueOrDefault(e.ItemId))
            .FirstOrDefault(b => b is not null && b.BookSeriesId == series.Id);
        if (book is null)
        {
            string title = string.IsNullOrWhiteSpace(series.Name) ? newestFirst[0].SeriesTitle ?? string.Empty : series.Name;
            return new ReadingHistoryRow(key, title, newestFirst[0].ItemLabel, newestFirst[0].TimestampUtc, CoverKey: null,
                ReadingHistoryContentKind.Book, IsInLibrary: true, IsFinished: false, IsCaughtUp: false, ProgressText: null,
                ReadingHistoryAction.None, TargetId: null, TargetLabel: null, BookFormat: null, DetailSeriesId: null, DetailBookId: null);
        }

        return BookRow(key, book, series, newestFirst[0].TimestampUtc);
    }

    private static ReadingHistoryRow BookRow(ReadingHistoryGroupKey key, Book book, BookSeries? series, DateTime lastReadUtc)
    {
        string? label = ReadingHistoryLabels.BookLabel(book, series);
        string? progress = book.Format != BookFormat.Pdf && !book.Finished && book.LastProgressionFraction is double f and > 0
            ? $"{Math.Clamp((int)Math.Round(f * 100), 1, 99)}%"
            : null;
        var action = book.Format == BookFormat.Pdf ? ReadingHistoryAction.Open
            : book.Finished ? ReadingHistoryAction.None
            : ReadingHistoryAction.Resume;

        return new ReadingHistoryRow(key, ReadingHistoryLabels.BookGroupTitle(book, series), label, lastReadUtc,
            CoverFingerprint.Stem(book.Id, book.FilePath, null), ReadingHistoryContentKind.Book,
            IsInLibrary: true, book.Finished, IsCaughtUp: false, progress, action,
            action == ReadingHistoryAction.None ? null : book.Id, label ?? book.Title, book.Format,
            DetailSeriesId: null, DetailBookId: book.Id);
    }

    /// <summary>The whole series/book is gone - named from the newest event that carries a snapshot, or dropped
    /// when none does (items deleted before snapshots existed, design Q16).</summary>
    private static ReadingHistoryRow? GreyedRow(ReadingHistoryGroupKey key, List<Ev> newestFirst, ReadingHistoryContentKind kind)
    {
        var named = newestFirst.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.SeriesTitle));
        if (named is null)
        {
            return null;
        }

        return new ReadingHistoryRow(key, named.SeriesTitle!, named.ItemLabel, newestFirst[0].TimestampUtc, CoverKey: null, kind,
            IsInLibrary: false, IsFinished: newestFirst[0].Kind == ReadingEventKind.Finished, IsCaughtUp: false, ProgressText: null,
            ReadingHistoryAction.None, TargetId: null, TargetLabel: null, BookFormat: null, DetailSeriesId: null, DetailBookId: null);
    }
}
