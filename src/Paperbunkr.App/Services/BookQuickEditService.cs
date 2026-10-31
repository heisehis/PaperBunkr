using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// The "Rate ▸" / "Set status ▸" one-click edits (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §6): write at once,
/// record one undo entry, and ask for an EPUB write-back. Shared by the Books grid menu and the Book Detail screen so both behave the same.
/// A rating set here is the user's own, so its provenance becomes Manual.
/// </summary>
public sealed class BookQuickEditService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly MetadataEditHistoryService _history;
    private readonly Func<IReadOnlyList<int>, Task>? _writeBack;

    public BookQuickEditService()
        : this(PaperbunkrDb.CreateContext, MetadataEditHistoryService.Shared, ids => new BookFileWriteBackService().AutoWriteAsync(ids))
    {
    }

    internal BookQuickEditService(Func<PaperbunkrDbContext> contextFactory, MetadataEditHistoryService history, Func<IReadOnlyList<int>, Task>? writeBack = null)
    {
        _contextFactory = contextFactory;
        _history = history;
        _writeBack = writeBack;
    }

    public bool SetStatus(IReadOnlyList<int> bookIds, BookReadingStatus status) =>
        Edit(bookIds, status == BookReadingStatus.Finished ? "Marked as finished" : $"Set status to {BookStatusLabels.ToLabel(status)}", (book, _) =>
        {
            if (book.Status == status)
            {
                return false;
            }

            book.Status = status;
            return true;
        });

    /// <param name="rating">0.5 steps up to 5; null or 0 clears it.</param>
    public bool SetRating(IReadOnlyList<int> bookIds, float? rating)
    {
        float? normalized = BookRating.Normalize(rating);
        return Edit(bookIds, normalized is null ? "Cleared rating" : $"Rated {normalized:0.#} stars", (book, now) =>
        {
            if (book.Rating == normalized)
            {
                return false;
            }

            book.Rating = normalized;
            BookMetadataApplier.MarkManual(book, new[] { BookFields.Rating }, now);
            return true;
        });
    }

    private bool Edit(IReadOnlyList<int> bookIds, string description, Func<Book, DateTime, bool> apply)
    {
        if (bookIds.Count == 0)
        {
            return false;
        }

        using var context = _contextFactory();
        var books = context.Books.Include(b => b.Tags).Include(b => b.FieldSources).Where(b => bookIds.Contains(b.Id)).ToList();
        if (books.Count == 0)
        {
            return false;
        }

        var before = books.ToDictionary(b => b.Id, b => BookMetadataSnapshot(b));
        DateTime now = DateTime.UtcNow;
        var changed = books.Where(b => apply(b, now)).ToList();
        if (changed.Count == 0)
        {
            return false;
        }

        context.SaveChanges();

        _history.RecordBookEdits(
            changed.Count == 1 ? $"{description}: “{changed[0].Title}”" : $"{description} ({changed.Count} books)",
            changed.ToDictionary(b => b.Id, b => before[b.Id]),
            changed.ToDictionary(b => b.Id, b => BookMetadataSnapshot(b)));

        if (_writeBack is not null)
        {
            _ = _writeBack(changed.Select(b => b.Id).ToList());
        }

        return true;
    }

    private static Dictionary<string, string?> BookMetadataSnapshot(Book book) => Models.BookMetadataSnapshot.Capture(book);
}
