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
            // Grouped: two library series can share one GCD id (a wrong Metron gcd_id, a duplicated series row), as in GcdBondSync.
            : Placeholders(context, gcd, gcdIds.GroupBy(x => x.Gcd).ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(x => x.Id).ToList()));

        // "Connections worth confirming" were the smart connector's weak guesses; those were dropped on 2026-10-12 (a date order alone is
        // placement, not a link: docs/superpowers/specs/2026-10-12-continuity-sources-design.md §4.2), so there is nothing to offer here yet.
        var connections = new List<OverviewConnection>();

        return ContinuityOverviewBuilder.Build(new ContinuityOverviewInput(map, continuations, placeholders, connections, gcdIds.Count > 0,
            LoadEntities(context, memberIds, ComicMetadataEntityKind.Character), LoadEntities(context, memberIds, ComicMetadataEntityKind.Team)));
    }

    public static IReadOnlyList<OverviewEntity> LoadEntities(PaperbunkrDbContext context, IReadOnlyList<int> memberIds, ComicMetadataEntityKind kind)
    {
        var appearanceRows = kind == ComicMetadataEntityKind.Character
            ? context.CharacterAppearances.Where(a => memberIds.Contains(a.Issue!.SeriesId) && !a.Issue.IsPlaceholder).Select(a => new { Id = a.CharacterId, a.IssueId }).ToList()
            : context.TeamAppearances.Where(a => memberIds.Contains(a.Issue!.SeriesId) && !a.Issue.IsPlaceholder).Select(a => new { Id = a.TeamId, a.IssueId }).ToList();
        var ranked = appearanceRows.GroupBy(a => a.Id).OrderByDescending(g => g.Select(a => a.IssueId).Distinct().Count()).ThenBy(g => g.Key).Take(10).ToList();
        var issueIds = appearanceRows.Select(a => a.IssueId).Distinct().ToList();
        var issues = context.Issues.AsNoTracking().Include(i => i.MetadataProposals).Where(i => issueIds.Contains(i.Id)).ToDictionary(i => i.Id);
        var result = new List<OverviewEntity>();
        foreach (var group in ranked)
        {
            var entity = EntityProfileCompletion.Find(context, kind, group.Key);
            string name = kind == ComicMetadataEntityKind.Character ? context.Characters.Find(group.Key)!.Name : context.Teams.Find(group.Key)!.Name;
            var first = group.Select(a => issues[a.IssueId]).OrderBy(i => i.EffectiveYear() ?? int.MaxValue)
                .ThenBy(i => i.Month ?? 0).ThenBy(i => i.Day ?? 0).ThenBy(i => i.Id).First();
            var image = EntityImageResolver.Choose(name, entity?.CustomImagePath, entity?.ImageUrl, first.Id);
            string? cover = image.CoverIssueId is int coverId && CoverThumbnailService.GetEffectiveCoverPath(coverId) is not null ? coverId.ToString() : null;
            result.Add(new OverviewEntity(kind, group.Key, name, group.Select(a => a.IssueId).Distinct().Count(), image.Path, cover));
        }
        return result;
    }

    /// <summary>GCD continuation bonds (numbering continues) from a member series to a GCD series nobody in the library is matched to.</summary>
    private static List<OverviewPlaceholder> Placeholders(PaperbunkrDbContext context, GcdDataStore gcd, IReadOnlyDictionary<int, IReadOnlyList<int>> ownedByGcd)
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
            if (ownedByGcd.TryGetValue(owned, out var ownedSeriesIds) && series.TryGetValue(other, out var s))
            {
                foreach (int ownedSeriesId in ownedSeriesIds)
                {
                    result.Add(new OverviewPlaceholder(other, s.Name, s.YearBegan, ownedSeriesId, ownedIsOlder));
                }
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
