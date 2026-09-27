using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read-only query/compute layer for the Insights screen's reading-goal cards (docs/superpowers/specs/
/// 2026-09-23-insights-reading-goals-design.md). Same pure-function shape as <see cref="StatsResolver"/>/
/// <see cref="RecapResolver"/>: a function of (context, now), no persistence, no caching of its own -
/// <see cref="ReadingGoal"/> itself is the persisted state, this class only computes progress against it.
/// </summary>
public static class GoalResolver
{
    public static IReadOnlyList<GoalProgress> Build(PaperbunkrDbContext context, DateTime nowUtc)
    {
        var goals = context.ReadingGoals.AsNoTracking().ToList();
        if (goals.Count == 0)
        {
            return Array.Empty<GoalProgress>();
        }

        var events = context.ReadingEvents.AsNoTracking()
            .Where(e => e.Kind == ReadingEventKind.Finished)
            .ToList();

        var today = DateOnly.FromDateTime(nowUtc.ToLocalTime().Date);
        return goals.Select(g => BuildProgress(g, events, today)).ToList();
    }

    private static GoalProgress BuildProgress(ReadingGoal goal, List<ReadingEvent> finishedEvents, DateOnly today)
    {
        var matched = finishedEvents.Where(e =>
        {
            var day = DateOnly.FromDateTime(e.TimestampUtc.ToLocalTime().Date);
            if (day < goal.PeriodStart || day > goal.PeriodEnd)
            {
                return false;
            }

            return goal.ScopeKind switch
            {
                GoalScopeKind.Series => goal.ScopeValue is not null && int.TryParse(goal.ScopeValue, out int sid) && e.SeriesId == sid,
                GoalScopeKind.Publisher => e.Publisher == goal.ScopeValue,
                GoalScopeKind.Genre => e.PrimaryGenre == goal.ScopeValue,
                _ => true, // Library
            };
        }).ToList();

        long currentValue = goal.Metric == GoalMetric.Items
            ? matched.Count
            : matched.Sum(e => (long)(e.PagesRead ?? 0));

        bool isComplete = currentValue >= goal.Target;

        GoalPaceState paceState = GoalPaceState.NotApplicable;
        long behindAmount = 0;

        if (!isComplete && goal.PeriodKind != GoalPeriodKind.Custom)
        {
            int totalDays = goal.PeriodEnd.DayNumber - goal.PeriodStart.DayNumber;
            double elapsedFraction = totalDays <= 0
                ? 1.0
                : Math.Clamp((today.DayNumber - goal.PeriodStart.DayNumber) / (double)totalDays, 0.0, 1.0);
            double expected = goal.Target * elapsedFraction;

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

        return new GoalProgress(goal, currentValue, isComplete, paceState, behindAmount);
    }
}

public sealed record GoalProgress(ReadingGoal Goal, long CurrentValue, bool IsComplete, GoalPaceState PaceState, long BehindAmount);

public enum GoalPaceState
{
    OnTrack,
    Behind,
    NotApplicable,
}
