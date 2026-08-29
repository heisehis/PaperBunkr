using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Collections;

/// <summary>
/// Create/rename/delete/reorder plus membership mutation for <see cref="Collection"/>
/// (docs/superpowers/specs/2026-08-27-collections-design.md). Same shape as
/// <see cref="Paperbunkr.Data.Metadata.ContinuityResolver"/>: static methods taking a live
/// <see cref="PaperbunkrDbContext"/>, each doing its own <see cref="DbContext.SaveChanges()"/>.
/// Read-only queries live in <see cref="CollectionResolver"/>.
/// </summary>
public static class CollectionService
{
    public static Collection Create(PaperbunkrDbContext context, string name)
    {
        int nextOrder = context.Collections.Any() ? context.Collections.Max(c => c.SortOrder) + 1 : 0;
        var collection = new Collection { Name = name.Trim(), SortOrder = nextOrder, IsAutoCover = true };
        context.Collections.Add(collection);
        context.SaveChanges();
        return collection;
    }

    public static void Rename(PaperbunkrDbContext context, int collectionId, string name)
    {
        var collection = context.Collections.Find(collectionId);
        if (collection is null)
        {
            return;
        }

        collection.Name = name.Trim();
        context.SaveChanges();
    }

    /// <summary>Cascade removes the collection's <see cref="CollectionItem"/> rows.</summary>
    public static void Delete(PaperbunkrDbContext context, int collectionId)
    {
        var collection = context.Collections.Find(collectionId);
        if (collection is null)
        {
            return;
        }

        context.Collections.Remove(collection);
        context.SaveChanges();
    }

    /// <summary>Assigns <see cref="Collection.SortOrder"/> = position in <paramref name="orderedCollectionIds"/>; ids not listed keep their current order after the listed ones.</summary>
    public static void Reorder(PaperbunkrDbContext context, IReadOnlyList<int> orderedCollectionIds)
    {
        var all = context.Collections.ToList();
        int i = 0;
        foreach (int id in orderedCollectionIds)
        {
            var c = all.FirstOrDefault(x => x.Id == id);
            if (c is not null)
            {
                c.SortOrder = i++;
            }
        }

        foreach (var c in all.Where(x => !orderedCollectionIds.Contains(x.Id)).OrderBy(x => x.SortOrder))
        {
            c.SortOrder = i++;
        }

        context.SaveChanges();
    }

    public static void SetAppearance(
        PaperbunkrDbContext context,
        int collectionId,
        string? description,
        string? accentColor,
        string? coverImagePath,
        bool isAutoCover)
    {
        var collection = context.Collections.Find(collectionId);
        if (collection is null)
        {
            return;
        }

        collection.Description = NullIfBlank(description);
        collection.AccentColor = NullIfBlank(accentColor);
        collection.CoverImagePath = NullIfBlank(coverImagePath);
        collection.IsAutoCover = isAutoCover;
        context.SaveChanges();
    }

    /// <summary>
    /// Adds the given targets to the collection, skipping any already present (idempotent). New
    /// items are appended after the collection's current highest <see cref="CollectionItem.SortOrder"/>.
    /// Each id is validated to reference a real row before insert, so a stale id is a silent skip
    /// rather than an FK failure.
    /// </summary>
    public static void AddItems(
        PaperbunkrDbContext context,
        int collectionId,
        IEnumerable<int>? seriesIds = null,
        IEnumerable<int>? issueIds = null,
        IEnumerable<int>? bookIds = null)
    {
        if (context.Collections.Find(collectionId) is null)
        {
            return;
        }

        var existing = context.CollectionItems.Where(ci => ci.CollectionId == collectionId).ToList();
        int nextOrder = existing.Count > 0 ? existing.Max(ci => ci.SortOrder) + 1 : 0;

        foreach (int seriesId in Distinct(seriesIds))
        {
            if (existing.Any(ci => ci.SeriesId == seriesId) || !context.Series.Any(s => s.Id == seriesId))
            {
                continue;
            }

            context.CollectionItems.Add(new CollectionItem { CollectionId = collectionId, SeriesId = seriesId, SortOrder = nextOrder++ });
        }

        foreach (int issueId in Distinct(issueIds))
        {
            if (existing.Any(ci => ci.IssueId == issueId) || !context.Issues.Any(i => i.Id == issueId))
            {
                continue;
            }

            context.CollectionItems.Add(new CollectionItem { CollectionId = collectionId, IssueId = issueId, SortOrder = nextOrder++ });
        }

        foreach (int bookId in Distinct(bookIds))
        {
            if (existing.Any(ci => ci.BookId == bookId) || !context.Books.Any(b => b.Id == bookId))
            {
                continue;
            }

            context.CollectionItems.Add(new CollectionItem { CollectionId = collectionId, BookId = bookId, SortOrder = nextOrder++ });
        }

        context.SaveChanges();
    }

    /// <summary>The context-menu toggle-off path: removes any items in the collection matching the given targets.</summary>
    public static void RemoveTargets(
        PaperbunkrDbContext context,
        int collectionId,
        IEnumerable<int>? seriesIds = null,
        IEnumerable<int>? issueIds = null,
        IEnumerable<int>? bookIds = null)
    {
        var s = Distinct(seriesIds).ToHashSet();
        var i = Distinct(issueIds).ToHashSet();
        var b = Distinct(bookIds).ToHashSet();

        var toRemove = context.CollectionItems
            .Where(ci => ci.CollectionId == collectionId)
            .AsEnumerable()
            .Where(ci => (ci.SeriesId is int sid && s.Contains(sid))
                      || (ci.IssueId is int iid && i.Contains(iid))
                      || (ci.BookId is int bid && b.Contains(bid)))
            .ToList();

        if (toRemove.Count == 0)
        {
            return;
        }

        context.CollectionItems.RemoveRange(toRemove);
        context.SaveChanges();
    }

    /// <summary>The overlay's per-row remove.</summary>
    public static void RemoveItem(PaperbunkrDbContext context, int collectionItemId)
    {
        var item = context.CollectionItems.Find(collectionItemId);
        if (item is null)
        {
            return;
        }

        context.CollectionItems.Remove(item);
        context.SaveChanges();
    }

    /// <summary>Assigns <see cref="CollectionItem.SortOrder"/> = position in the given list; items not listed keep their relative order after the listed ones.</summary>
    public static void ReorderItems(PaperbunkrDbContext context, int collectionId, IReadOnlyList<int> orderedCollectionItemIds)
    {
        var items = context.CollectionItems.Where(ci => ci.CollectionId == collectionId).ToList();
        int i = 0;
        foreach (int id in orderedCollectionItemIds)
        {
            var item = items.FirstOrDefault(x => x.Id == id);
            if (item is not null)
            {
                item.SortOrder = i++;
            }
        }

        foreach (var item in items.Where(x => !orderedCollectionItemIds.Contains(x.Id)).OrderBy(x => x.SortOrder))
        {
            item.SortOrder = i++;
        }

        context.SaveChanges();
    }

    private static IEnumerable<int> Distinct(IEnumerable<int>? ids) => (ids ?? Enumerable.Empty<int>()).Distinct();

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
