using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Collections;

/// <summary>Which kind of row a <see cref="CollectionMember"/> points at.</summary>
public enum CollectionMemberKind
{
    Series,
    Issue,
    Book,
}

/// <summary>
/// One resolved member of a <see cref="Collection"/>. Exactly one of <see cref="Series"/>/
/// <see cref="Issue"/>/<see cref="Book"/> is non-null, matching <see cref="Kind"/>.
///
/// <see cref="CollectionItemId"/> is <see langword="null"/> for a member present only because it
/// matches one of the collection's rule slots (docs/superpowers/specs/2026-08-30-smart-collections-
/// design.md) - there's no backing <see cref="CollectionItem"/> row to remove or reorder. A
/// rule-matched member's <see cref="SortOrder"/> is a fixed sentinel (<see cref="int.MaxValue"/>),
/// not a meaningful position - callers ordering a mixed manual+rule-matched list apply each kind's
/// own default sort to the rule-matched tail instead of trusting this value.
/// </summary>
public sealed record CollectionMember(
    int? CollectionItemId,
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

    /// <summary>
    /// Every member of a collection: manual <see cref="CollectionItem"/> rows (ordered by
    /// <see cref="CollectionItem.SortOrder"/>, as before) plus, for each rule slot the collection has
    /// set (docs/superpowers/specs/2026-08-30-smart-collections-design.md), the live match set of
    /// that rule - skipping any target already present as a manual member of that kind (dedup) -
    /// appended after, grouped by kind in <see cref="CollectionMemberKind"/> declaration order
    /// (Series, Issue, Book) for stable output. A collection with no rule slots set behaves exactly
    /// as before this feature existed.
    /// </summary>
    public static IReadOnlyList<CollectionMember> GetMembers(PaperbunkrDbContext context, int collectionId)
    {
        var collection = context.Collections.Find(collectionId);
        if (collection is null)
        {
            return new List<CollectionMember>();
        }

        var items = context.CollectionItems
            .Where(ci => ci.CollectionId == collectionId)
            .Include(ci => ci.Series).ThenInclude(s => s!.Issues)
            .Include(ci => ci.Issue).ThenInclude(i => i!.Series)
            .Include(ci => ci.Book)
            .OrderBy(ci => ci.SortOrder)
            .ThenBy(ci => ci.Id)
            .ToList();

        var result = new List<CollectionMember>(items.Count);
        var manualSeriesIds = new HashSet<int>();
        var manualIssueIds = new HashSet<int>();
        var manualBookIds = new HashSet<int>();

        foreach (var ci in items)
        {
            if (ci.Series is not null)
            {
                result.Add(new CollectionMember(ci.Id, ci.SortOrder, CollectionMemberKind.Series, ci.Series.Id, ci.Series.Name, ci.Series, null, null));
                manualSeriesIds.Add(ci.Series.Id);
            }
            else if (ci.Issue is not null)
            {
                string title = IssueDisplayTitle(ci.Issue);
                result.Add(new CollectionMember(ci.Id, ci.SortOrder, CollectionMemberKind.Issue, ci.Issue.Id, title, null, ci.Issue, null));
                manualIssueIds.Add(ci.Issue.Id);
            }
            else if (ci.Book is not null)
            {
                result.Add(new CollectionMember(ci.Id, ci.SortOrder, CollectionMemberKind.Book, ci.Book.Id, ci.Book.Title, null, null, ci.Book));
                manualBookIds.Add(ci.Book.Id);
            }
        }

        if (collection.SeriesSmartListId is int seriesSmartListId
            && SmartListTreeLoader.LoadWithTree(context, seriesSmartListId) is { } seriesList)
        {
            foreach (var series in SeriesSmartListQueryBuilder.Build(context, seriesList))
            {
                if (manualSeriesIds.Add(series.Id))
                {
                    result.Add(new CollectionMember(null, int.MaxValue, CollectionMemberKind.Series, series.Id, series.Name, series, null, null));
                }
            }
        }

        if (collection.IssueSmartListId is int issueSmartListId
            && SmartListTreeLoader.LoadWithTree(context, issueSmartListId) is { } issueList)
        {
            foreach (var issue in SmartListQueryBuilder.Build(context, issueList))
            {
                if (manualIssueIds.Add(issue.Id))
                {
                    result.Add(new CollectionMember(null, int.MaxValue, CollectionMemberKind.Issue, issue.Id, IssueDisplayTitle(issue), null, issue, null));
                }
            }
        }

        if (collection.NovelSmartListId is int novelSmartListId
            && SmartListTreeLoader.LoadWithTree(context, novelSmartListId) is { } novelList)
        {
            foreach (var book in NovelSmartListQueryBuilder.Build(context, novelList))
            {
                if (manualBookIds.Add(book.Id))
                {
                    result.Add(new CollectionMember(null, int.MaxValue, CollectionMemberKind.Book, book.Id, book.Title, null, null, book));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Other series that share at least one collection with <paramref name="seriesId"/> -
    /// deduplicated across collections, matching <c>ContinuityResolver.GetOtherSeriesSharingContinuity</c>.
    /// Goes through <see cref="GetMembers"/> per candidate collection (rather than querying
    /// <see cref="CollectionItem"/> directly) so a collection where <paramref name="seriesId"/> is
    /// present only via a <see cref="Collection.SeriesSmartListId"/> rule still counts - collections
    /// are small and curated (docs/superpowers/specs/2026-08-27-collections-design.md), so this
    /// isn't the library-scale query <see cref="GetMembers"/>'s own doc comments guard against.
    /// </summary>
    public static IReadOnlyList<Series> GetOtherSeriesSharingCollection(PaperbunkrDbContext context, int seriesId)
    {
        var result = new List<Series>();
        var seen = new HashSet<int> { seriesId };

        foreach (var collection in context.Collections.ToList())
        {
            var members = GetMembers(context, collection.Id);
            if (!members.Any(m => m.Kind == CollectionMemberKind.Series && m.TargetId == seriesId))
            {
                continue;
            }

            foreach (var member in members.Where(m => m.Kind == CollectionMemberKind.Series))
            {
                if (seen.Add(member.TargetId))
                {
                    result.Add(member.Series!);
                }
            }
        }

        return result.OrderBy(s => s.Name).ToList();
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
