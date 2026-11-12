using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Collections;

/// <summary>
/// The one display order for collection lists (docs/superpowers/specs/2026-11-06-collection-identity-design.md): pinned first, then the rest,
/// each group in <see cref="Collection.SortOrder"/> order. Every list (Library sidebar, Books, Home, Show in list) uses it so they agree.
/// </summary>
public static class CollectionOrdering
{
    public static IOrderedQueryable<Collection> Apply(IQueryable<Collection> collections) =>
        collections.OrderByDescending(c => c.IsPinned).ThenBy(c => c.SortOrder).ThenBy(c => c.Id);

    public static IOrderedEnumerable<Collection> Sorted(IEnumerable<Collection> collections) =>
        collections.OrderByDescending(c => c.IsPinned).ThenBy(c => c.SortOrder).ThenBy(c => c.Id);
}
