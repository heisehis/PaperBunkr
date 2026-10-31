using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.SeriesConnector;

/// <summary>
/// Turns Metron's associated series (stored <see cref="SeriesAssociation"/> rows) into Related candidates between two library series
/// (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.1). An association names only a Metron series id, so the
/// local series is found by, in order: that Metron id; the associated series' ComicVine id; its Grand Comics Database id (both read
/// from its Metron record by <see cref="MetronAssociationFetch"/>). The first step that finds anything decides - exactly one local series
/// is a match, two or more (a split series) is no match rather than a guess. No network.
/// </summary>
public static class MetronAssociationMapping
{
    public const decimal Confidence = 0.85m;

    /// <summary>What <see cref="RelationEvidence.ProviderRelationType"/> says on Metron evidence this writes, telling it apart from other Metron evidence.</summary>
    public const string ProviderRelationType = "associated";

    public static IReadOnlyList<InferredSeriesRelation> Map(PaperbunkrDbContext context, SeriesConnectorSnapshot snapshot)
    {
        var associations = context.SeriesAssociations.AsNoTracking().Where(a => a.Provider == ComicProvider.Metron).ToList();
        if (associations.Count == 0)
        {
            return [];
        }

        var byMetron = SeriesIdsByExternalId(context, ComicProvider.Metron);
        var byComicVine = SeriesIdsByExternalId(context, ComicProvider.ComicVine);
        var byGcd = context.Series.AsNoTracking().Where(s => s.GcdSeriesId != null)
            .Select(s => new { s.Id, Gcd = s.GcdSeriesId!.Value })
            .AsEnumerable()
            .GroupBy(s => s.Gcd.ToString(CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.Select(s => s.Id).Distinct().ToList());

        var results = new List<InferredSeriesRelation>();
        foreach (var association in associations)
        {
            int? target = Match(association, byMetron, byComicVine, byGcd);
            if (target is not int targetId
                || !snapshot.Series.TryGetValue(association.SeriesId, out var source)
                || !snapshot.Series.TryGetValue(targetId, out var other)
                || !SeriesConnectorGuards.SameContentFamily(source, other)
                || SeriesConnectorGuards.IsBlocked(snapshot, source.Id, targetId))
            {
                continue;
            }

            results.Add(new InferredSeriesRelation(source.Id, targetId, RelationType.Related, Confidence, "Metron",
                RelationEvidenceProvider.Metron, association.ExternalSeriesId));
        }

        return results;
    }

    internal static int? Match(
        SeriesAssociation association,
        IReadOnlyDictionary<string, List<int>> byMetron,
        IReadOnlyDictionary<string, List<int>> byComicVine,
        IReadOnlyDictionary<string, List<int>> byGcd)
    {
        foreach (var (lookup, key) in new[]
        {
            (byMetron, association.ExternalSeriesId),
            (byComicVine, association.ComicVineSeriesId?.ToString(CultureInfo.InvariantCulture)),
            (byGcd, association.GcdSeriesId?.ToString(CultureInfo.InvariantCulture)),
        })
        {
            if (key is null || !lookup.TryGetValue(key, out var ids))
            {
                continue;
            }

            var others = ids.Where(id => id != association.SeriesId).ToList();
            return others.Count == 1 ? others[0] : null;
        }

        return null;
    }

    /// <summary>Local series ids by their provider series id (the scraper's <see cref="ComicMetadataExternalId"/> rows).</summary>
    public static Dictionary<string, List<int>> SeriesIdsByExternalId(PaperbunkrDbContext context, ComicProvider provider) =>
        context.ComicMetadataExternalIds.AsNoTracking()
            .Where(x => x.EntityKind == ComicMetadataEntityKind.Series && x.Provider == provider)
            .Select(x => new { x.ExternalId, x.EntityId })
            .AsEnumerable()
            .GroupBy(x => x.ExternalId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.EntityId).Distinct().ToList());
}
