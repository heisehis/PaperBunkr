using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Multi-level undo/redo for metadata edits (docs/ce-feature-inventory.md §A) - no CE precedent
/// exists for this feature (verified against <c>ComicBookDialog.cs</c>/<c>MultipleComicBooksDialog.
/// cs</c>). Session-only (a plain in-memory stack pair, never persisted) - undoing a change from a
/// prior app session against data someone else may have since edited again isn't a scenario worth
/// the added complexity of a durable history store, and no CE precedent suggests one either.
///
/// <see cref="Shared"/> is a single app-wide instance rather than constructor-injected into
/// <c>IssuePropertiesScreenViewModel</c>/<c>BulkIssuePropertiesScreenViewModel</c>/<c>MainViewModel</c>
/// - an edit made in one editor has to be undoable even after switching to the other, so the stack
/// itself has to be shared, not per-editor-instance. Every constructor that takes this service
/// still accepts an explicit instance too (test seam, same shape as this codebase's <c>_contextFactory</c>
/// convention elsewhere) - <see cref="Shared"/> is only ever the *default*.
/// </summary>
public sealed class MetadataEditHistoryService
{
    public static readonly MetadataEditHistoryService Shared = new();

    private readonly Stack<MetadataEditHistoryEntry> _undoStack = new();
    private readonly Stack<MetadataEditHistoryEntry> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Every <see cref="BulkFieldRegistry"/> field's current value on <paramref name="issue"/> -
    /// reused as-is by both editors' Save, so Undo/Redo needs no separate field enumeration of its
    /// own. Restoring a list field (Genre/Tags) through <see cref="BulkFieldDescriptor.Set"/> re-diffs
    /// via <see cref="Issue.MergeFrom"/> same as a real edit would - a value removed then restored
    /// comes back as a plain new tag, without whatever Category/Weight it had before (the same
    /// tradeoff <see cref="Issue.MergeFrom"/>'s own doc comment already accepts for a real edit).
    /// </summary>
    public static Dictionary<string, string?> CaptureSnapshot(Issue issue) =>
        BulkFieldRegistry.All.ToDictionary(f => f.Label, f => f.Get(issue));

    /// <summary>Pushed by a Save; clears the redo stack, same as every other undo/redo implementation - redoing a since-superseded future doesn't make sense once a new edit happens.</summary>
    public void Record(string description, Dictionary<int, Dictionary<string, string?>> before, Dictionary<int, Dictionary<string, string?>> after)
    {
        _undoStack.Push(new MetadataEditHistoryEntry { Description = description, Before = before, After = after });
        _redoStack.Clear();
    }

    /// <summary>The <see cref="Book"/>-row equivalent of <see cref="Record"/> (docs/superpowers/specs/
    /// 2026-08-27-book-properties-editor-design.md) - one entry per Book Properties overlay Save.
    /// <paramref name="before"/>/<paramref name="after"/> are <see cref="BookMetadataSnapshot"/> dicts.</summary>
    public void RecordBookEdit(string description, int bookId, Dictionary<string, string?> before, Dictionary<string, string?> after) =>
        RecordBookEdits(description, new() { [bookId] = before }, new() { [bookId] = after });

    /// <summary>Multi-book variant (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-
    /// design.md) - one entry spanning every book a bulk Save touched. <c>Apply</c>'s
    /// <see cref="MetadataEditTarget.Book"/> branch already restores every key.</summary>
    public void RecordBookEdits(string description, Dictionary<int, Dictionary<string, string?>> before, Dictionary<int, Dictionary<string, string?>> after)
    {
        _undoStack.Push(new MetadataEditHistoryEntry
        {
            Description = description,
            Target = MetadataEditTarget.Book,
            Before = before,
            After = after,
        });
        _redoStack.Clear();
    }

    /// <summary>Returns the undone entry's description for a toast, or null if there was nothing to undo.</summary>
    public string? Undo(System.Func<PaperbunkrDbContext> contextFactory)
    {
        if (_undoStack.Count == 0)
        {
            return null;
        }

        var entry = _undoStack.Pop();
        Apply(contextFactory, entry.Target, entry.Before);
        _redoStack.Push(entry);
        return entry.Description;
    }

    public string? Redo(System.Func<PaperbunkrDbContext> contextFactory)
    {
        if (_redoStack.Count == 0)
        {
            return null;
        }

        var entry = _redoStack.Pop();
        Apply(contextFactory, entry.Target, entry.After);
        _undoStack.Push(entry);
        return entry.Description;
    }

    private static void Apply(System.Func<PaperbunkrDbContext> contextFactory, MetadataEditTarget target, Dictionary<int, Dictionary<string, string?>> snapshots)
    {
        using var context = contextFactory();

        if (target == MetadataEditTarget.Book)
        {
            var books = context.Books.Where(b => snapshots.Keys.Contains(b.Id)).ToList();
            foreach (var book in books)
            {
                BookMetadataSnapshot.Apply(book, snapshots[book.Id]);
            }

            context.SaveChanges();
            return;
        }

        var issues = context.Issues.Include(i => i.Series).Include(i => i.Tags)
            .Where(i => snapshots.Keys.Contains(i.Id)).ToList();
        foreach (var issue in issues)
        {
            var values = snapshots[issue.Id];
            foreach (var field in BulkFieldRegistry.All)
            {
                field.Set(issue, values.GetValueOrDefault(field.Label));
            }
        }

        context.SaveChanges();
    }
}
