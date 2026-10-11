using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Notices when the reader finishes a whole continuity or story event (docs/superpowers/specs/2026-10-06-smart-features-design.md §7.4).
/// "Complete" means every issue in it is read (the 95% rule), placeholders aside: for a continuity, every issue of every member
/// series; for a story event, every member issue. Neither entity has a status of its own to consult, so completion is computed and
/// <c>CompletedNotifiedAt</c> remembers that it was announced - once, until the collection is found incomplete again.
/// </summary>
public static class CollectionCompletion
{
    /// <summary>
    /// Call after <paramref name="issueId"/> was read through. Returns the collections that just became complete (each now stamped, so
    /// the next call returns nothing for it); also clears the stamp of any collection containing this issue that is no longer complete.
    /// Saves its own changes. The caller raises the returned events.
    /// </summary>
    public static IReadOnlyList<CollectionCompletedEvent> OnIssueFinished(PaperbunkrDbContext context, int issueId, DateTime nowUtc)
    {
        var issue = context.Issues.AsNoTracking().FirstOrDefault(i => i.Id == issueId);
        if (issue is null)
        {
            return [];
        }

        var continuityIds = context.ContinuityMemberships.Where(m => m.SeriesId == issue.SeriesId).Select(m => m.ContinuityId).Distinct().ToList();
        var eventIds = context.EventMemberships.Where(m => m.IssueId == issueId).Select(m => m.StoryEventId).Distinct().ToList();
        // The finish is announced the moment the reader crosses the read threshold, which can be before the debounced position save has
        // written that page to the row - so this issue counts as read here whatever the row says yet.
        return Evaluate(context, continuityIds, eventIds, nowUtc, announce: true, justFinishedIssueId: issueId);
    }

    /// <summary>
    /// The re-arm pass, run after a library scan: any collection that was announced as complete but now has unread issues (new files
    /// arrived) has its stamp cleared, so finishing the new issues completes it again. Announces nothing. Returns how many were re-armed.
    /// </summary>
    public static int Rearm(PaperbunkrDbContext context)
    {
        var continuityIds = context.Continuities.Where(c => c.CompletedNotifiedAt != null).Select(c => c.Id).ToList();
        var eventIds = context.StoryEvents.Where(e => e.CompletedNotifiedAt != null).Select(e => e.Id).ToList();
        int before = continuityIds.Count + eventIds.Count;
        if (before == 0)
        {
            return 0;
        }

        Evaluate(context, continuityIds, eventIds, DateTime.UtcNow, announce: false);
        int after = context.Continuities.Count(c => c.CompletedNotifiedAt != null) + context.StoryEvents.Count(e => e.CompletedNotifiedAt != null);
        return before - after;
    }

    /// <summary>
    /// A series was just added to <paramref name="continuity"/>: if the reader has not finished that series, the continuity is no
    /// longer complete, so its stamp is cleared (the caller saves).
    /// </summary>
    public static void OnSeriesAdded(PaperbunkrDbContext context, Continuity continuity, int seriesId)
    {
        if (continuity.CompletedNotifiedAt is null)
        {
            return;
        }

        var issues = context.Issues.AsNoTracking().Where(i => i.SeriesId == seriesId && !i.IsPlaceholder).ToList();
        if (issues.Any(i => !i.HasBeenRead()))
        {
            continuity.CompletedNotifiedAt = null;
        }
    }

    private static IReadOnlyList<CollectionCompletedEvent> Evaluate(
        PaperbunkrDbContext context, IReadOnlyCollection<int> continuityIds, IReadOnlyCollection<int> eventIds, DateTime nowUtc, bool announce, int? justFinishedIssueId = null)
    {
        var completed = new List<CollectionCompletedEvent>();

        if (continuityIds.Count > 0)
        {
            var continuities = context.Continuities.Where(c => continuityIds.Contains(c.Id)).ToList();
            var members = context.ContinuityMemberships.Where(m => continuityIds.Contains(m.ContinuityId)).Select(m => new { m.ContinuityId, m.SeriesId }).ToList();
            var seriesIds = members.Select(m => m.SeriesId).Distinct().ToList();
            var issuesBySeries = context.Issues.AsNoTracking()
                .Where(i => seriesIds.Contains(i.SeriesId) && !i.IsPlaceholder)
                .ToList()
                .GroupBy(i => i.SeriesId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var continuity in continuities)
            {
                var issues = members.Where(m => m.ContinuityId == continuity.Id)
                    .SelectMany(m => issuesBySeries.TryGetValue(m.SeriesId, out var list) ? list : [])
                    .ToList();
                Apply(issues, continuity.CompletedNotifiedAt, stamp => continuity.CompletedNotifiedAt = stamp,
                    () => new CollectionCompletedEvent(CompletedCollectionKind.Continuity, continuity.Id, continuity.Name, issues.Count, nowUtc));
            }
        }

        if (eventIds.Count > 0)
        {
            var events = context.StoryEvents.Where(e => eventIds.Contains(e.Id)).ToList();
            var memberIssues = context.EventMemberships.AsNoTracking()
                .Where(m => eventIds.Contains(m.StoryEventId) && m.Issue != null && !m.Issue.IsPlaceholder)
                .Select(m => new { m.StoryEventId, Issue = m.Issue! })
                .ToList()
                .GroupBy(m => m.StoryEventId)
                .ToDictionary(g => g.Key, g => g.Select(m => m.Issue).ToList());

            foreach (var storyEvent in events)
            {
                var issues = memberIssues.TryGetValue(storyEvent.Id, out var list) ? list : [];
                Apply(issues, storyEvent.CompletedNotifiedAt, stamp => storyEvent.CompletedNotifiedAt = stamp,
                    () => new CollectionCompletedEvent(CompletedCollectionKind.StoryEvent, storyEvent.Id, storyEvent.Name, issues.Count, nowUtc));
            }
        }

        context.SaveChanges();
        return completed;

        void Apply(List<Issue> issues, DateTime? stamped, Action<DateTime?> setStamp, Func<CollectionCompletedEvent> makeEvent)
        {
            // An empty collection is not "complete": there was nothing to finish.
            bool complete = issues.Count > 0 && issues.All(i => i.Id == justFinishedIssueId || i.HasBeenRead());
            if (complete && stamped is null && announce)
            {
                setStamp(nowUtc);
                completed.Add(makeEvent());
            }
            else if (!complete && stamped is not null)
            {
                setStamp(null);
            }
        }
    }
}
