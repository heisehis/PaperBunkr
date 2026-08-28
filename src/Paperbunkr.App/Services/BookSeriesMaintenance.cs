using System.Linq;
using Paperbunkr.Data;

namespace Paperbunkr.App.Services;

/// <summary>
/// Keeps <see cref="Paperbunkr.Data.Entities.BookSeries"/> rows from lingering once their last book
/// leaves them (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-design.md, component d).
/// Silent - no toast, no undo entry; a pruned series has no books by definition, so there's nothing
/// a user would want restored.
/// </summary>
public static class BookSeriesMaintenance
{
    /// <summary>
    /// Deletes <paramref name="bookSeriesId"/>'s row iff no <see cref="Paperbunkr.Data.Entities.Book"/>
    /// still references it. The caller passes its own live <paramref name="context"/> and must have
    /// already <c>SaveChanges()</c>d the membership change this responds to. No-op for a null id, a
    /// still-referenced series, or an already-deleted row.
    /// </summary>
    public static void PruneIfEmpty(PaperbunkrDbContext context, int? bookSeriesId)
    {
        if (bookSeriesId is not int id)
        {
            return;
        }

        if (context.Books.Any(b => b.BookSeriesId == id))
        {
            return;
        }

        var series = context.BookSeries.Find(id);
        if (series is null)
        {
            return;
        }

        context.BookSeries.Remove(series);
        context.SaveChanges();
    }
}
