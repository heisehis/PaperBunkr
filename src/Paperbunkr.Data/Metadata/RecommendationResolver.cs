using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One relationally-anchored recommendation candidate (docs/superpowers/specs/2026-08-18-metadata-model-phase6a-recommendation-engine-design.md) - computed live, not persisted (see the spec's "Live-computed, not a persisted table" section for why).</summary>
public sealed record RecommendationCandidate(
    int SourceSeriesId,
    int TargetSeriesId,
    RecommendationReason ReasonType,
    decimal Score,
    string Explanation,
    DateTime GeneratedAt,
    RecommendationSignals Signals);

/// <summary>Per-signal breakdown (§47: "expose separate signals," not one opaque number) - also what <see cref="RecommendationCandidate.ReasonType"/> is picked from.</summary>
public sealed record RecommendationSignals(
    decimal RelationScore,
    decimal ContinuityScore,
    decimal EventScore,
    decimal CollectionScore,
    decimal CharacterScore,
    decimal CreatorScore,
    decimal GenreScore,
    decimal TagScore);

/// <summary>
/// Read-only recommendation engine (docs/superpowers/specs/2026-08-18-metadata-model-phase6a-
/// recommendation-engine-design.md). The candidate pool is relationally-anchored: a target series
/// only appears if it shares a <see cref="MediaRelation"/>, <see cref="Continuity"/>, or
/// <see cref="StoryEvent"/> membership with the source - content-similarity signals (genre/
/// character/creator/tag overlap) refine an already-anchored candidate's score and explanation, they
/// never independently surface an otherwise-unrelated series.
/// </summary>
public static class RecommendationResolver
{
    // Rebalanced to make room for CollectionWeight (still summing to 1.0): Relation 0.30->0.25,
    // Continuity/Event 0.15->0.10 each - a Collection is at least as deliberate an editorial anchor
    // as Continuity/Event membership (the user hand-picks every member), so it gets a comparable
    // weight rather than being tacked on as an afterthought.
    private const decimal RelationWeight = 0.25m;
    private const decimal ContinuityWeight = 0.10m;
    private const decimal EventWeight = 0.10m;
    private const decimal CollectionWeight = 0.15m;
    private const decimal CharacterWeight = 0.15m;
    private const decimal CreatorWeight = 0.10m;
    private const decimal GenreWeight = 0.10m;
    private const decimal TagWeight = 0.05m;

    public static IReadOnlyList<RecommendationCandidate> GetRecommendations(PaperbunkrDbContext context, int seriesId, int limit = 10)
    {
        var source = context.Series.FirstOrDefault(s => s.Id == seriesId);
        if (source is null)
        {
            return Array.Empty<RecommendationCandidate>();
        }

        // Strongest MediaRelation per target (a pair can have more than one relation type), plus
        // Continuity/Event anchoring - all three already-tested resolvers, reused as-is.
        var relationsByTarget = MediaRelationResolver.GetRelatedSeries(context, seriesId)
            .GroupBy(r => r.OtherSeries.Id)
            .ToDictionary(g => g.Key, g => LoadStrongestEvidence(context, g));

        var continuityTargetIds = ContinuityResolver.GetOtherSeriesSharingContinuity(context, seriesId)
            .Select(s => s.Id)
            .ToHashSet();

        var eventTargetIds = EventMembershipResolver.GetOtherSeriesInSharedEvents(context, seriesId)
            .Select(s => s.Id)
            .ToHashSet();

        var collectionTargetIds = CollectionResolver.GetOtherSeriesSharingCollection(context, seriesId)
            .Select(s => s.Id)
            .ToHashSet();

        var candidateIds = relationsByTarget.Keys
            .Union(continuityTargetIds)
            .Union(eventTargetIds)
            .Union(collectionTargetIds)
            .ToList();

        if (candidateIds.Count == 0)
        {
            return Array.Empty<RecommendationCandidate>();
        }

        var candidateSeries = context.Series
            .Include(s => s.Issues).ThenInclude(i => i.Tags)
            .Where(s => candidateIds.Contains(s.Id))
            .ToDictionary(s => s.Id);

        var sourceSeriesWithIssues = context.Series.Include(s => s.Issues).ThenInclude(i => i.Tags).First(s => s.Id == seriesId);
        var sourceCharacters = AggregateTokens(sourceSeriesWithIssues.Issues, i => i.Characters);
        var sourceCreators = AggregateCreatorTokens(sourceSeriesWithIssues.Issues);
        var sourceTags = AggregateTokens(sourceSeriesWithIssues.Issues, i => i.JoinedTags());
        var sourceGenre = TextTokenizer.Tokenize(sourceSeriesWithIssues.Genre);

        DateTime generatedAt = DateTime.UtcNow;
        var results = new List<RecommendationCandidate>();

        foreach (int targetId in candidateIds)
        {
            if (!candidateSeries.TryGetValue(targetId, out var target))
            {
                continue;
            }

            bool hasRelation = relationsByTarget.TryGetValue(targetId, out var relation);
            decimal relationScore = hasRelation ? relation.Confidence : 0m;
            RelationType? relationDisplayType = hasRelation ? relation.DisplayType : null;
            decimal continuityScore = continuityTargetIds.Contains(targetId) ? 1.0m : 0m;
            decimal eventScore = eventTargetIds.Contains(targetId) ? 1.0m : 0m;
            decimal collectionScore = collectionTargetIds.Contains(targetId) ? 1.0m : 0m;

            var targetCharacters = AggregateTokens(target.Issues, i => i.Characters);
            var targetCreators = AggregateCreatorTokens(target.Issues);
            var targetTags = AggregateTokens(target.Issues, i => i.JoinedTags());
            var targetGenre = TextTokenizer.Tokenize(target.Genre);

            decimal characterScore = OverlapRatio(sourceCharacters, targetCharacters);
            decimal creatorScore = OverlapRatio(sourceCreators, targetCreators);
            decimal genreScore = OverlapRatio(sourceGenre, targetGenre);
            decimal tagScore = OverlapRatio(sourceTags, targetTags);

            var signals = new RecommendationSignals(relationScore, continuityScore, eventScore, collectionScore, characterScore, creatorScore, genreScore, tagScore);

            decimal finalScore =
                relationScore * RelationWeight +
                continuityScore * ContinuityWeight +
                eventScore * EventWeight +
                collectionScore * CollectionWeight +
                characterScore * CharacterWeight +
                creatorScore * CreatorWeight +
                genreScore * GenreWeight +
                tagScore * TagWeight;

            var (reason, explanation) = DescribeDominantSignal(signals, relationDisplayType, target.Name, sourceCharacters, sourceCreators, sourceGenre, sourceTags, targetCharacters, targetCreators, targetGenre, targetTags);

            results.Add(new RecommendationCandidate(seriesId, targetId, reason, finalScore, explanation, generatedAt, signals));
        }

        return results.OrderByDescending(r => r.Score).Take(limit).ToList();
    }

    private static (RelationType DisplayType, decimal Confidence) LoadStrongestEvidence(
        PaperbunkrDbContext context,
        IEnumerable<(Series OtherSeries, RelationType DisplayType, int MediaRelationId)> relationsForTarget)
    {
        var relationIds = relationsForTarget.Select(r => r.MediaRelationId).ToList();
        var relations = context.MediaRelations
            .Include(m => m.Evidence)
            .Where(m => relationIds.Contains(m.Id))
            .ToList();

        var best = relations
            .Select(m => (m.Id, Confidence: m.Evidence.Count > 0 ? m.Evidence.Max(e => e.Confidence) : 0m))
            .OrderByDescending(x => x.Confidence)
            .First();

        RelationType displayType = relationsForTarget.First(r => r.MediaRelationId == best.Id).DisplayType;
        return (displayType, best.Confidence);
    }

    private static (RecommendationReason Reason, string Explanation) DescribeDominantSignal(
        RecommendationSignals signals,
        RelationType? relationDisplayType,
        string targetName,
        HashSet<string> sourceCharacters, HashSet<string> sourceCreators, HashSet<string> sourceGenre, HashSet<string> sourceTags,
        HashSet<string> targetCharacters, HashSet<string> targetCreators, HashSet<string> targetGenre, HashSet<string> targetTags)
    {
        var weighted = new (string Signal, decimal Weighted)[]
        {
            ("Relation", signals.RelationScore * RelationWeight),
            ("Continuity", signals.ContinuityScore * ContinuityWeight),
            ("Event", signals.EventScore * EventWeight),
            ("Collection", signals.CollectionScore * CollectionWeight),
            ("Character", signals.CharacterScore * CharacterWeight),
            ("Creator", signals.CreatorScore * CreatorWeight),
            ("Genre", signals.GenreScore * GenreWeight),
            ("Tag", signals.TagScore * TagWeight),
        };

        string dominant = weighted.OrderByDescending(w => w.Weighted).First().Signal;

        return dominant switch
        {
            "Relation" => (MapRelationType(relationDisplayType), ExplainRelation(relationDisplayType, targetName)),
            "Continuity" => (RecommendationReason.SameContinuity, $"Shares continuity with {targetName}"),
            "Event" => (RecommendationReason.SameEvent, $"Shares a story event with {targetName}"),
            "Collection" => (RecommendationReason.SameCollection, $"In the same collection as {targetName}"),
            "Character" => (RecommendationReason.SharedCharacters, $"Shares {Intersect(sourceCharacters, targetCharacters).Count} character(s) with {targetName}"),
            "Creator" => (RecommendationReason.SharedCreators, $"Shares {Intersect(sourceCreators, targetCreators).Count} creator(s) with {targetName}"),
            "Genre" => (RecommendationReason.SharedGenre, $"Shares {Intersect(sourceGenre, targetGenre).Count} genre(s) with {targetName}"),
            "Tag" => (RecommendationReason.SharedTags, $"Shares {Intersect(sourceTags, targetTags).Count} tag(s) with {targetName}"),
            _ => (RecommendationReason.Related, $"Related to {targetName}"),
        };
    }

    private static RecommendationReason MapRelationType(RelationType? type) => type switch
    {
        RelationType.Prequel => RecommendationReason.Prequel,
        RelationType.Sequel => RecommendationReason.Sequel,
        RelationType.Crossover => RecommendationReason.Crossover,
        RelationType.SameUniverse => RecommendationReason.SameUniverse,
        RelationType.SharedUniverse => RecommendationReason.SameUniverse,
        RelationType.SameContinuity => RecommendationReason.SameContinuity,
        RelationType.SharedCharacters => RecommendationReason.SharedCharacters,
        _ => RecommendationReason.Related,
    };

    /// <summary><paramref name="type"/> is the already-resolved display type for the target's own
    /// role (per <see cref="MediaRelationResolver.GetRelatedSeries"/>'s doc comment) - e.g.
    /// <see cref="RelationType.Sequel"/> means the *target* is the sequel, not the source.</summary>
    private static string ExplainRelation(RelationType? type, string targetName) => type switch
    {
        RelationType.Prequel => $"{targetName} is a prequel",
        RelationType.Sequel => $"{targetName} is a sequel",
        RelationType.Crossover => $"Crossover with {targetName}",
        RelationType.SameUniverse or RelationType.SharedUniverse => $"Same universe as {targetName}",
        RelationType.SameContinuity => $"Same continuity as {targetName}",
        RelationType.SharedCharacters => $"Shares characters with {targetName}",
        _ => $"Related to {targetName}",
    };

    private static HashSet<string> Intersect(HashSet<string> a, HashSet<string> b)
    {
        var result = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        result.IntersectWith(b);
        return result;
    }

    private static decimal OverlapRatio(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0m;
        }

        var union = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        union.UnionWith(b);

        int intersectionCount = Intersect(a, b).Count;
        return union.Count == 0 ? 0m : (decimal)intersectionCount / union.Count;
    }

    private static HashSet<string> AggregateTokens(IEnumerable<Issue> issues, Func<Issue, string?> selector)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in issues)
        {
            tokens.UnionWith(TextTokenizer.Tokenize(selector(issue)));
        }

        return tokens;
    }

    private static HashSet<string> AggregateCreatorTokens(IEnumerable<Issue> issues)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in issues)
        {
            tokens.UnionWith(TextTokenizer.Tokenize(issue.Writer));
            tokens.UnionWith(TextTokenizer.Tokenize(issue.Penciller));
            tokens.UnionWith(TextTokenizer.Tokenize(issue.Inker));
            tokens.UnionWith(TextTokenizer.Tokenize(issue.Colorist));
            tokens.UnionWith(TextTokenizer.Tokenize(issue.Letterer));
            tokens.UnionWith(TextTokenizer.Tokenize(issue.CoverArtist));
            tokens.UnionWith(TextTokenizer.Tokenize(issue.Editor));
            tokens.UnionWith(TextTokenizer.Tokenize(issue.Translator));
        }

        return tokens;
    }
}
