using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.ContinuityScreen;

/// <summary>
/// The database side of <see cref="ContinuityOverviewBuilder"/>: the continuity map's data (which already carries every member-series
/// issue with its read state and GCD date), the Continuation relations between members, GCD bonds to series you don't own, and the
/// smart connector's weak inferences between the continuity's own events. Meant to run off the UI thread with a context of its own.
/// </summary>
public static class ContinuityOverviewLoader
{
    public static ContinuityOverview? Load(PaperbunkrDbContext context, int continuityId, GcdDataStore? gcd)
    {
        var map = ContinuityMapLoader.LoadData(context, continuityId, gcd);
        if (map is null)
        {
            return null;
        }

        var memberIds = map.MemberLanes.Select(l => l.SeriesId).ToList();
        var continuations = context.MediaRelations.AsNoTracking()
            .Where(r => r.RelationType == RelationType.Continuation && r.SourceSeriesId != null && r.TargetSeriesId != null
                        && memberIds.Contains(r.SourceSeriesId.Value) && memberIds.Contains(r.TargetSeriesId.Value))
            .Select(r => new { Newer = r.SourceSeriesId!.Value, Older = r.TargetSeriesId!.Value })
            .AsEnumerable()
            .Select(r => (r.Newer, r.Older))
            .ToList();

        var gcdIds = context.Series.AsNoTracking()
            .Where(s => memberIds.Contains(s.Id) && s.GcdSeriesId != null)
            .Select(s => new { s.Id, Gcd = s.GcdSeriesId!.Value })
            .ToList();

        var placeholders = gcd is null || gcdIds.Count == 0
            ? new List<OverviewPlaceholder>()
            : Placeholders(context, gcd, gcdIds.ToDictionary(x => x.Gcd, x => x.Id));

        var eventIds = map.Events.Select(e => e.Id).ToHashSet();
        var connections = new List<OverviewConnection>();
        if (eventIds.Count >= 2)
        {
            var names = map.Events.ToDictionary(e => e.Id, e => e.Name);
            foreach (var inference in EventChronologyInference.Infer(context, null, gcd)
                         .Where(i => !i.IsStrong && eventIds.Contains(i.SourceEventId) && eventIds.Contains(i.TargetEventId)))
            {
                connections.Add(new OverviewConnection(inference.SourceEventId, inference.TargetEventId, inference.Type,
                    Headline(inference, names), inference.Reason));
            }
        }

        return ContinuityOverviewBuilder.Build(new ContinuityOverviewInput(map, continuations, placeholders, connections, gcdIds.Count > 0));
    }

    /// <summary>GCD continuation bonds (numbering continues) from a member series to a GCD series nobody in the library is matched to.</summary>
    private static List<OverviewPlaceholder> Placeholders(PaperbunkrDbContext context, GcdDataStore gcd, IReadOnlyDictionary<int, int> ownedByGcd)
    {
        var bonds = gcd.BondsFor(ownedByGcd.Keys)
            .Where(b => GcdBondSync.RelationFor(b.BondType) == RelationType.Continuation)
            .ToList();
        var otherIds = bonds.SelectMany(b => new[] { b.OriginId, b.TargetId }).Where(id => !ownedByGcd.ContainsKey(id)).Distinct().ToList();
        if (otherIds.Count == 0)
        {
            return new List<OverviewPlaceholder>();
        }

        var inLibrary = context.Series.AsNoTracking()
            .Where(s => s.GcdSeriesId != null && otherIds.Contains(s.GcdSeriesId.Value))
            .Select(s => s.GcdSeriesId!.Value)
            .ToHashSet();
        var series = gcd.GetSeries(otherIds.Where(id => !inLibrary.Contains(id))).ToDictionary(s => s.Id);

        var result = new List<OverviewPlaceholder>();
        foreach (var bond in bonds)
        {
            bool ownedIsOlder = ownedByGcd.ContainsKey(bond.OriginId);
            int owned = ownedIsOlder ? bond.OriginId : bond.TargetId;
            int other = ownedIsOlder ? bond.TargetId : bond.OriginId;
            if (ownedByGcd.TryGetValue(owned, out int ownedSeriesId) && series.TryGetValue(other, out var s))
            {
                result.Add(new OverviewPlaceholder(other, s.Name, s.YearBegan, ownedSeriesId, ownedIsOlder));
            }
        }

        return result;
    }

    /// <summary>"Planet Hulk looks like the prequel of World War Hulk" - both names, since the flyout isn't on either event's page.</summary>
    internal static string Headline(InferredEventRelation inference, IReadOnlyDictionary<int, string> names)
    {
        string source = names.GetValueOrDefault(inference.SourceEventId, "An event");
        string target = names.GetValueOrDefault(inference.TargetEventId, "another event");
        string relation = inference.Type switch
        {
            RelationType.Crossover => "looks like a crossover with",
            RelationType.Sequel => "looks like the sequel of",
            RelationType.Prequel => "looks like the prequel of",
            RelationType.Continuation => "looks like it continues",
            _ => "looks related to",
        };
        return $"{source} {relation} {target}";
    }
}
