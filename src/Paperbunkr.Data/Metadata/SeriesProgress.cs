using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// How far through a series the reader is (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.1). Shared by the series
/// smart-list fields (<see cref="SmartListField.UnreadCount"/> etc.) and the reading-behaviour resolvers (Up Next, drop-off) so
/// "read" means the same thing everywhere: the 95% rule (<see cref="IssueMetadataExtensions.HasBeenRead"/>); an in-progress issue
/// counts as unread; placeholders (wanted, not owned) are not counted at all.
/// </summary>
public readonly record struct SeriesProgress(int ReadCount, int UnreadCount, DateTime? LastReadUtc)
{
    /// <summary>
    /// <paramref name="issues"/> are one series' issues; <paramref name="lastEventUtc"/> is the newest <see cref="ReadingEvent"/> timestamp
    /// for the series (null if none). <see cref="LastReadUtc"/> is the later of that and the newest <see cref="Issue.OpenedTime"/> (stored UTC).
    /// </summary>
    public static SeriesProgress Of(IEnumerable<Issue> issues, DateTime? lastEventUtc)
    {
        int read = 0;
        int unread = 0;
        DateTime? last = lastEventUtc;

        foreach (var issue in issues)
        {
            if (issue.OpenedTime is { } opened && (last is null || opened > last))
            {
                last = opened;
            }

            if (issue.IsPlaceholder)
            {
                continue;
            }

            if (issue.HasBeenRead())
            {
                read++;
            }
            else
            {
                unread++;
            }
        }

        return new SeriesProgress(read, unread, last);
    }

    /// <summary>Whole days between <see cref="LastReadUtc"/> and <paramref name="nowUtc"/>; null when the series was never opened.</summary>
    public int? DaysSinceLastRead(DateTime nowUtc) =>
        LastReadUtc is { } last ? Math.Max(0, (int)Math.Floor((nowUtc - last).TotalDays)) : null;
}
