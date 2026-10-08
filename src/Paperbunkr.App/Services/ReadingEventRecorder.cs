using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.History;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>
/// Default <see cref="IReadingEventRecorder"/>. Constructed once in <c>MainViewModel</c> and handed
/// to the three reader view-models. Writes via a fresh short-lived context per call, same pattern as
/// the reader VMs' own inline read-state saves (<c>ReaderScreenViewModel.FlushPendingPositionSave</c>).
/// </summary>
public sealed class ReadingEventRecorder : IReadingEventRecorder
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    /// <param name="contextFactory">Defaults to <see cref="PaperbunkrDb.CreateContext"/>; tests pass a factory over a temp/in-memory database.</param>
    public ReadingEventRecorder(Func<PaperbunkrDbContext>? contextFactory = null)
    {
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
    }

    public event Action? ReadingEventRecorded;

    public void RecordOpened(ReadingItemType itemType, int itemId, int? seriesId, string? publisher, string? primaryGenre)
        => Insert(new ReadingEvent
        {
            ItemType = itemType,
            ItemId = itemId,
            Kind = ReadingEventKind.Opened,
            TimestampUtc = DateTime.UtcNow,
            SeriesId = seriesId,
            Publisher = publisher,
            PrimaryGenre = primaryGenre,
        });

    public event Action<ReadingEvent>? ReadingFinished;

    public void RecordFinished(ReadingItemType itemType, int itemId, int? seriesId, string? publisher, string? primaryGenre, int? pagesRead)
    {
        var finished = new ReadingEvent
        {
            ItemType = itemType,
            ItemId = itemId,
            Kind = ReadingEventKind.Finished,
            TimestampUtc = DateTime.UtcNow,
            PagesRead = pagesRead is > 0 ? pagesRead : null,
            SeriesId = seriesId,
            Publisher = publisher,
            PrimaryGenre = primaryGenre,
        };

        Insert(finished);

        // After the row is durably saved. A throwing handler never reaches the reader that recorded
        // the finish - the plugin host isolates its own failures, this is just belt and braces.
        try
        {
            ReadingFinished?.Invoke(finished);
        }
        catch (Exception)
        {
        }
    }

    public void UpdateSessionPages(ReadingItemType itemType, int itemId, int pagesRead) =>
        UpdateSessionPages(itemType, itemId, pagesRead, activeSeconds: null);

    public void UpdateSessionPages(ReadingItemType itemType, int itemId, int pagesRead, int? activeSeconds)
    {
        if (pagesRead <= 0)
        {
            return;
        }

        using var context = _contextFactory();
        var row = context.ReadingEvents
            .Where(e => e.ItemType == itemType && e.ItemId == itemId
                        && e.Kind == ReadingEventKind.Opened && e.PagesRead == null)
            .OrderByDescending(e => e.TimestampUtc)
            .ThenByDescending(e => e.Id)
            .FirstOrDefault();

        if (row is null)
        {
            return;
        }

        row.PagesRead = pagesRead;
        row.ActiveSeconds = activeSeconds is > 0 ? activeSeconds : null;
        context.SaveChanges();
        ReadingEventRecorded?.Invoke();
    }

    public void HideFromHistory(ReadingHistoryGroupKey? group)
    {
        using var context = _contextFactory();
        var rows = context.ReadingEvents.Where(e => !e.HiddenFromHistory);
        rows = group switch
        {
            null => rows,
            { Kind: ReadingHistoryGroupKind.ComicSeries } => rows.Where(e => e.ItemType == ReadingItemType.Comic && e.SeriesId == group.Id),
            { Kind: ReadingHistoryGroupKind.BookSeries } => rows.Where(e => e.ItemType == ReadingItemType.Novel && e.SeriesId == group.Id),
            _ => rows.Where(e => e.ItemType == ReadingItemType.Novel && e.SeriesId == null && e.ItemId == group.Id),
        };

        rows.ExecuteUpdate(setters => setters.SetProperty(e => e.HiddenFromHistory, true));
        ReadingEventRecorded?.Invoke();
    }

    private void Insert(ReadingEvent readingEvent)
    {
        using var context = _contextFactory();
        FillHistorySnapshot(context, readingEvent);
        context.ReadingEvents.Add(readingEvent);
        context.SaveChanges();
        ReadingEventRecorded?.Invoke();
    }

    /// <summary>
    /// Freezes the History tab's display names onto the row (docs/superpowers/specs/2026-09-29-insights-
    /// reading-history-design.md §1) so a "no longer in library" row can still be named after the item is
    /// deleted. Best-effort: a failed or empty lookup leaves both null and never blocks the insert.
    /// </summary>
    private static void FillHistorySnapshot(PaperbunkrDbContext context, ReadingEvent readingEvent)
    {
        try
        {
            if (readingEvent.ItemType == ReadingItemType.Comic)
            {
                var issue = context.Issues.IgnoreQueryFilters().AsNoTracking()
                    .Include(i => i.Series)
                    .Include(i => i.MetadataProposals)
                    .FirstOrDefault(i => i.Id == readingEvent.ItemId);
                if (issue is not null)
                {
                    readingEvent.SeriesTitle = issue.Series?.Name;
                    readingEvent.ItemLabel = ReadingHistoryLabels.IssueLabel(issue);
                }
            }
            else
            {
                var book = context.Books.AsNoTracking().Include(b => b.BookSeries).FirstOrDefault(b => b.Id == readingEvent.ItemId);
                if (book is not null)
                {
                    readingEvent.SeriesTitle = ReadingHistoryLabels.BookGroupTitle(book, book.BookSeries);
                    readingEvent.ItemLabel = ReadingHistoryLabels.BookLabel(book, book.BookSeries);
                }
            }
        }
        catch (Exception)
        {
            // Names are a display nicety for the History tab - never let them cost the reading event itself.
        }
    }
}
