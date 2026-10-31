using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>What one <see cref="SeriesConnectorSweep.Run"/> changed: relations made, relations whose evidence changed, relations deleted.</summary>
public sealed record SeriesConnectorSummary(int Created, int Updated, int Removed)
{
    public bool Changed => Created + Removed > 0;

    public string Text => !Changed
        ? "No new relations"
        : $"{Created} relation{(Created == 1 ? "" : "s")} found · {Removed} removed";
}

/// <summary>
/// Saves what the series connector works out (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §5): the local rules
/// (later volumes, companions, team-ups, event tie-ins and crossovers) as <see cref="RelationEvidenceProvider.Inferred"/> evidence and
/// Metron's associated series (already fetched) as <see cref="RelationEvidenceProvider.Metron"/> "associated" evidence. Same shape as
/// <see cref="Gcd.GcdBondSync"/> and <see cref="EventConnectorSweep"/>: a relation already there (same type, either direction) just gains
/// evidence; evidence this sweep owns that no longer holds is dropped, and a relation left with none is deleted; GCD, Wikidata and your
/// own evidence are never touched; your pairs and dismissed pairs are skipped. No network.
/// </summary>
public static class SeriesConnectorSweep
{
    /// <summary>Every local rule's candidates for the snapshot, automatic ones only (confidence at least <see cref="InferredSeriesRelation.AutomaticThreshold"/>).</summary>
    public static IReadOnlyList<InferredSeriesRelation> InferLocal(SeriesConnectorSnapshot snapshot) =>
        VolumeChainInference.Infer(snapshot)
            .Concat(CompanionSuffixInference.Infer(snapshot))
            .Concat(SlashTitleInference.Infer(snapshot))
            .Concat(EventTieInInference.Infer(snapshot))
            .Where(r => r.Confidence >= InferredSeriesRelation.AutomaticThreshold)
            .ToList();

    public static SeriesConnectorSummary Run(Func<PaperbunkrDbContext> contextFactory)
    {
        using var context = contextFactory();
        var snapshot = SeriesConnectorSnapshot.Load(context);
        var wanted = InferLocal(snapshot).Concat(MetronAssociationMapping.Map(context, snapshot)).ToList();
        return Apply(context, wanted);
    }

    /// <summary>Makes the saved relations match <paramref name="wanted"/> for the evidence this sweep owns. Public for tests.</summary>
    public static SeriesConnectorSummary Apply(PaperbunkrDbContext context, IReadOnlyList<InferredSeriesRelation> wanted)
    {
        // One candidate per (pair, type, provider, source id); the strongest wins when two rules say the same thing.
        var byKey = new Dictionary<(int Low, int High, RelationType Type, RelationEvidenceProvider Provider, string? SourceId), InferredSeriesRelation>();
        foreach (var w in wanted)
        {
            var key = (w.Pair.Low, w.Pair.High, w.Type, w.Provider, w.ProviderSourceId);
            if (!byKey.TryGetValue(key, out var existing) || w.Confidence > existing.Confidence)
            {
                byKey[key] = w;
            }
        }

        var relations = context.MediaRelations.Include(m => m.Evidence)
            .Where(m => m.SourceSeriesId != null && m.TargetSeriesId != null)
            .ToList();
        int created = 0, updated = 0, removed = 0;

        foreach (var w in byKey.Values)
        {
            var relation = relations.FirstOrDefault(m => m.RelationType == w.Type
                && SeriesConnectorGuards.Pair(m.SourceSeriesId!.Value, m.TargetSeriesId!.Value) == w.Pair);
            bool isNew = relation is null;
            if (relation is null)
            {
                relation = new MediaRelation { SourceSeriesId = w.SourceSeriesId, TargetSeriesId = w.TargetSeriesId, RelationType = w.Type };
                context.MediaRelations.Add(relation);
                relations.Add(relation);
            }

            string providerType = ProviderTypeFor(w);
            var evidence = relation.Evidence.FirstOrDefault(e => IsOwned(e) && e.Provider == w.Provider && e.ProviderSourceId == w.ProviderSourceId);
            if (evidence is null)
            {
                relation.Evidence.Add(new RelationEvidence
                {
                    MediaRelation = relation, Provider = w.Provider, ProviderRelationType = providerType, ProviderSourceId = w.ProviderSourceId,
                    Confidence = w.Confidence, RetrievedAt = DateTime.UtcNow,
                });
                if (isNew)
                {
                    created++;
                }
                else
                {
                    updated++;
                }
            }
            else if (evidence.ProviderRelationType != providerType || evidence.Confidence != w.Confidence)
            {
                evidence.ProviderRelationType = providerType;
                evidence.Confidence = w.Confidence;
                evidence.RetrievedAt = DateTime.UtcNow;
                updated++;
            }
        }

        var keep = byKey.Values
            .Select(w => (w.Pair, w.Type, w.Provider, w.ProviderSourceId))
            .ToHashSet();
        foreach (var relation in relations.ToList())
        {
            var pair = SeriesConnectorGuards.Pair(relation.SourceSeriesId!.Value, relation.TargetSeriesId!.Value);
            var stale = relation.Evidence.Where(e => IsOwned(e) && !keep.Contains((pair, relation.RelationType, e.Provider, e.ProviderSourceId))).ToList();
            if (stale.Count == 0)
            {
                continue;
            }

            foreach (var evidence in stale)
            {
                relation.Evidence.Remove(evidence);
                if (evidence.Id != 0)
                {
                    context.RelationEvidence.Remove(evidence);
                }
            }

            if (relation.Evidence.Count == 0)
            {
                context.MediaRelations.Remove(relation);
                removed++;
            }
            else
            {
                updated++;
            }
        }

        context.SaveChanges();

        // A sweep follows every scan and scrape, which is what changes the names, years and characters suggestions read.
        SeriesRelationSuggestions.Invalidate();
        return new SeriesConnectorSummary(created, updated, removed);
    }

    /// <summary>Evidence this sweep wrote and may prune: every inferred row, and Metron's "associated" rows.</summary>
    public static bool IsOwned(RelationEvidence evidence) =>
        evidence.Provider == RelationEvidenceProvider.Inferred
        || (evidence.Provider == RelationEvidenceProvider.Metron && evidence.ProviderRelationType == MetronAssociationMapping.ProviderRelationType);

    /// <summary>Inferred evidence keeps the card's reason ("Later volume", "Tie-in · #33–34"); Metron's says "associated".</summary>
    private static string ProviderTypeFor(InferredSeriesRelation w) =>
        w.Provider == RelationEvidenceProvider.Metron ? MetronAssociationMapping.ProviderRelationType : w.Reason;
}
