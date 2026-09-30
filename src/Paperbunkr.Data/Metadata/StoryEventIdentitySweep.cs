using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>What a sweep did - one Activity Center summary (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §4).</summary>
public sealed record StoryEventIdentitySweepSummary(
    int Checked, int IdsFilled, int Failed, IReadOnlyList<StoryEventMergeResult> Merges, int ToReview, IReadOnlyList<ComicProvider> MissingProviders)
{
    public string Text
    {
        get
        {
            string text = $"{IdsFilled} id{(IdsFilled == 1 ? "" : "s")} filled · {Merges.Count} duplicate{(Merges.Count == 1 ? "" : "s")} merged · {ToReview} to review";
            if (Failed > 0)
            {
                text += $" · {Failed} to retry";
            }

            if (MissingProviders.Count > 0)
            {
                text += $" ({string.Join(" and ", MissingProviders.Select(ComicProviderFactory.DisplayName))} not configured)";
            }

            return text;
        }
    }

    /// <summary>"Hulk: Planet Hulk → Planet Hulk", one line per merge.</summary>
    public string MergeDetails => string.Join("\n", Merges.Select(m => $"{m.RemovedName} → {m.SurvivorName}"));
}

/// <summary>
/// The story-event identity sweep: id completion for every due event (batched - an event checked within 30 days with unchanged members
/// is skipped, so repeated runs resume where the last stopped), then <see cref="StoryEventIdentityResolver.FindPairs"/>, then the silent
/// merges, repeated until none are left (a merge can make its survivor match a third event).
/// </summary>
public static class StoryEventIdentitySweep
{
    /// <summary>Events id-completed per run. At ≤ 5 sampled issues per provider plus a few arc calls each, this stays inside the Metron and ComicVine limits over a run.</summary>
    public const int DefaultBatchSize = 40;

    private const int MaxMergeRounds = 50;

    public static async Task<StoryEventIdentitySweepSummary> RunAsync(
        Func<PaperbunkrDbContext> contextFactory,
        IReadOnlyDictionary<ComicProvider, IArcIdentitySource> sources,
        IReadOnlyCollection<int>? onlyEventIds,
        int batchSize,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        List<int> due;
        using (var context = contextFactory())
        {
            var rows = context.StoryEvents.AsNoTracking()
                .Where(e => onlyEventIds == null || onlyEventIds.Contains(e.Id))
                .Select(e => new { Event = e, IssueIds = e.Members.Select(m => m.IssueId).ToList() })
                .ToList();
            due = rows
                .Where(r => StoryEventIdCompletion.IsDue(r.Event, r.IssueIds, nowUtc))
                .OrderBy(r => r.Event.IdentityCheckedAt ?? DateTime.MinValue)
                .ThenBy(r => r.Event.Id)
                .Take(Math.Max(1, batchSize))
                .Select(r => r.Event.Id)
                .ToList();
        }

        int checkedCount = 0, filled = 0, failed = 0;
        if (sources.Count > 0)
        {
            for (int i = 0; i < due.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((i, due.Count));
                using var context = contextFactory();
                if (!context.StoryEvents.Any(e => e.Id == due[i]))
                {
                    continue;
                }

                var outcome = await StoryEventIdCompletion.CompleteAsync(context, due[i], sources, nowUtc, cancellationToken).ConfigureAwait(false);
                filled += outcome.IdsFilled;
                if (outcome.Status == IdCompletionStatus.Failed)
                {
                    failed++;
                }
                else if (outcome.Status == IdCompletionStatus.Checked)
                {
                    checkedCount++;
                }
            }

            progress?.Report((due.Count, due.Count));
        }

        var merges = MergeSilentPairs(contextFactory, onlyEventIds);
        int toReview;
        using (var context = contextFactory())
        {
            toReview = StoryEventIdentityResolver.FindReviewItems(context).Count;
        }

        var missing = ComicProviderFactory.All.Where(p => !sources.ContainsKey(p)).ToList();
        return new StoryEventIdentitySweepSummary(checkedCount, filled, failed, merges, toReview, missing);
    }

    /// <summary>
    /// Merges every silent pair. With <paramref name="onlyEventIds"/>, only pairs touching those events (followed through to their
    /// survivors) - the accept-time check never merges unrelated events behind the user's back.
    /// </summary>
    public static IReadOnlyList<StoryEventMergeResult> MergeSilentPairs(Func<PaperbunkrDbContext> contextFactory, IReadOnlyCollection<int>? onlyEventIds = null)
    {
        var scope = onlyEventIds is null ? null : new HashSet<int>(onlyEventIds);
        var merges = new List<StoryEventMergeResult>();
        for (int round = 0; round < MaxMergeRounds; round++)
        {
            DuplicatePair? next;
            using (var context = contextFactory())
            {
                next = StoryEventIdentityResolver.FindPairs(context)
                    .FirstOrDefault(p => p.IsSilent && (scope is null || scope.Contains(p.EventAId) || scope.Contains(p.EventBId)));
            }

            if (next is null)
            {
                break;
            }

            using (var context = contextFactory())
            {
                var result = StoryEventMerger.Merge(context, next.EventAId, next.EventBId);
                merges.Add(result);
                scope?.Add(result.SurvivorId);
            }
        }

        return merges;
    }
}
