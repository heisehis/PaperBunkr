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

    public GoalScopeKind ScopeKind { get; set; }

    /// <summary>Series id (as a string), publisher name, or genre tag depending on <see cref="ScopeKind"/>.
    /// Null for <see cref="GoalScopeKind.Library"/>.</summary>
    public string? ScopeValue { get; set; }

    public DateTime CreatedUtc { get; set; }
}

public enum GoalMetric
{
    Items,
    Pages,
}

public enum GoalPeriodKind
{
    ThisYear,
    ThisMonth,
    Custom,
}

public enum GoalScopeKind
{
    Library,
    Series,
    Publisher,
    Genre,
}
