using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One series' Continue Reading candidacy - <see cref="ResumeIssue"/> is the specific issue
/// to jump back into, not necessarily the same one <c>SeriesCardSample.ContinueReadingIssueId</c>
/// (Paperbunkr.App) would pick (that one finds the first *unread* issue in reading order; this finds
/// the actual in-progress one, which can be a later issue if earlier ones were never opened at all -
/// see docs/superpowers/specs/2026-08-18-home-screen-design.md Module 1).</summary>
public sealed record ContinueReadingCandidate(Series Series, Issue ResumeIssue);

/// <summary>
/// Read-only query/pick logic for the Home screen's five modules (docs/superpowers/specs/
/// 2026-08-18-home-screen-design.md) - mirrors <see cref="RecommendationResolver"/>'s shape (static,
/// testable, no persistence) for the two modules that need genuinely new logic (Spotlight, Reading
/// List spotlight); Continue Reading/Recently Added are here too for the same reason Library's own
/// sort fields aren't scattered across the ViewModel - one place to find "how is X computed."
/// <see cref="RecommendationResolver.GetRecommendations"/> itself is reused as-is for the "Because
/// You Read" module, not duplicated here.
/// </summary>
public static class HomeFeedResolver
{
    /// <summary>Series with at least one <see cref="IssueMetadataExtensions.IsInProgress"/> issue,
    /// paired with whichever such issue was opened most recently (a series could have more than one
    /// in-progress issue in unusual cases - the most-recently-touched one wins as the resume target).
    /// Sorted by that issue's <see cref="Issue.OpenedTime"/> descending. Excludes series the user has
    /// marked <see cref="ReadingStatus.Dropped"/> (docs/superpowers/specs/2026-08-19-metadata-model-
    /// reading-status-design.md) - a dropped series resurfacing here would contradict the status the
    /// user just set.</summary>
    public static IReadOnlyList<ContinueReadingCandidate> GetContinueReading(PaperbunkrDbContext context, int limit = 10)
    {
        var series = context.Series.Include(s => s.Issues)
            .Where(s => s.ReadingStatus != ReadingStatus.Dropped)
            .ToList();

        var candidates = new List<ContinueReadingCandidate>();
        foreach (var s in series)
        {
            var resumeIssue = s.Issues
                .Where(i => i.IsInProgress())
                .OrderByDescending(i => i.OpenedTime)
                .FirstOrDefault();

            if (resumeIssue is not null)
            {
                candidates.Add(new ContinueReadingCandidate(s, resumeIssue));
            }
        }

        return candidates
            .OrderByDescending(c => c.ResumeIssue.OpenedTime)
            .Take(limit)
            .ToList();
    }

    /// <summary>Series sorted by their newest issue's <see cref="Issue.AddedTime"/> descending - same
    /// aggregate <c>SeriesCardSample.LastAddedTime</c> (Paperbunkr.App) already computes for
    /// Library's own sort field. Series with no issues (shouldn't normally happen, but not assumed
    /// impossible) are excluded rather than throwing on an empty Max().</summary>
    public static IReadOnlyList<Series> GetRecentlyAdded(PaperbunkrDbContext context, int limit = 10)
    {
        return context.Series.Include(s => s.Issues)
            .ToList()
            .Where(s => s.Issues.Count > 0)
            .OrderByDescending(s => s.Issues.Max(i => i.AddedTime))
            .Take(limit)
            .ToList();
    }

    /// <summary>Seed series ids for the "Because You Read" module - the <paramref name="count"/> most
    /// recently-opened series, by their newest issue's <see cref="Issue.OpenedTime"/>. Series with no
    /// opened issues at all are excluded (nothing to seed "because you read" from).</summary>
    public static IReadOnlyList<int> GetRecentlyOpenedSeriesIds(PaperbunkrDbContext context, int count = 3)
    {
        return context.Series.Include(s => s.Issues)
            .ToList()
            .Where(s => s.Issues.Any(i => i.OpenedTime is not null))
            .OrderByDescending(s => s.Issues.Max(i => i.OpenedTime))
            .Take(count)
            .Select(s => s.Id)
            .ToList();
    }

    /// <summary>
    /// Weighted-random genre-frequency picks, without replacement (Module 4 - a small auto-advancing
    /// carousel, not a single static pick, per the user's own explicit follow-up request after
    /// recalling a similar carousel from a companion project). Builds a frequency map by tokenizing
    /// <see cref="Issue.Genre"/> across every <see cref="IssueMetadataExtensions.HasBeenRead"/> issue,
    /// then repeatedly weighted-picks from the remaining candidate pool (every
    /// <see cref="IssueMetadataExtensions.IsUnread"/> issue - deliberately excludes in-progress issues
    /// too, so this never overlaps Module 1) by the sum of the frequency-map counts for each
    /// candidate's own genre tokens, removing each pick before the next roll. Once every remaining
    /// candidate has zero weight (no read history yet, or every genre-matching issue has already been
    /// picked), falls back to uniform-random-without-replacement for the rest. Returns fewer than
    /// <paramref name="count"/> items when the candidate pool itself is smaller; empty only when it's
    /// empty (every issue is either read or in progress).
    /// </summary>
    public static IReadOnlyList<Issue> GetSpotlightPicks(PaperbunkrDbContext context, Random random, int count = 6)
    {
        var allIssues = context.Issues.Include(i => i.Series).Include(i => i.Tags).ToList();
        var candidates = allIssues.Where(i => i.IsUnread()).ToList();
        if (candidates.Count == 0)
        {
            return Array.Empty<Issue>();
        }

        var genreFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in allIssues.Where(i => i.HasBeenRead()))
        {
            foreach (string token in TextTokenizer.Tokenize(issue.JoinedGenre()))
            {
                genreFrequency[token] = genreFrequency.GetValueOrDefault(token) + 1;
            }
        }

        var remaining = candidates
            .Select(c => (Issue: c, Weight: TextTokenizer.Tokenize(c.JoinedGenre()).Sum(g => genreFrequency.GetValueOrDefault(g))))
            .ToList();

        var picks = new List<Issue>();
        while (picks.Count < count && remaining.Count > 0)
        {
            int totalWeight = remaining.Sum(x => x.Weight);
            int index;
            if (totalWeight > 0)
            {
                int roll = random.Next(totalWeight);
                int cumulative = 0;
                index = remaining.Count - 1; // Unreachable fallback given roll < totalWeight.
                for (int i = 0; i < remaining.Count; i++)
                {
                    cumulative += remaining[i].Weight;
                    if (roll < cumulative)
                    {
                        index = i;
                        break;
                    }
                }
            }
            else
            {
                index = random.Next(remaining.Count);
            }

            picks.Add(remaining[index].Issue);
            remaining.RemoveAt(index);
        }

        return picks;
    }

    /// <summary>Uniform-random pick among <see cref="ReadingList"/>s with at least one unread linked
    /// item (Module 5). Returns null when every list is either empty or fully read, or no lists
    /// exist at all.</summary>
    public static ReadingList? GetReadingListSpotlight(PaperbunkrDbContext context, Random random)
    {
        // ThenInclude(Tags) on Issue - ReadingListSpotlightSample.Compute reads Issue.JoinedGenre()
        // (docs/superpowers/specs/2026-08-23-weighted-categorized-tags-design.md).
        var lists = context.ReadingLists
            .Include(l => l.Items).ThenInclude(item => item.Issue!).ThenInclude(i => i.Series)
            .Include(l => l.Items).ThenInclude(item => item.Issue!).ThenInclude(i => i.Tags)
            .ToList();

        var candidates = lists
            .Where(l => l.Items.Any(item => item.Issue is not null && item.Issue.IsUnread()))
            .ToList();

        return candidates.Count == 0 ? null : candidates[random.Next(candidates.Count)];
    }

    /// <summary>Novels the user has started but not finished (docs/superpowers/specs/2026-08-27-
    /// books-screen-chrome-and-home-strip-design.md) - <see cref="Book.LastOpenedTime"/> set,
    /// <see cref="Book.Finished"/> false, and a real reading position (not merely opened once).
    /// Newest-opened first, capped. Feeds Home's separate "Continue Reading — Books" row.</summary>
    public static IReadOnlyList<Book> GetContinueReadingBooks(PaperbunkrDbContext context, int limit = 10)
    {
        return context.Books
            .Include(b => b.BookSeries)
            .Where(b => b.LastOpenedTime != null
                        && !b.Finished
                        && (b.LastChapterIndex > 0 || b.LastCharacterOffset > 0))
            .OrderByDescending(b => b.LastOpenedTime)
            .Take(limit)
            .ToList();
    }
}
