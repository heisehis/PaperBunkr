using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

public sealed record RecommendationCandidate(int SourceSeriesId, int TargetSeriesId, RecommendationReason ReasonType,
    decimal Score, string Explanation, DateTime GeneratedAt, RecommendationSignals Signals);
public sealed record RecommendationSignals(decimal RelationScore, decimal ContinuityScore, decimal EventScore, decimal CollectionScore,
    decimal CharacterScore, decimal CreatorScore, decimal GenreScore, decimal TagScore, decimal PublisherScore = 0, decimal EraScore = 0);

/// <summary>Local, read-only recommendations. Scalar/index projections discover candidates; only the best 200 get a full score.</summary>
public static class RecommendationResolver
{
    public const int CandidateLimit = 200;
    public static IReadOnlyDictionary<string, decimal> Weights { get; } = new Dictionary<string, decimal>
    {
        ["creator"] = .20m, ["character"] = .15m, ["relation"] = .15m, ["tag"] = .10m, ["genre"] = .10m,
        ["continuity"] = .10m, ["publisher"] = .05m, ["era"] = .05m, ["event"] = .05m, ["collection"] = .05m,
    };

    public static IReadOnlyList<RecommendationCandidate> GetRecommendations(PaperbunkrDbContext context, int seriesId, int limit = 10)
    {
        if (limit <= 0) return Array.Empty<RecommendationCandidate>();
        var metadata = context.Series.AsNoTracking().Where(s => s.Issues.Any(i => !i.IsPlaceholder))
            .Select(s => new { s.Id, s.Name, s.Genre, s.Publisher, s.Imprint }).ToDictionary(s => s.Id);
        if (!metadata.TryGetValue(seriesId, out var source)) return Array.Empty<RecommendationCandidate>();

        // These are bounded scalar rows, not tracked graphs with thousands of Issues/Tags/Creators materialised per series.
        var characters = context.CharacterAppearances.AsNoTracking().Where(a => !a.Issue!.IsPlaceholder)
            .Select(a => new { a.Issue!.SeriesId, a.CharacterId }).Distinct().ToList()
            .GroupBy(a => a.SeriesId).ToDictionary(g => g.Key, g => g.Select(a => a.CharacterId).ToHashSet());
        var credits = context.CreatorCredits.AsNoTracking().Where(c => !c.Issue!.IsPlaceholder)
            .Select(c => new { c.Issue!.SeriesId, c.CreatorId, c.Role }).Distinct().ToList().GroupBy(c => c.SeriesId)
            .ToDictionary(g => g.Key, g => g.GroupBy(c => c.CreatorId).ToDictionary(c => c.Key,
                c => c.Any(r => r.Role is "Writer" or "Artist" or "Penciller") ? 2m : 1m));
        var writerIds = context.CreatorCredits.AsNoTracking().Where(c => c.Role == "Writer" && !c.Issue!.IsPlaceholder)
            .Select(c => new { c.Issue!.SeriesId, c.CreatorId }).Distinct().ToList().ToLookup(c => c.SeriesId, c => c.CreatorId);
        var tags = context.IssueTags.AsNoTracking().Where(t => !t.Issue!.IsPlaceholder)
            .Select(t => new { t.Issue!.SeriesId, t.Field, t.Value, t.Weight }).ToList();
        var weightedTags = tags.Where(t => t.Field == IssueTagField.Tags).GroupBy(t => t.SeriesId)
            .ToDictionary(g => g.Key, g => g.GroupBy(t => t.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(t => t.Key, t => t.Max(v => TagWeight(v.Weight)), StringComparer.OrdinalIgnoreCase));
        var genres = metadata.Values.ToDictionary(s => s.Id, s => CharacterResolver.ParseNames(s.Genre).ToHashSet(StringComparer.OrdinalIgnoreCase));
        foreach (var tag in tags.Where(t => t.Field == IssueTagField.Genre))
            if (genres.TryGetValue(tag.SeriesId, out var genre)) genre.Add(tag.Value.Trim());
        var publicationRows = context.Issues.AsNoTracking().Where(i => !i.IsPlaceholder)
            .Select(i => new { i.SeriesId, i.Year, i.Publisher, i.Imprint }).ToList().ToLookup(i => i.SeriesId);
        string? Dominant(IEnumerable<string?> values, string? fallback) => values.Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v!.Trim(), StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key).FirstOrDefault() ?? fallback;
        // Issue metadata is the editable source of truth; the legacy series publisher is only a fallback.
        var publication = metadata.Values.ToDictionary(s => s.Id, s => (
            Publisher: Dominant(publicationRows[s.Id].Select(i => i.Publisher), s.Publisher),
            Imprint: Dominant(publicationRows[s.Id].Select(i => i.Imprint), s.Imprint),
            Year: publicationRows[s.Id].Where(i => i.Year > 0).Select(i => i.Year).DefaultIfEmpty(null).Min()));
        var sourcePublication = publication[seriesId];
        var continuityRows = context.ContinuityMemberships.AsNoTracking().Select(m => new { m.SeriesId, m.ContinuityId }).ToList();
        var sourceContinuities = continuityRows.Where(m => m.SeriesId == seriesId).Select(m => m.ContinuityId).ToHashSet();
        var continuityTargets = continuityRows.Where(m => sourceContinuities.Contains(m.ContinuityId)).Select(m => m.SeriesId).ToHashSet();
        var eventRows = context.EventMemberships.AsNoTracking().Where(m => !m.Issue!.IsPlaceholder)
            .Select(m => new { m.Issue!.SeriesId, m.StoryEventId }).Distinct().ToList();
        var sourceEvents = eventRows.Where(m => m.SeriesId == seriesId).Select(m => m.StoryEventId).ToHashSet();
        var eventTargets = eventRows.Where(m => sourceEvents.Contains(m.StoryEventId)).Select(m => m.SeriesId).ToHashSet();
        var collectionTargets = CollectionResolver.GetOtherSeriesSharingCollection(context, seriesId).Select(s => s.Id).ToHashSet();
        var relationEndpoints = MediaRelationResolver.GetRelatedFromSeries(context, seriesId).Where(r => r.Kind == MediaRelationEndpointKind.Series).ToList();
        var relationIds = relationEndpoints.Select(r => r.MediaRelationId).ToList();
        var evidence = context.MediaRelations.AsNoTracking().Include(r => r.Evidence).Where(r => relationIds.Contains(r.Id)).ToList()
            .ToDictionary(r => r.Id, r => r.Evidence.Select(e => e.Confidence).DefaultIfEmpty(0m).Max());
        var relations = relationEndpoints.GroupBy(r => r.Series!.Id).ToDictionary(g => g.Key,
            g => g.OrderByDescending(r => evidence.GetValueOrDefault(r.MediaRelationId)).ThenBy(r => r.MediaRelationId).First());

        var emptyIds = new HashSet<int>();
        var emptyWeights = new Dictionary<int, decimal>();
        var emptyTags = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var sourceCharacters = characters.GetValueOrDefault(seriesId) ?? emptyIds;
        var sourceCreators = credits.GetValueOrDefault(seriesId) ?? emptyWeights;
        var sourceTags = weightedTags.GetValueOrDefault(seriesId) ?? emptyTags;
        var sourceGenres = genres[seriesId];
        var sourceWriters = writerIds[seriesId].ToHashSet();
        bool Same(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        var pool = metadata.Values.Where(s => s.Id != seriesId).Select(target =>
        {
            var targetCharacters = characters.GetValueOrDefault(target.Id) ?? emptyIds;
            var targetCreators = credits.GetValueOrDefault(target.Id) ?? emptyWeights;
            var targetTags = weightedTags.GetValueOrDefault(target.Id) ?? emptyTags;
            var targetPublication = publication[target.Id];
            int signals = (sourceCharacters.Overlaps(targetCharacters) ? 1 : 0) + (sourceCreators.Keys.Intersect(targetCreators.Keys).Any() ? 1 : 0)
                + (sourceTags.Keys.Intersect(targetTags.Keys, StringComparer.OrdinalIgnoreCase).Any() ? 1 : 0)
                + (sourceGenres.Overlaps(genres[target.Id]) ? 1 : 0) + (Same(sourcePublication.Publisher, targetPublication.Publisher) || Same(sourcePublication.Imprint, targetPublication.Imprint) ? 1 : 0)
                + (relations.ContainsKey(target.Id) ? 1 : 0) + (continuityTargets.Contains(target.Id) ? 1 : 0)
                + (eventTargets.Contains(target.Id) ? 1 : 0) + (collectionTargets.Contains(target.Id) ? 1 : 0);
            return (Target: target, Signals: signals);
        }).Where(c => c.Signals > 0).OrderByDescending(c => c.Signals).ThenBy(c => c.Target.Id).Take(CandidateLimit).ToList();

        var result = new List<RecommendationCandidate>();
        foreach (var candidate in pool)
        {
            var target = candidate.Target;
            var targetCharacters = characters.GetValueOrDefault(target.Id) ?? emptyIds;
            var targetCreators = credits.GetValueOrDefault(target.Id) ?? emptyWeights;
            var targetTags = weightedTags.GetValueOrDefault(target.Id) ?? emptyTags;
            var relation = relations.GetValueOrDefault(target.Id);
            var targetPublication = publication[target.Id];
            decimal publisher = Same(sourcePublication.Imprint, targetPublication.Imprint) ? 1m : Same(sourcePublication.Publisher, targetPublication.Publisher) ? .5m : 0m;
            decimal era = sourcePublication.Year is int sourceYear && targetPublication.Year is int targetYear
                ? EraScore(sourceYear, targetYear) : 0m;
            var signals = new RecommendationSignals(relation is null ? 0 : evidence.GetValueOrDefault(relation.MediaRelationId),
                continuityTargets.Contains(target.Id) ? 1 : 0, eventTargets.Contains(target.Id) ? 1 : 0, collectionTargets.Contains(target.Id) ? 1 : 0,
                Jaccard(sourceCharacters, targetCharacters), WeightedOverlap(sourceCreators, targetCreators),
                Jaccard(sourceGenres, genres[target.Id]), WeightedOverlap(sourceTags, targetTags, StringComparer.OrdinalIgnoreCase), publisher, era);
            int sharedCharacters = sourceCharacters.Intersect(targetCharacters).Count();
            bool sameWriter = writerIds[target.Id].Any(sourceWriters.Contains);
            var reasons = new (string Key, decimal Value, RecommendationReason Reason, string Text)[]
            {
                ("creator", signals.CreatorScore, RecommendationReason.SharedCreators, sameWriter ? "same writer" : "shared creators"),
                ("character", signals.CharacterScore, RecommendationReason.SharedCharacters, $"{sharedCharacters} shared characters"),
                ("relation", signals.RelationScore, RelationReason(relation?.DisplayType), RelationText(relation?.DisplayType)),
                ("tag", signals.TagScore, RecommendationReason.SharedTags, "shared tags"),
                ("genre", signals.GenreScore, RecommendationReason.SharedGenre, "shared genres"),
                ("continuity", signals.ContinuityScore, RecommendationReason.SameContinuity, "same continuity"),
                ("publisher", signals.PublisherScore, RecommendationReason.Related, publisher == 1 ? "same imprint" : "same publisher"),
                ("era", signals.EraScore, RecommendationReason.Related, "similar publication era"),
                ("event", signals.EventScore, RecommendationReason.SameEvent, "shared story events"),
                ("collection", signals.CollectionScore, RecommendationReason.SameCollection, "same collection"),
            };
            var contributing = reasons.Where(r => r.Value > 0).OrderByDescending(r => r.Value * Weights[r.Key]).ToList();
            result.Add(new RecommendationCandidate(seriesId, target.Id, contributing.First().Reason,
                reasons.Sum(r => r.Value * Weights[r.Key]), string.Join(" · ", contributing.Take(3).Select(r => r.Text)), DateTime.UtcNow, signals));
        }
        return result.OrderByDescending(r => r.Score).ThenBy(r => r.TargetSeriesId).Take(limit).ToList();
    }

    public static decimal EraScore(int sourceYear, int targetYear)
    {
        int distance = Math.Abs(sourceYear - targetYear);
        return distance <= 5 ? 1 : distance >= 15 ? 0 : (15m - distance) / 10m;
    }
    private static decimal TagWeight(IssueTagWeight weight) => weight switch
    { IssueTagWeight.Incidental => .25m, IssueTagWeight.Recurrent => .5m, IssueTagWeight.Defining => .75m, IssueTagWeight.Core => 1m, _ => .5m };
    private static decimal Jaccard<T>(HashSet<T> a, HashSet<T> b)
    {
        int union = a.Union(b, a.Comparer).Count();
        return union == 0 ? 0 : (decimal)a.Intersect(b, a.Comparer).Count() / union;
    }
    private static decimal WeightedOverlap<T>(Dictionary<T, decimal> a, Dictionary<T, decimal> b, IEqualityComparer<T>? comparer = null) where T : notnull
    {
        decimal intersection = 0, union = 0;
        foreach (var key in a.Keys.Union(b.Keys, comparer))
        {
            decimal left = a.GetValueOrDefault(key), right = b.GetValueOrDefault(key);
            intersection += Math.Min(left, right); union += Math.Max(left, right);
        }
        return union == 0 ? 0 : intersection / union;
    }
    private static RecommendationReason RelationReason(RelationType? type) => type switch
    { RelationType.Prequel => RecommendationReason.Prequel, RelationType.Sequel => RecommendationReason.Sequel,
      RelationType.Crossover => RecommendationReason.Crossover, RelationType.SameUniverse or RelationType.SharedUniverse => RecommendationReason.SameUniverse,
      RelationType.SameContinuity => RecommendationReason.SameContinuity, _ => RecommendationReason.Related };
    private static string RelationText(RelationType? type) => type switch
    { RelationType.Prequel => "prequel", RelationType.Sequel => "sequel", RelationType.Crossover => "crossover",
      RelationType.SameUniverse or RelationType.SharedUniverse => "same universe", _ => "related series" };
}
