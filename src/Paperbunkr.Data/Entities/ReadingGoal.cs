namespace Paperbunkr.Data.Entities;

/// <summary>
/// A user-defined reading target (docs/superpowers/specs/2026-09-23-insights-reading-goals-design.md) -
/// "50 issues this year", "3,000 pages a month", optionally scoped to a series/publisher/genre. Unlike
/// every other Insights-family record (<see cref="ReadingEvent"/>, <see cref="LibrarySnapshot"/>), this is
/// user-authored state, not a derived observation - created/deleted through the goal editor overlay, never
/// written by a background task.
///
/// <see cref="PeriodStart"/>/<see cref="PeriodEnd"/> are resolved once at creation time, even for the
/// <see cref="GoalPeriodKind.ThisYear"/>/<see cref="GoalPeriodKind.ThisMonth"/> presets - a "This year" goal
/// created in September still spans Jan 1-Dec 31 of the creation year. There is no auto-renewal: once
/// <see cref="PeriodEnd"/> passes, the row simply stops being "current" and stays visible as history: the
/// user creates a fresh goal for the next period themselves.
/// </summary>
public class ReadingGoal
{
    public int Id { get; set; }

    /// <summary>Short user-facing label, auto-suggested at creation but freely editable.</summary>
    public string Title { get; set; } = string.Empty;

    public GoalMetric Metric { get; set; }

    public long Target { get; set; }

    public GoalPeriodKind PeriodKind { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    /// <summary>The goal's single scope before combined scopes existed (docs/superpowers/specs/2026-10-04-insights-goal-scopes-design.md).
    /// Kept for old rows and never dropped (the project's orphan-column rule): the migration copies a non-Library value into a
    /// <see cref="ReadingGoalScope"/> row, and <c>GoalResolver</c> only reads this pair when a goal has no such rows. New goals leave it Library.</summary>
    public GoalScopeKind ScopeKind { get; set; }

    /// <summary>Series id (as a string), publisher name, or genre tag depending on <see cref="ScopeKind"/>.
    /// Null for <see cref="GoalScopeKind.Library"/>.</summary>
    public string? ScopeValue { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>Count (N items/pages in a period) or Finish ("finish this reading list": the target is the size of the scope).</summary>
    public GoalKind Kind { get; set; }

    /// <summary>Count goals only: count each item once instead of once per finish, so a re-read does not count again.</summary>
    public bool DistinctOnly { get; set; }

    /// <summary>Every filter must match (AND). Empty = the whole library (or the legacy <see cref="ScopeKind"/> pair on an old row).</summary>
    public List<ReadingGoalScope> Scopes { get; set; } = new();
}

/// <summary>One filter on a <see cref="ReadingGoal"/>. A goal's filters combine with AND.</summary>
public class ReadingGoalScope
{
    public int Id { get; set; }

    public int ReadingGoalId { get; set; }

    public ReadingGoal? ReadingGoal { get; set; }

    public GoalScopeKind Kind { get; set; }

    /// <summary>The entity id as a string (series, reading list, collection, story event, continuity, creator), the publisher / genre text,
    /// or the media-type name.</summary>
    public string? Value { get; set; }

    /// <summary>The name frozen at creation, so a goal whose list/collection/... was deleted can still say what it was.</summary>
    public string? Label { get; set; }
}

public enum GoalMetric
{
    Items,
    Pages,
}

public enum GoalKind
{
    Count,
    Finish,
}

public enum GoalPeriodKind
{
    ThisYear,
    ThisMonth,
    Custom,

    /// <summary>A finish goal with no deadline: <c>PeriodEnd</c> is <see cref="DateOnly.MaxValue"/> (a sentinel - a nullable column would force a SQLite table rebuild).</summary>
    NoDeadline,
}

// Values are stored as integers: append only, never reorder.
public enum GoalScopeKind
{
    Library,
    Series,
    Publisher,
    Genre,
    ReadingList,
    Collection,
    StoryEvent,
    Continuity,
    Creator,
    MediaType,
}
