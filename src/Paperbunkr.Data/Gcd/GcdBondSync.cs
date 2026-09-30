using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Gcd;

/// <summary>What one <see cref="GcdBondSync.Run"/> pass changed.</summary>
public sealed record GcdBondSyncResult(int Added, int Removed);

/// <summary>
/// Turns Grand Comics Database series bonds between two matched library series into series relations with GCD evidence
/// (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4). "Source is the {type} of target", so the newer series is the source:
/// the numbering-continues bonds are <see cref="RelationType.Continuation"/>, a reboot is <see cref="RelationType.Reboot"/>, a merge
/// is <see cref="RelationType.Related"/>. Idempotent: a relation already there (either direction, same type) just gains GCD evidence,
/// GCD evidence whose bond is gone is dropped (and the relation with it when nothing else backs it), relations you made yourself keep
/// their own evidence, and pairs you deleted (<see cref="SeriesRelationDismissal"/>) stay deleted.
/// </summary>
public static class GcdBondSync
{
    /// <summary>How much a GCD bond is trusted: curated by GCD's indexers, so near-certain.</summary>
    public const decimal Confidence = 0.95m;

    public static RelationType? RelationFor(string bondType) => bondType switch
    {
        "minor_name_numbering_continues" or "major_name_numbering_continues" or "publisher_numbering_continues"
            or "subnumbering_continues" or "merge_numbering_continues" => RelationType.Continuation,
        "reboot" => RelationType.Reboot,
        "merge" => RelationType.Related,
        _ => null,
    };

    /// <param name="store">The installed extract; null (the data was removed) drops every GCD-only relation and all GCD evidence.</param>
    public static GcdBondSyncResult Run(Func<PaperbunkrDbContext> contextFactory, GcdDataStore? store)
    {
        using var context = contextFactory();
        var localByGcd = context.Series.AsNoTracking()
            .Where(s => s.GcdSeriesId != null)
            .Select(s => new { s.Id, Gcd = s.GcdSeriesId!.Value })
            .AsEnumerable()
            .GroupBy(s => s.Gcd)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList());
        var dismissed = context.SeriesRelationDismissals.AsNoTracking()
            .Select(d => new { d.LowerSeriesId, d.HigherSeriesId })
            .AsEnumerable()
            .Select(d => (d.LowerSeriesId, d.HigherSeriesId))
            .ToHashSet();

        var wanted = new Dictionary<(int Source, int Target, RelationType Type), GcdBond>();
        foreach (var bond in localByGcd.Count == 0 || store is null ? Array.Empty<GcdBond>() : store.BondsFor(localByGcd.Keys))
        {
            if (RelationFor(bond.BondType) is not RelationType type
                || !localByGcd.TryGetValue(bond.TargetId, out var newer)
                || !localByGcd.TryGetValue(bond.OriginId, out var older))
            {
                continue;
            }

            foreach (int source in newer)
            {
                foreach (int target in older)
                {
                    if (source != target && !dismissed.Contains((Math.Min(source, target), Math.Max(source, target))))
                    {
                        wanted.TryAdd((source, target, type), bond);
                    }
                }
            }
        }

        var existing = context.MediaRelations.Include(m => m.Evidence)
            .Where(m => m.SourceSeriesId != null && m.TargetSeriesId != null)
            .ToList();

        bool IsWanted(MediaRelation m) =>
            wanted.ContainsKey((m.SourceSeriesId!.Value, m.TargetSeriesId!.Value, m.RelationType))
            || wanted.ContainsKey((m.TargetSeriesId!.Value, m.SourceSeriesId!.Value, m.RelationType));

        int removed = 0;
        foreach (var relation in existing.Where(m => m.Evidence.Any(e => e.Provider == RelationEvidenceProvider.Gcd) && !IsWanted(m)).ToList())
        {
            foreach (var evidence in relation.Evidence.Where(e => e.Provider == RelationEvidenceProvider.Gcd).ToList())
            {
                relation.Evidence.Remove(evidence);
                context.RelationEvidence.Remove(evidence);
            }

            if (relation.Evidence.Count == 0)
            {
                context.MediaRelations.Remove(relation);
                existing.Remove(relation);
            }

            removed++;
        }

        int added = 0;
        foreach (var ((source, target, type), bond) in wanted)
        {
            var relation = existing.FirstOrDefault(m => m.RelationType == type
                && ((m.SourceSeriesId == source && m.TargetSeriesId == target) || (m.SourceSeriesId == target && m.TargetSeriesId == source)));
            if (relation is null)
            {
                relation = new MediaRelation { SourceSeriesId = source, TargetSeriesId = target, RelationType = type };
                context.MediaRelations.Add(relation);
                existing.Add(relation);
            }
            else if (relation.Evidence.Any(e => e.Provider == RelationEvidenceProvider.Gcd))
            {
                continue;
            }

            relation.Evidence.Add(Evidence(bond));
            added++;
        }

        context.SaveChanges();
        return new GcdBondSyncResult(added, removed);
    }

    private static RelationEvidence Evidence(GcdBond bond) => new()
    {
        Provider = RelationEvidenceProvider.Gcd,
        ProviderRelationType = bond.BondType,
        ProviderSourceId = bond.OriginId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Confidence = Confidence,
    };
}
