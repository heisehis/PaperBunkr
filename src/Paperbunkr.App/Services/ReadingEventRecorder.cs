using System;
using System.Linq;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

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

    public void RecordFinished(ReadingItemType itemType, int itemId, int? seriesId, string? publisher, string? primaryGenre, int? pagesRead)
        => Insert(new ReadingEvent
        {
            ItemType = itemType,
            ItemId = itemId,
            Kind = ReadingEventKind.Finished,
            TimestampUtc = DateTime.UtcNow,
            PagesRead = pagesRead is > 0 ? pagesRead : null,
            SeriesId = seriesId,
            Publisher = publisher,
            PrimaryGenre = primaryGenre,
        });

    public void UpdateSessionPages(ReadingItemType itemType, int itemId, int pagesRead)
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
        context.SaveChanges();
        ReadingEventRecorded?.Invoke();
    }

    private void Insert(ReadingEvent readingEvent)
    {
        using var context = _contextFactory();
        context.ReadingEvents.Add(readingEvent);
        context.SaveChanges();
        ReadingEventRecorded?.Invoke();
    }
}
