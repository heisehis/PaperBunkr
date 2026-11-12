using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Collections;

/// <summary>Read units out of total units for one collection.</summary>
public readonly record struct CollectionProgress(int Read, int Total)
{
    /// <summary>0..1; 0 when there are no units.</summary>
    public double Fraction => Total <= 0 ? 0 : System.Math.Clamp(Read / (double)Total, 0, 1);
}

/// <summary>
/// Read progress per collection for the opt-in sidebar / Home ring and the Library banner (docs/superpowers/specs/2026-11-06-collection-identity-design.md,
/// ...-collection-surfaces-design.md). A series member counts its issues (read = the same "started" test the series cards use: <c>LastPageRead &gt; 0</c>), a
/// standalone issue counts 1, a book counts 1 when finished. Rule-matched members are included, but only collections that actually have a rule slot go through
/// <see cref="CollectionResolver.GetMembers"/>; plain ones are the manual items. The per-series totals are one grouped query limited to the series involved
/// (the whole table only when more than <see cref="SeriesFilterLimit"/> series are involved).
/// </summary>
public static class CollectionProgressResolver
{
    private const int SeriesFilterLimit = 500;

    public static IReadOnlyDictionary<int, CollectionProgress> GetProgress(PaperbunkrDbContext context, IReadOnlyCollection<int> collectionIds)
    {
        var result = new Dictionary<int, CollectionProgress>();
        if (collectionIds.Count == 0)
        {
            return result;
        }

        var ids = collectionIds.ToList();
        var smart = context.Collections
            .Where(c => ids.Contains(c.Id) && (c.IssueSmartListId != null || c.SeriesSmartListId != null || c.NovelSmartListId != null))
            .Select(c => c.Id)
            .ToHashSet();

        var plainItems = context.CollectionItems
            .Where(ci => ids.Contains(ci.CollectionId) && !smart.Contains(ci.CollectionId))
            .Select(ci => new
            {
                ci.CollectionId,
                ci.SeriesId,
                IssueRead = ci.IssueId != null && ci.Issue!.LastPageRead != null && ci.Issue.LastPageRead > 0,
                IsIssue = ci.IssueId != null,
                IsBook = ci.BookId != null,
                BookFinished = ci.BookId != null && ci.Book!.Status == BookReadingStatus.Finished,
            })
            .ToList();

        var smartMembers = smart.ToDictionary(id => id, id => CollectionResolver.GetMembers(context, id));

        var neededSeries = plainItems.Where(i => i.SeriesId is not null).Select(i => i.SeriesId!.Value)
            .Concat(smartMembers.Values.SelectMany(m => m).Where(m => m.Kind == CollectionMemberKind.Series).Select(m => m.TargetId))
            .ToHashSet();

        var issues = neededSeries.Count == 0
            ? new Dictionary<int, CollectionProgress>()
            : TotalsFor(context, neededSeries);

        foreach (var group in plainItems.GroupBy(i => i.CollectionId))
        {
            int read = 0, total = 0;
            foreach (var item in group)
            {
                if (item.SeriesId is int seriesId)
                {
                    if (issues.TryGetValue(seriesId, out var s))
                    {
                        read += s.Read;
                        total += s.Total;
                    }
                }
                else if (item.IsIssue)
                {
                    total++;
                    read += item.IssueRead ? 1 : 0;
                }
                else if (item.IsBook)
                {
                    total++;
                    read += item.BookFinished ? 1 : 0;
                }
            }

            if (total > 0)
            {
                result[group.Key] = new CollectionProgress(read, total);
            }
        }

        foreach (var (collectionId, members) in smartMembers)
        {
            int read = 0, total = 0;
            foreach (var member in members)
            {
                switch (member.Kind)
                {
                    case CollectionMemberKind.Series when member.Series is { } series:
                        if (issues.TryGetValue(series.Id, out var s))
                        {
                            read += s.Read;
                            total += s.Total;
                        }

                        break;
                    case CollectionMemberKind.Issue when member.Issue is { } issue:
                        total++;
                        read += issue.LastPageRead is > 0 ? 1 : 0;
                        break;
                    case CollectionMemberKind.Book when member.Book is { } book:
                        total++;
                        read += book.Status == BookReadingStatus.Finished ? 1 : 0;
                        break;
                }
            }

            if (total > 0)
            {
                result[collectionId] = new CollectionProgress(read, total);
            }
        }

        return result;
    }

    private static Dictionary<int, CollectionProgress> TotalsFor(PaperbunkrDbContext context, HashSet<int> seriesIds)
    {
        var query = seriesIds.Count <= SeriesFilterLimit
            ? context.Issues.Where(i => seriesIds.Contains(i.SeriesId))
            : context.Issues;

        return query
            .GroupBy(i => i.SeriesId)
            .Select(g => new { SeriesId = g.Key, Total = g.Count(), Read = g.Count(i => i.LastPageRead != null && i.LastPageRead > 0) })
            .ToDictionary(x => x.SeriesId, x => new CollectionProgress(x.Read, x.Total));
    }
}
