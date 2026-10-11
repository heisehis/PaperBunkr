using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read-only query/compute layer for the Insights screen's reading-goal cards (docs/superpowers/specs/
/// 2026-09-23-insights-reading-goals-design.md, extended by 2026-10-04-insights-goal-outcomes-and-chart-colour-design.md and
/// 2026-10-04-insights-goal-scopes-design.md). Same pure-function shape as <see cref="StatsResolver"/>/
/// <see cref="RecapResolver"/>: a function of (context, now), no persistence, no caching of its own -
/// <see cref="ReadingGoal"/> itself is the persisted state, this class only computes progress against it.
///
/// A goal's scope filters combine with AND. Series / Publisher / Genre / MediaType are <em>predicates</em> on a finish event's frozen columns;
/// Reading list / Collection / Story event / Continuity / Creator are <em>memberships</em>, looked up live per <see cref="Build"/> and expanded to a set
/// of issue and book ids (the goal's "universe"; several memberships intersect). Finish goals use memberships only.
/// </summary>
public static class GoalResolver
{
    public static IReadOnlyList<GoalProgress> Build(PaperbunkrDbContext context, DateTime nowUtc)
    {
        var goals = context.ReadingGoals.AsNoTracking().Include(g => g.Scopes).ToList();
        if (goals.Count == 0)
        {
            return Array.Empty<GoalProgress>();
        }

        var events = context.ReadingEvents.AsNoTracking()
            .Where(e => e.Kind == ReadingEventKind.Finished)
            .ToList();

        var today = DateOnly.FromDateTime(nowUtc.ToLocalTime().Date);
        var lookup = new MembershipLookup(context);
        return goals.Select(g => BuildProgress(g, events, today, lookup)).ToList();
    }

    /// <summary>The filters a goal applies: its scope rows, or - on a row that predates them - the legacy single scope.</summary>
    public static IReadOnlyList<(GoalScopeKind Kind, string? Value, string? Label)> FiltersOf(ReadingGoal goal)
    {
        if (goal.Scopes.Count > 0)
        {
            return goal.Scopes.Select(s => (s.Kind, s.Value, s.Label ?? s.Value)).ToList();
        }

        return goal.ScopeKind == GoalScopeKind.Library
            ? Array.Empty<(GoalScopeKind, string?, string?)>()
            : new[] { (goal.ScopeKind, goal.ScopeValue, goal.ScopeValue) };
    }

    public static bool IsMembership(GoalScopeKind kind)
        => kind is GoalScopeKind.ReadingList or GoalScopeKind.Collection or GoalScopeKind.StoryEvent
            or GoalScopeKind.Continuity or GoalScopeKind.Creator;

    private static GoalProgress BuildProgress(ReadingGoal goal, List<ReadingEvent> finishedEvents, DateOnly today, MembershipLookup lookup)
    {
        bool finish = goal.Kind == GoalKind.Finish;
        var filters = FiltersOf(goal);

        // Memberships -> the universe (an intersection); a membership whose entity is gone marks the goal ScopeMissing.
        ItemSet? universe = null;
        string? missingLabel = null;
        foreach (var (kind, value, label) in filters.Where(f => IsMembership(f.Kind)))
        {
            var set = lookup.Resolve(kind, value);
            if (set is null)
            {
                missingLabel ??= label ?? kind.ToString();
                continue;
            }

            universe = universe is null ? set : universe.Intersect(set);
        }

        bool scopeMissing = missingLabel is not null;
        if (scopeMissing)
        {
            universe = ItemSet.Empty; // a goal whose list is gone counts nothing, rather than silently widening to the whole library
        }

        // Finish goals use memberships only; the predicate scopes (series/publisher/genre/media type) apply to count goals.
        var predicates = finish ? new List<(GoalScopeKind Kind, string? Value, string? Label)>() : filters.Where(f => !IsMembership(f.Kind)).ToList();

        bool Matches(ReadingEvent e)
        {
            if (universe is not null && !universe.Contains(e))
            {
                return false;
            }

            foreach (var (kind, value, _) in predicates)
            {
                bool ok = kind switch
                {
                    GoalScopeKind.Series => int.TryParse(value, out int sid) && e.SeriesId == sid,
                    GoalScopeKind.Publisher => e.Publisher == value,
                    GoalScopeKind.Genre => e.PrimaryGenre == value,
                    GoalScopeKind.MediaType => lookup.MediaTypeOf(e) == value,
                    _ => true,
                };
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        long currentValue;
        long target;
        DateOnly? completedOn = null;

        if (finish)
        {
            // Distinct universe items finished at any time (earlier reading counts, a re-read does not double); the target is the universe's size, live.
            var firstFinishes = finishedEvents.Where(Matches)
                .GroupBy(e => (e.ItemType, e.ItemId))
                .Select(g => g.OrderBy(e => e.TimestampUtc).First())
                .OrderBy(e => e.TimestampUtc)
                .ToList();
            currentValue = firstFinishes.Count;
            target = universe?.Count ?? 0;
            if (!scopeMissing && target > 0 && currentValue >= target)
            {
                completedOn = DateOnly.FromDateTime(firstFinishes[(int)target - 1].TimestampUtc.ToLocalTime().Date);
            }
        }
        else
        {
            var matched = finishedEvents.Where(e =>
            {
                var day = DateOnly.FromDateTime(e.TimestampUtc.ToLocalTime().Date);
                return day >= goal.PeriodStart && day <= goal.PeriodEnd && Matches(e);
            }).ToList();

            if (goal.DistinctOnly)
            {
                matched = matched.GroupBy(e => (e.ItemType, e.ItemId)).Select(g => g.OrderBy(e => e.TimestampUtc).First()).ToList();
            }

            currentValue = goal.Metric == GoalMetric.Items
                ? matched.Count
                : matched.Sum(e => (long)(e.PagesRead ?? 0));
            target = goal.Target;

            if (!scopeMissing && currentValue >= target)
            {
                long running = 0;
                foreach (var e in matched.OrderBy(e => e.TimestampUtc))
                {
                    running += goal.Metric == GoalMetric.Items ? 1 : e.PagesRead ?? 0;
                    if (running >= target)
                    {
                        completedOn = DateOnly.FromDateTime(e.TimestampUtc.ToLocalTime().Date);
                        break;
                    }
                }
            }
        }

        // A zero target is never "done": an empty list, or a goal whose scope no longer exists, must not read Completed.
        bool isComplete = !scopeMissing && target > 0 && currentValue >= target;

        // Missed = the period is over and the target wasn't reached. Derived (never stored): ReadingEvent is
        // append-only, so the answer cannot drift (docs/superpowers/specs/2026-10-04-insights-goal-outcomes-and-chart-colour-design.md).
        var outcome = isComplete ? GoalOutcome.Completed
            : today > goal.PeriodEnd ? GoalOutcome.Missed
            : GoalOutcome.Active;
        if (!isComplete)
        {
            completedOn = null;
        }

        GoalPaceState paceState = GoalPaceState.NotApplicable;
        long behindAmount = 0;

        bool pacing = !finish && goal.PeriodKind is GoalPeriodKind.ThisYear or GoalPeriodKind.ThisMonth;
        if (outcome == GoalOutcome.Active && pacing && !scopeMissing)
        {
            int totalDays = goal.PeriodEnd.DayNumber - goal.PeriodStart.DayNumber;
            double elapsedFraction = totalDays <= 0
                ? 1.0
                : Math.Clamp((today.DayNumber - goal.PeriodStart.DayNumber) / (double)totalDays, 0.0, 1.0);
            double expected = target * elapsedFraction;

            if (currentValue < expected)
            {
                paceState = GoalPaceState.Behind;
                behindAmount = (long)Math.Ceiling(expected - currentValue);
            }
            else
            {
                paceState = GoalPaceState.OnTrack;
            }
        }

        return new GoalProgress(goal, currentValue, isComplete, paceState, behindAmount, outcome, completedOn, target, missingLabel);
    }

    /// <summary>Issue and book ids a membership scope covers.</summary>
    internal sealed class ItemSet
    {
        public static readonly ItemSet Empty = new(new HashSet<int>(), new HashSet<int>());

        public ItemSet(HashSet<int> issues, HashSet<int> books)
        {
            Issues = issues;
            Books = books;
        }

        public HashSet<int> Issues { get; }

        public HashSet<int> Books { get; }

        public int Count => Issues.Count + Books.Count;

        public bool Contains(ReadingEvent e)
            => e.ItemType == ReadingItemType.Comic ? Issues.Contains(e.ItemId) : Books.Contains(e.ItemId);

        public ItemSet Intersect(ItemSet other)
            => new(Issues.Where(other.Issues.Contains).ToHashSet(), Books.Where(other.Books.Contains).ToHashSet());
    }

    /// <summary>Per-<see cref="Build"/> membership queries, cached by (kind, value) so goals sharing a list pay for it once.</summary>
    private sealed class MembershipLookup
    {
        private readonly PaperbunkrDbContext _context;
        private readonly Dictionary<(GoalScopeKind, string?), ItemSet?> _sets = new();
        private Dictionary<int, ContentType>? _seriesContentTypes;

        public MembershipLookup(PaperbunkrDbContext context) => _context = context;

        /// <summary>The "Comic" / "Manga" / ... / "Novel" a finished event belongs to.</summary>
        public string MediaTypeOf(ReadingEvent e)
        {
            if (e.ItemType == ReadingItemType.Novel)
            {
                return "Novel";
            }

            _seriesContentTypes ??= _context.Series.AsNoTracking().Select(s => new { s.Id, s.ContentType }).ToDictionary(s => s.Id, s => s.ContentType);
            return e.SeriesId is { } id && _seriesContentTypes.TryGetValue(id, out var type) ? type.ToString() : ContentType.Unknown.ToString();
        }

        /// <summary>The set a membership scope covers, or null when the entity it points at no longer exists.</summary>
        public ItemSet? Resolve(GoalScopeKind kind, string? value)
        {
            if (_sets.TryGetValue((kind, value), out var cached))
            {
                return cached;
            }

            var set = Query(kind, value);
            _sets[(kind, value)] = set;
            return set;
        }

        private ItemSet? Query(GoalScopeKind kind, string? value)
        {
            if (!int.TryParse(value, out int id))
            {
                return null;
            }

            switch (kind)
            {
                case GoalScopeKind.ReadingList:
                    return _context.ReadingLists.AsNoTracking().Any(l => l.Id == id)
                        ? Issues(_context.ReadingListItems.AsNoTracking().Where(i => i.ReadingListId == id).Select(i => i.IssueId))
                        : null;

                case GoalScopeKind.StoryEvent:
                    return _context.StoryEvents.AsNoTracking().Any(e => e.Id == id)
                        ? Issues(_context.EventMemberships.AsNoTracking().Where(m => m.StoryEventId == id).Select(m => m.IssueId))
                        : null;

                case GoalScopeKind.Creator:
                    return _context.Creators.AsNoTracking().Any(c => c.Id == id)
                        ? Issues(_context.CreatorCredits.AsNoTracking().Where(c => c.CreatorId == id).Select(c => c.IssueId))
                        : null;

                case GoalScopeKind.Continuity:
                    if (!_context.Continuities.AsNoTracking().Any(c => c.Id == id))
                    {
                        return null;
                    }

                    return new ItemSet(
                        IssuesOfSeries(_context.ContinuityMemberships.AsNoTracking().Where(m => m.ContinuityId == id).Select(m => m.SeriesId).ToList()),
                        new HashSet<int>());

                case GoalScopeKind.Collection:
                    if (!_context.Collections.AsNoTracking().Any(c => c.Id == id))
                    {
                        return null;
                    }

                    var items = _context.CollectionItems.AsNoTracking().Where(i => i.CollectionId == id).ToList();
                    var issues = items.Where(i => i.IssueId is not null).Select(i => i.IssueId!.Value).ToHashSet();
                    issues.UnionWith(IssuesOfSeries(items.Where(i => i.SeriesId is not null).Select(i => i.SeriesId!.Value).ToList()));
                    return new ItemSet(issues, items.Where(i => i.BookId is not null).Select(i => i.BookId!.Value).ToHashSet());

                default:
                    return null;
            }
        }

        private static ItemSet Issues(IQueryable<int> ids) => new(ids.ToHashSet(), new HashSet<int>());

        private HashSet<int> IssuesOfSeries(List<int> seriesIds)
            => seriesIds.Count == 0
                ? new HashSet<int>()
                : _context.Issues.AsNoTracking().Where(i => seriesIds.Contains(i.SeriesId)).Select(i => i.Id).ToHashSet();
    }
}

/// <summary>Active until the period ends; Completed once the target is met; Missed when the period ended below target (any period kind).</summary>
public enum GoalOutcome
{
    Active,
    Completed,
    Missed,
}

/// <param name="EffectiveTarget">What the goal is measured against: <c>Goal.Target</c> for a count goal, the live size of the scope for a finish goal.
/// Every display uses this rather than <c>Goal.Target</c>.</param>
/// <param name="MissingScopeLabel">Set when a list / collection / event / continuity / creator the goal points at no longer exists (the goal then counts nothing).</param>
public sealed record GoalProgress(ReadingGoal Goal, long CurrentValue, bool IsComplete, GoalPaceState PaceState, long BehindAmount,
    GoalOutcome Outcome = GoalOutcome.Active, DateOnly? CompletedOn = null, long EffectiveTarget = 0, string? MissingScopeLabel = null)
{
    public bool ScopeMissing => MissingScopeLabel is not null;
}

public enum GoalPaceState
{
    OnTrack,
    Behind,
    NotApplicable,
}
