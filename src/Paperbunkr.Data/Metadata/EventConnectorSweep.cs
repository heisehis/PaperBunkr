using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

public sealed record EventConnectorSummary(int Created, int Updated, int Removed)
{
    public string Text => $"{Created} connection{(Created == 1 ? "" : "s")} found · {Removed} removed";
}

/// <summary>
/// Saves what the smart connector works out (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §4): the Metron reading-list
/// chain (<see cref="EventOrderEvidence"/> rows written by <see cref="EventOrderSweep"/>) becomes <see cref="RelationEvidenceProvider.Metron"/>
/// relations, and strong local inferences <see cref="RelationEvidenceProvider.Inferred"/> ones. Wikidata is no longer asked: relations that only
/// Wikidata ever supported are removed. Your own relations are never touched and block inference for their pair; dismissed pairs are skipped;
/// an automatic relation whose evidence has gone is deleted. Runs after the identity step so two halves of a duplicate are never linked.
/// </summary>
public static class EventConnectorSweep
{
    public const decimal MetronConfidence = 0.85m;

    public static Task<EventConnectorSummary> RunAsync(Func<PaperbunkrDbContext> contextFactory, CancellationToken cancellationToken, Gcd.GcdDataStore? gcd = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var context = contextFactory();
        int created = 0, updated = 0, removed = 0;
        var blocked = BlockedPairs(context);

        // Wikidata is out of the picture (§3): what only it supported goes, once and for good.
        foreach (var r in RelationsWith(context, RelationEvidenceProvider.Wikidata).ToList())
        {
            context.EventRelations.Remove(r);
            removed++;
        }

        // Metron reading lists: "X's list comes before Y's list" → X Prequel of Y.
        var metronPairs = new HashSet<(int, int)>();
        var evidenceRows = context.EventOrderEvidence.AsNoTracking()
            .Where(e => e.Provider == EventOrderProvider.MetronList && e.SourceId != null && (e.PreviousName != null || e.NextName != null))
            .ToList();
        if (evidenceRows.Count > 0)
        {
            var events = EventOrderSweep.LoadEvents(context);
            foreach (var row in evidenceRows)
            {
                if (ResolveNeighbour(row.NextName, row.StoryEventId, events) is int later)
                {
                    AddMetronLink(row.StoryEventId, later, row);
                }

                if (ResolveNeighbour(row.PreviousName, row.StoryEventId, events) is int earlier)
                {
                    AddMetronLink(earlier, row.StoryEventId, row);
                }
            }

            void AddMetronLink(int earlierId, int laterId, EventOrderEvidence row)
            {
                var pair = Pair(earlierId, laterId);
                if (!metronPairs.Add(pair) || blocked.Contains(pair))
                {
                    return;
                }

                var (c, u) = Upsert(context, RelationEvidenceProvider.Metron, earlierId, laterId, RelationType.Prequel,
                    MetronConfidence, reason: $"Metron reading list “{row.SourceName}”: the next list in reading order", sourceId: row.SourceId);
                created += c;
                updated += u;
            }
        }

        foreach (var r in RelationsWith(context, RelationEvidenceProvider.Metron).ToList())
        {
            if (!metronPairs.Contains(Pair(r.SourceEventId, r.TargetEventId)))
            {
                context.EventRelations.Remove(r);
                removed++;
            }
        }

        context.SaveChanges();

        // Local inference: strong ones only; a pair already carrying a Metron relation keeps it.
        var strong = EventChronologyInference.Infer(context, gcd: gcd).Where(i => i.IsStrong && !metronPairs.Contains(i.Pair) && !blocked.Contains(i.Pair)).ToList();
        foreach (var inference in strong)
        {
            var (c, u) = Upsert(context, RelationEvidenceProvider.Inferred, inference.SourceEventId, inference.TargetEventId, inference.Type,
                inference.Confidence, inference.Reason, sourceId: null);
            created += c;
            updated += u;
        }

        var keep = strong.Select(i => i.Pair).ToHashSet();
        foreach (var r in RelationsWith(context, RelationEvidenceProvider.Inferred).ToList())
        {
            if (!keep.Contains(Pair(r.SourceEventId, r.TargetEventId)))
            {
                context.EventRelations.Remove(r);
                removed++;
            }
        }

        context.SaveChanges();
        return Task.FromResult(new EventConnectorSummary(created, updated, removed));
    }

    /// <summary>The one library event a neighbouring reading list's name is, other than <paramref name="selfId"/>; null when none or several are.</summary>
    private static int? ResolveNeighbour(string? listName, int selfId, IReadOnlyList<EventOrderSweep.EventInfo> events)
    {
        if (string.IsNullOrWhiteSpace(listName))
        {
            return null;
        }

        var keys = EventNameKeys.For(listName, Array.Empty<string>());
        var matches = events.Where(e => e.Id != selfId && e.Keys.Overlaps(keys)).Select(e => e.Id).Distinct().ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Records "these two aren't connected" - called when you delete an inferred or Wikidata relation or dismiss a connector suggestion.
    /// </summary>
    public static void Dismiss(PaperbunkrDbContext context, int eventAId, int eventBId)
    {
        var (low, high) = Pair(eventAId, eventBId);
        if (low == high || context.EventRelationDismissals.Any(d => d.LowerEventId == low && d.HigherEventId == high))
        {
            return;
        }

        context.EventRelationDismissals.Add(new EventRelationDismissal { LowerEventId = low, HigherEventId = high });
        context.SaveChanges();
    }

    /// <summary>True for a relation the smart connector made (inferred, Metron or - from before it was dropped - Wikidata) rather than you.</summary>
    public static bool IsAutomatic(EventRelation relation) =>
        relation.Evidence.Count > 0
        && relation.Evidence.All(e => e.Provider is RelationEvidenceProvider.Inferred or RelationEvidenceProvider.Wikidata or RelationEvidenceProvider.Metron);

    /// <summary>Removes a relation; if the smart connector made it, the pair is dismissed so it's never re-created.</summary>
    public static void RemoveRelation(PaperbunkrDbContext context, int eventRelationId)
    {
        var relation = context.EventRelations.Include(r => r.Evidence).FirstOrDefault(r => r.Id == eventRelationId);
        if (relation is null)
        {
            return;
        }

        if (IsAutomatic(relation))
        {
            Dismiss(context, relation.SourceEventId, relation.TargetEventId);
        }

        context.EventRelations.Remove(relation);
        context.SaveChanges();
    }

    private static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>Pairs the connector must not touch: ones with a relation you set (User evidence, or legacy rows with none) and dismissed ones.</summary>
    private static HashSet<(int, int)> BlockedPairs(PaperbunkrDbContext context)
    {
        var set = context.EventRelations.AsNoTracking()
            .Where(r => r.Evidence.Any(e => e.Provider == RelationEvidenceProvider.User || e.Provider == RelationEvidenceProvider.Other) || !r.Evidence.Any())
            .Select(r => new { r.SourceEventId, r.TargetEventId })
            .AsEnumerable()
            .Select(r => Pair(r.SourceEventId, r.TargetEventId))
            .ToHashSet();
        foreach (var d in context.EventRelationDismissals.AsNoTracking().Select(d => new { d.LowerEventId, d.HigherEventId }).ToList())
        {
            set.Add((d.LowerEventId, d.HigherEventId));
        }

        return set;
    }

    private static IQueryable<EventRelation> RelationsWith(PaperbunkrDbContext context, RelationEvidenceProvider provider) =>
        context.EventRelations.Include(r => r.Evidence)
            .Where(r => r.Evidence.Any() && r.Evidence.All(e => e.Provider == provider));

    /// <summary>Creates, or re-points/re-types, the one relation this provider owns for the pair. Returns (created, updated).</summary>
    private static (int Created, int Updated) Upsert(
        PaperbunkrDbContext context, RelationEvidenceProvider provider, int sourceEventId, int targetEventId, RelationType type,
        decimal confidence, string reason, string? sourceId)
    {
        string? evidenceSourceId = sourceId;
        var (low, high) = Pair(sourceEventId, targetEventId);
        var existing = RelationsWith(context, provider)
            .FirstOrDefault(r => (r.SourceEventId == low && r.TargetEventId == high) || (r.SourceEventId == high && r.TargetEventId == low));
        string storedReason = reason.Length > 200 ? reason[..200] : reason;

        if (existing is null)
        {
            var relation = new EventRelation { SourceEventId = sourceEventId, TargetEventId = targetEventId, RelationType = type };
            relation.Evidence.Add(new EventRelationEvidence
            {
                EventRelation = relation, Provider = provider, Confidence = confidence,
                ProviderRelationType = storedReason, ProviderSourceId = evidenceSourceId,
            });
            context.EventRelations.Add(relation);
            return (1, 0);
        }

        var evidence = existing.Evidence.First();
        bool changed = existing.SourceEventId != sourceEventId || existing.RelationType != type
                       || evidence.ProviderRelationType != storedReason || evidence.Confidence != confidence;
        existing.SourceEventId = sourceEventId;
        existing.TargetEventId = targetEventId;
        existing.RelationType = type;
        evidence.ProviderRelationType = storedReason;
        evidence.ProviderSourceId = evidenceSourceId;
        evidence.Confidence = confidence;
        evidence.RetrievedAt = DateTime.UtcNow;
        return changed ? (0, 1) : (0, 0);
    }
}
