using System;
using System.Collections.Generic;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// The one-line toast shown when a book reading session ends
/// (docs/superpowers/specs/2026-10-27-books-slice3a-reader-jumpback-session-design.md §3).
/// </summary>
public static class BookSessionSummary
{
    public const int MinimumActiveSeconds = 300;

    /// <summary>"32 min · 41 pages · Daily pages: 62 of 50"; null under five minutes of active time.</summary>
    public static string? Format(int activeSeconds, int pages, string? goalLine)
    {
        if (activeSeconds < MinimumActiveSeconds)
            return null;

        var minutes = (int)Math.Round(activeSeconds / 60.0, MidpointRounding.AwayFromZero);
        var parts = new List<string> { FormatDuration(minutes) };
        if (pages > 0)
            parts.Add(pages == 1 ? "1 page" : $"{pages} pages");
        if (!string.IsNullOrWhiteSpace(goalLine))
            parts.Add(goalLine!);
        return string.Join(" · ", parts);
    }

    /// <summary>"Daily pages: 62 of 50" for the first goal still in play, "{name}: done" when it is complete; null when there is none.</summary>
    public static string? GoalLine(IEnumerable<Paperbunkr.Data.Metadata.GoalProgress> goals)
    {
        foreach (var progress in goals)
        {
            if (progress.ScopeMissing || progress.Outcome == Paperbunkr.Data.Metadata.GoalOutcome.Missed)
                continue;
            return progress.IsComplete
                ? $"{progress.Goal.Title}: done"
                : $"{progress.Goal.Title}: {progress.CurrentValue} of {progress.EffectiveTarget}";
        }

        return null;
    }

    private static string FormatDuration(int minutes)
    {
        if (minutes < 60)
            return $"{minutes} min";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} hr" : $"{hours} hr {rest} min";
    }
}
