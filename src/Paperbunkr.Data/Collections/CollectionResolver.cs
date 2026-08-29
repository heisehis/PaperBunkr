using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Collections;

/// <summary>Which kind of row a <see cref="CollectionMember"/> points at.</summary>
public enum CollectionMemberKind
{
    Series,
    Issue,
    Book,
}

/// <summary>
/// One resolved member of a <see cref="Collection"/>, in the collection's manual order. Exactly one
/// of <see cref="Series"/>/<see cref="Issue"/>/<see cref="Book"/> is non-null, matching
/// <see cref="Kind"/>.
/// </summary>
public sealed record CollectionMember(
    int CollectionItemId,
    int SortOrder,
    CollectionMemberKind Kind,
    int TargetId,
    string DisplayTitle,
    Series? Series,
    Issue? Issue,
    Book? Book);

/// <summary>
/// Cover hint for a collection: the manual path when one is set and pinned, otherwise the identity
/// of the first member so the App layer can resolve the actual thumbnail through its own cover
/// cache (the Data layer has no comic-cover paths - only <see cref="Book.CoverImagePath"/>).
/// </summary>
public sealed record CollectionCoverHint(string? ManualPath, CollectionMember? FirstMember);

/// <summary>
/// Read-only queries for <see cref="Collection"/> membership
/// (docs/superpowers/specs/2026-08-27-collections-design.md). Mutation lives in
/// <see cref="CollectionService"/>; the "other series sharing a collection" query mirrors
/// <c>ContinuityResolver.GetOtherSeriesSharingContinuity</c>.
/// </summary>
public static class CollectionResolver
{
    /// <summary>Collections the given series belongs to, ordered by <see cref="Collection.SortOrder"/>.</summary>
    public static IReadOnlyList<Collection> GetCollections(PaperbunkrDbContext context, int seriesId)
    {
        return context.CollectionItems
            .Where(ci => ci.SeriesId == seriesId)
            .Select(ci => ci.Collection!)
            .OrderBy(c => c.SortOrder)
            .ToList();
    }

    /// <summary>Every member of a collection, resolved and ordered by <see cref="CollectionItem.SortOrder"/>.</summary>
    public static IReadOnlyList<CollectionMember> GetMembers(PaperbunkrDbContext context, int collectionId)
    {
        var items = context.CollectionItems
            .Where(ci => ci.CollectionId == collectionId)
            .Include(ci => ci.Series).ThenInclude(s => s!.Issues)
            .Include(ci => ci.Issue).ThenInclude(i => i!.Series)
            .Include(ci => ci.Book)
            .OrderBy(ci => ci.SortOrder)
            .ThenBy(ci => ci.Id)
            .ToList();

        var result = new List<CollectionMember>(items.Count);
        foreach (var ci in items)
        {
            if (ci.Series is not null)
            {
                result.Add(new CollectionMember(ci.Id, ci.SortOrder, CollectionMemberKind.Series, ci.Series.Id, ci.Series.Name, ci.Series, null, null));
            }
            else if (ci.Issue is not null)
            {
                string title = IssueDisplayTitle(ci.Issue);
                result.Add(new CollectionMember(ci.Id, ci.SortOrder, CollectionMemberKind.Issue, ci.Issue.Id, title, null, ci.Issue, null));
            }
            else if (ci.Book is not null)
            {
                result.Add(new CollectionMember(ci.Id, ci.SortOrder, CollectionMemberKind.Book, ci.Book.Id, ci.Book.Title, null, null, ci.Book));
            }
        }

        return result;
    }

    /// <summary>Other series that share at least one collection with <paramref name="seriesId"/> - deduplicated across collections, matching <c>ContinuityResolver.GetOtherSeriesSharingContinuity</c>.</summary>
    public static IReadOnlyList<Series> GetOtherSeriesSharingCollection(PaperbunkrDbContext context, int seriesId)
    {
        var collectionIds = context.CollectionItems
            .Where(ci => ci.SeriesId == seriesId)
            .Select(ci => ci.CollectionId)
            .Distinct()
            .ToList();

        if (collectionIds.Count == 0)
        {
            return new List<Series>();
        }

        return context.CollectionItems
            .Where(ci => collectionIds.Contains(ci.CollectionId) && ci.SeriesId != null && ci.SeriesId != seriesId)
            .Select(ci => ci.Series!)
            .Distinct()
            .OrderBy(s => s.Name)
            .ToList();
    }

    /// <summary>See <see cref="CollectionCoverHint"/>. Never touches the filesystem - the caller checks path validity.</summary>
    public static CollectionCoverHint GetCoverHint(PaperbunkrDbContext context, int collectionId)
    {
        var collection = context.Collections.Find(collectionId);
        string? manual = collection is { IsAutoCover: false } ? collection.CoverImagePath : null;
        var first = GetMembers(context, collectionId).FirstOrDefault();
        return new CollectionCoverHint(manual, first);
    }

    private static string IssueDisplayTitle(Issue issue)
    {
        string series = issue.Series?.Name ?? "Unknown series";
        string number = string.IsNullOrWhiteSpace(issue.Number) ? string.Empty : $" #{issue.Number}";
        return string.IsNullOrWhiteSpace(issue.Title) ? $"{series}{number}" : $"{series}{number} - {issue.Title}";
    }
}
