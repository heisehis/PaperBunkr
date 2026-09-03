using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services;

/// <summary>
/// Pure fuzzy scorer + ranker for the Quick Open palette (docs/superpowers/specs/2026-09-03-quick-
/// open-command-palette-design.md). No DB, no state - unit-tested in isolation.
/// </summary>
public static class QuickOpenMatcher
{
    private const int MaxResults = 50;
    private const int RecencyWindowDays = 7;
    private const int PreTypeRecentCount = 8;

    /// <summary>
    /// Case-insensitive subsequence score: every char of <paramref name="query"/> must appear in
    /// <paramref name="target"/> in order. <see langword="null"/> = no match. Higher = better -
    /// rewards contiguous runs, word-boundary starts, a match at index 0, and a shorter target.
    /// </summary>
    public static int? Score(string query, string target)
    {
        if (string.IsNullOrEmpty(query))
        {
            return 0;
        }

        int score = 0;
        int qi = 0;
        int lastMatch = -2;
        char[] q = query.ToLowerInvariant().ToCharArray();

        for (int ti = 0; ti < target.Length && qi < q.Length; ti++)
        {
            char tc = char.ToLowerInvariant(target[ti]);
            if (tc != q[qi])
            {
                continue;
            }

            score += 1;

            if (ti == lastMatch + 1)
            {
                score += 5; // contiguous run
            }

            bool atBoundary = ti == 0 || IsBoundary(target[ti - 1]);
            if (atBoundary)
            {
                score += 8;
            }

            if (ti == 0)
            {
                score += 6; // exact prefix start
            }

            lastMatch = ti;
            qi++;
        }

        if (qi < q.Length)
        {
            return null;
        }

        // Shorter targets win a tie - small, never enough to overturn a real scoring difference.
        score -= target.Length / 8;
        return score;
    }

    private static bool IsBoundary(char c) => c is ' ' or '#' or '-' or ':' or '/' or '.' or '_';

    /// <summary>
    /// Empty query → the recently-opened issues/books (most-recent-first, capped) then the shell
    /// screens. Non-empty → scored matches ordered by score, then a recency boost, then kind
    /// priority, then target length; capped at 50.
    /// </summary>
    public static IReadOnlyList<QuickOpenEntry> Rank(string query, IReadOnlyList<QuickOpenEntry> index, DateTime? nowUtc = null)
    {
        DateTime now = nowUtc ?? DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(query))
        {
            var recent = index
                .Where(e => e.RecencyUtc is not null && e.Kind is QuickOpenKind.Issue or QuickOpenKind.Book)
                .OrderByDescending(e => e.RecencyUtc)
                .Take(PreTypeRecentCount);

            var screens = index.Where(e => e.Kind == QuickOpenKind.Screen);
            return recent.Concat(screens).ToList();
        }

        return index
            .Select(e => (Entry: e, Score: Score(query, e.Primary)))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score!.Value)
            .ThenByDescending(x => RecencyBoost(x.Entry, now))
            .ThenBy(x => KindPriority(x.Entry.Kind))
            .ThenBy(x => x.Entry.Primary.Length)
            .Take(MaxResults)
            .Select(x => x.Entry)
            .ToList();
    }

    private static int RecencyBoost(QuickOpenEntry e, DateTime now) =>
        e.RecencyUtc is { } r && (now - r).TotalDays <= RecencyWindowDays ? 1 : 0;

    private static int KindPriority(QuickOpenKind kind) => kind switch
    {
        QuickOpenKind.Series => 0,
        QuickOpenKind.Book => 0,
        QuickOpenKind.Issue => 1,
        QuickOpenKind.ReadingList => 2,
        QuickOpenKind.SmartList => 2,
        QuickOpenKind.Collection => 2,
        QuickOpenKind.StoryEvent => 2,
        QuickOpenKind.Continuity => 2,
        QuickOpenKind.Screen => 3,
        QuickOpenKind.Action => 4,
        _ => 5,
    };
}
