using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

public sealed record EventConnectorSummary(int Created, int Updated, int Removed, int WikidataChecked, int WikidataMatched)
{
    public string Text => $"{Created} connection{(Created == 1 ? "" : "s")} found · {Removed} removed"
                          + (WikidataChecked > 0 ? $" · {WikidataMatched} of {WikidataChecked} events matched on Wikidata" : string.Empty);
}

/// <summary>
/// Saves what the smart connector works out (docs/superpowers/specs/2026-09-27-continuity-map-design.md §2): strong local inferences
/// become <see cref="RelationEvidenceProvider.Inferred"/> relations, Wikidata links <see cref="RelationEvidenceProvider.Wikidata"/>
/// ones. Your own relations are never touched and block inference for their pair; dismissed pairs are skipped; an inferred relation
/// whose evidence has gone is deleted. Runs after the identity step so two halves of a duplicate are never linked.
/// </summary>
public static class EventConnectorSweep
{
    public const decimal WikidataConfidence = 0.9m;

    public static async Task<EventConnectorSummary> RunAsync(
        Func<PaperbunkrDbContext> contextFactory, IWikidataLookup? wikidata, int wikidataBatchSize, CancellationToken cancellationToken,
        Gcd.GcdDataStore? gcd = null)
    {
        WikidataLinksResult? wd = null;
        if (wikidata is not null)
        {
            wd = await WikidataEventLinks.RunAsync(contextFactory, wikidata, wikidataBatchSize, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        using var context = contextFactory();
        int created = 0, updated = 0, removed = 0;
        var blocked = BlockedPairs(context);

        // Wikidata: A followed by B → A Prequel of B.
        if (wd is not null)
        {
            var found = new HashSet<(int, int)>();
            foreach (var link in wd.Links)
            {
                var pair = Pair(link.EarlierEventId, link.LaterEventId);
                found.Add(pair);
                if (blocked.Contains(pair))
                {
                    continue;
                }

                var (c, u) = Upsert(context, RelationEvidenceProvider.Wikidata, link.EarlierEventId, link.LaterEventId, RelationType.Prequel,
                    WikidataConfidence, reason: "Wikidata: followed by", sourceId: link.EvidenceQid);
                created += c;
                updated += u;
            }

            // A Wikidata relation touching a re-checked event that Wikidata no longer supports goes.
            var checkedIds = wd.CheckedEventIds.ToHashSet();
            foreach (var r in RelationsWith(context, RelationEvidenceProvider.Wikidata)
                         .Where(r => checkedIds.Contains(r.SourceEventId) || checkedIds.Contains(r.TargetEventId)).ToList())
            {
                if (!found.Contains(Pair(r.SourceEventId, r.TargetEventId)))
                {
                    context.EventRelations.Remove(r);
                    removed++;
                }
            }

            context.SaveChanges();
        }

        // Local inference: strong ones only; a pair already carrying a Wikidata relation keeps it.
        var wikidataPairs = RelationsWith(context, RelationEvidenceProvider.Wikidata).Select(r => Pair(r.SourceEventId, r.TargetEventId)).ToHashSet();
        var strong = EventChronologyInference.Infer(context, gcd: gcd).Where(i => i.IsStrong && !wikidataPairs.Contains(i.Pair) && !blocked.Contains(i.Pair)).ToList();
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
        return new EventConnectorSummary(created, updated, removed, wd?.Checked ?? 0, wd?.Matched ?? 0);
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

    /// <summary>True for a relation the smart connector made (inferred or Wikidata) rather than you.</summary>
    public static bool IsAutomatic(EventRelation relation) =>
        relation.Evidence.Count > 0 && relation.Evidence.All(e => e.Provider is RelationEvidenceProvider.Inferred or RelationEvidenceProvider.Wikidata);

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
