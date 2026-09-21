using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Reader;

/// <summary>The issue reached by stepping forward/backward from the current one, plus where it sits (1-based) in the order used.</summary>
public sealed record ReadingOrderStep(Issue From, Issue To, int ToPosition, int Total, string SourceLabel);

/// <summary>Where an issue sits in a reading list or Story Event, with that context's own previous/next (missing files skipped).</summary>
public sealed record ReadingContext(ReadingContextKind Kind, string Label, int Position, int Total, int? PrevIssueId, int? NextIssueId);

public enum ReadingContextKind
{
    ReadingList,
    StoryEvent,
}

/// <summary>
/// One place that answers "what comes before/after this issue, and where does it sit" for the comic
/// reader (docs/superpowers/specs/2026-09-21-comic-reader-flow-and-defaults-design.md §1). Replaces the
/// reader VM's private adjacent-issue query. Paging order follows the reading list the reader was
/// opened from, else series order - it never silently switches to Event order. <see cref="ResolveContext"/>
/// is the separate, display-only lookup behind the context strip. Continuity is deliberately not used:
/// it is series-level (<see cref="ContinuityMembership"/>) and defines no issue order.
/// </summary>
public static class ReadingOrderResolver
{
    /// <summary>
    /// Step to the next (<paramref name="forward"/>) or previous issue. With a <paramref name="readingListId"/>
    /// this walks that list's <c>SortOrder</c> across series, skipping missing files and stopping at the
    /// list boundary with no fallback to series order; otherwise it walks the series by issue number.
    /// </summary>
    public static ReadingOrderStep? ResolveNeighbour(PaperbunkrDbContext context, int issueId, int? seriesId, int? readingListId, bool forward)
    {
        int step = forward ? 1 : -1;

        if (readingListId is int listId)
        {
            var items = context.ReadingListItems
                .Where(i => i.ReadingListId == listId)
                .Include(i => i.Issue).ThenInclude(i => i!.Series)
                .Include(i => i.Issue).ThenInclude(i => i!.MetadataProposals)
                .OrderBy(i => i.SortOrder)
                .ToList();
            int listIndex = items.FindIndex(i => i.IssueId == issueId);
            if (listIndex < 0)
            {
                return null;
            }

            for (int i = listIndex + step; i >= 0 && i < items.Count; i += step)
            {
                if (items[i].Issue is { FileIsMissing: false, Series: not null } candidate)
                {
                    string name = context.ReadingLists.Where(l => l.Id == listId).Select(l => l.Name).FirstOrDefault() ?? "Reading list";
                    return new ReadingOrderStep(items[listIndex].Issue!, candidate, i + 1, items.Count, $"Reading list: {name}");
                }
            }

            return null;
        }

        if (seriesId is not int sid)
        {
            return null;
        }

        var series = context.Series.Include(s => s.Issues).ThenInclude(i => i.MetadataProposals).FirstOrDefault(s => s.Id == sid);
        var ordered = series?.Issues.OrderByNumber().ToList();
        int seriesIndex = ordered?.FindIndex(i => i.Id == issueId) ?? -1;
        if (series is null || ordered is null || seriesIndex < 0)
        {
            return null;
        }

        int adjacent = seriesIndex + step;
        if (adjacent < 0 || adjacent >= ordered.Count)
        {
            return null;
        }

        var to = ordered[adjacent];
        to.Series ??= series;
        return new ReadingOrderStep(ordered[seriesIndex], to, adjacent + 1, ordered.Count, $"Series: {series.Name}");
    }

    /// <summary>
    /// The context the strip describes. The reading list the reader was opened from wins; otherwise
    /// the Story Event the issue belongs to (lowest event id if several); otherwise none.
    /// </summary>
    public static ReadingContext? ResolveContext(PaperbunkrDbContext context, int issueId, int? readingListId)
    {
        if (readingListId is int listId)
        {
            var items = context.ReadingListItems
                .Where(i => i.ReadingListId == listId)
                .Include(i => i.Issue)
                .OrderBy(i => i.SortOrder)
                .ToList();
            string? name = context.ReadingLists.Where(l => l.Id == listId).Select(l => l.Name).FirstOrDefault();
            return Build(ReadingContextKind.ReadingList, name ?? "Reading list", items.Select(i => (i.IssueId, i.Issue)).ToList(), issueId);
        }

        var membership = context.EventMemberships
            .Where(m => m.IssueId == issueId)
            .OrderBy(m => m.StoryEventId)
            .FirstOrDefault();
        if (membership is null)
        {
            return null;
        }

        var members = context.EventMemberships
            .Where(m => m.StoryEventId == membership.StoryEventId)
            .Include(m => m.Issue)
            .OrderBy(m => m.Position)
            .ToList();
        string eventName = context.StoryEvents.Where(e => e.Id == membership.StoryEventId).Select(e => e.Name).FirstOrDefault() ?? "Event";
        return Build(ReadingContextKind.StoryEvent, eventName, members.Select(m => (m.IssueId, m.Issue)).ToList(), issueId);
    }

    private static ReadingContext? Build(ReadingContextKind kind, string label, List<(int IssueId, Issue? Issue)> ordered, int issueId)
    {
        int index = ordered.FindIndex(m => m.IssueId == issueId);
        if (index < 0)
        {
            return null;
        }

        return new ReadingContext(kind, label, index + 1, ordered.Count, Adjacent(ordered, index, -1), Adjacent(ordered, index, 1));
    }

    private static int? Adjacent(List<(int IssueId, Issue? Issue)> ordered, int from, int step)
    {
        for (int i = from + step; i >= 0 && i < ordered.Count; i += step)
        {
            if (ordered[i].Issue is { FileIsMissing: false })
            {
                return ordered[i].IssueId;
            }
        }

        return null;
    }
}
