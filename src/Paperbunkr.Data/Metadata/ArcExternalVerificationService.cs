using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Optional, strictly additive verification/enrichment pass over
/// <see cref="StoryArcGroupingResolver"/> candidates, reusing the existing CBL Manager
/// <see cref="ComicVineSource"/>/<see cref="MetronSource"/> arc-lookup adapters (docs/superpowers/
/// specs/2026-09-17-storyevent-continuity-autopopulate-design.md, Phase 1). A candidate is never
/// discarded because a source didn't recognize it - local library data is the base signal; this
/// only ever upgrades <see cref="FormatSignalStrength.Weak"/> to <see cref="FormatSignalStrength.Strong"/>
/// and fills in ordering, never the reverse.
/// </summary>
internal static class ArcExternalVerificationService
{
    /// <summary>Comfortably inside ComicVine's ~200/hour limit even against a large first-time backlog.</summary>
    private const int MaxCandidatesPerRun = 15;

    /// <summary>External catalogs change slowly; a monthly re-check avoids permanently blacklisting an arc a source simply hadn't indexed yet.</summary>
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromDays(30);

    public static Task<IReadOnlyList<StoryEventCandidate>> VerifyAsync(
        PaperbunkrDbContext context,
        IReadOnlyList<StoryEventCandidate> candidates,
        CancellationToken cancellationToken) =>
        VerifyAsync(context, candidates, ReadingListSourceRegistry.Get(context, "ComicVine"), ReadingListSourceRegistry.Get(context, "Metron"), cancellationToken);

    /// <summary>Test seam - production always resolves sources from <see cref="ReadingListSourceRegistry"/> via the overload above.</summary>
    internal static async Task<IReadOnlyList<StoryEventCandidate>> VerifyAsync(
        PaperbunkrDbContext context,
        IReadOnlyList<StoryEventCandidate> candidates,
        IReadingListSource? comicVine,
        IReadingListSource? metron,
        CancellationToken cancellationToken)
    {
        if (comicVine is null && metron is null)
        {
            // Neither source is credentialed - nothing to verify against, every candidate stays Weak.
            return candidates;
        }

        DateTime cacheHorizon = DateTime.UtcNow - NegativeCacheTtl;
        var negativeCache = context.StoryEventVerificationNegativeCaches
            .Where(c => c.CheckedAt >= cacheHorizon)
            .Select(c => new { c.ArcName, c.Publisher, c.Source })
            .AsEnumerable()
            .Select(c => (c.ArcName, c.Publisher, c.Source))
            .ToHashSet();

        var prioritized = candidates.OrderByDescending(c => c.Members.Count).Take(MaxCandidatesPerRun).ToList();
        var byKey = candidates.ToDictionary(c => (c.ArcName, c.Publisher));

        foreach (var candidate in prioritized)
        {
            var verified = await VerifyOneAsync(context, candidate, comicVine, metron, negativeCache, cancellationToken).ConfigureAwait(false);
            byKey[(candidate.ArcName, candidate.Publisher)] = verified;
        }

        return candidates.Select(c => byKey[(c.ArcName, c.Publisher)]).ToList();
    }

    private static async Task<StoryEventCandidate> VerifyOneAsync(
        PaperbunkrDbContext context,
        StoryEventCandidate candidate,
        IReadingListSource? comicVine,
        IReadingListSource? metron,
        HashSet<(string ArcName, string Publisher, ArcVerificationSource Source)> negativeCache,
        CancellationToken cancellationToken)
    {
        if (comicVine is not null && !negativeCache.Contains((candidate.ArcName, candidate.Publisher, ArcVerificationSource.ComicVine)))
        {
            var upgraded = await TryVerifyAgainstSourceAsync(context, candidate, comicVine, ArcVerificationSource.ComicVine, cancellationToken).ConfigureAwait(false);
            if (upgraded is not null)
            {
                return upgraded;
            }

            // ComicVine failed or found nothing - fall back to Metron for this same candidate
            // rather than aborting verification for it entirely.
        }

        if (metron is not null && !negativeCache.Contains((candidate.ArcName, candidate.Publisher, ArcVerificationSource.Metron)))
        {
            var upgraded = await TryVerifyAgainstSourceAsync(context, candidate, metron, ArcVerificationSource.Metron, cancellationToken).ConfigureAwait(false);
            if (upgraded is not null)
            {
                return upgraded;
            }
        }

        return candidate;
    }

    private static async Task<StoryEventCandidate?> TryVerifyAgainstSourceAsync(
        PaperbunkrDbContext context,
        StoryEventCandidate candidate,
        IReadingListSource source,
        ArcVerificationSource sourceKind,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ArcSearchResult> results;
        try
        {
            results = await source.SearchAsync(candidate.ArcName, cancellationToken).ConfigureAwait(false);
        }
        catch (ReadingListSourceException)
        {
            // Treated the same as "no match" - the caller falls back to the other source, or the
            // candidate simply stays Weak. Never aborts the whole run over one failed lookup.
            return null;
        }

        // A result counts as a name match when it normalizes (case-insensitive, trimmed) to the
        // same string as the local group's arc name - the same normalization used for local
        // grouping, not a separate fuzzy-match threshold.
        var match = results.FirstOrDefault(r => string.Equals(r.Name.Trim(), candidate.ArcName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            RecordNegativeCache(context, candidate, sourceKind);
            return null;
        }

        IReadOnlyList<ArcIssue> orderedIssues;
        try
        {
            orderedIssues = await source.GetArcIssuesInOrderAsync(match.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (ReadingListSourceException)
        {
            return null;
        }

        var externalNumbers = orderedIssues
            .Select((issue, index) => (issue.Number.Trim(), Index: index))
            .GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Index, StringComparer.OrdinalIgnoreCase);

        int matchedCount = candidate.Members.Count(m => externalNumbers.ContainsKey((m.Issue.Number ?? string.Empty).Trim()));
        if (matchedCount < Math.Max(1, candidate.Members.Count / 2))
        {
            // Fewer than half the local issues recognized externally - not confident enough to
            // call this the same arc. Leave the candidate as-is rather than upgrading it.
            return null;
        }

        var reordered = candidate.Members
            .OrderBy(m => externalNumbers.TryGetValue((m.Issue.Number ?? string.Empty).Trim(), out int idx) ? idx : int.MaxValue)
            .ThenBy(m => m.Issue.Year ?? int.MaxValue)
            .Select((m, i) => m with { Position = i })
            .ToList();

        string reason = $"{candidate.Reason} · verified against {source.DisplayName} ({matchedCount}/{candidate.Members.Count} issues matched)";

        return candidate with
        {
            Members = reordered,
            Strength = FormatSignalStrength.Strong,
            Reason = reason,
            ComicVineArcId = sourceKind == ArcVerificationSource.ComicVine ? match.Id : candidate.ComicVineArcId,
            MetronArcId = sourceKind == ArcVerificationSource.Metron ? match.Id : candidate.MetronArcId,
        };
    }

    private static void RecordNegativeCache(PaperbunkrDbContext context, StoryEventCandidate candidate, ArcVerificationSource source)
    {
        bool already = context.StoryEventVerificationNegativeCaches.Any(c =>
            c.ArcName == candidate.ArcName && c.Publisher == candidate.Publisher && c.Source == source);
        if (already)
        {
            return;
        }

        context.StoryEventVerificationNegativeCaches.Add(new StoryEventVerificationNegativeCache
        {
            ArcName = candidate.ArcName,
            Publisher = candidate.Publisher,
            Source = source,
        });
        context.SaveChanges();
    }
}
