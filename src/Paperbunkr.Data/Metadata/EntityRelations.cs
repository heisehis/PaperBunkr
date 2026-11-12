using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>Another entity seen in (or credited on) the same issues as the page's entity. <see cref="FirstIssueId"/> is the earliest shared issue in
/// the page's reading order (the tile's fallback inset cover).</summary>
public sealed record EntityRelation(ComicMetadataEntityKind Kind, int Id, string Name, int SharedCount, string? CustomImagePath, string? ImageUrl, int? FirstIssueId);

/// <summary>One run on a creator's career timeline (docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md §4.3).</summary>
public sealed record CareerRun(int SeriesId, string Series, string Role, string Range, int? FirstYear, int? LastYear, IReadOnlyList<int> IssueIds, int ReadCount);

/// <summary>
/// The relation queries beside <see cref="EntityPageResolver"/> (entity pages redesign §6). Every query takes the page's <em>already filtered</em>
/// reading order (continuity / role applied once), so the strips agree with the tabs.
/// </summary>
public static class EntityRelations
{
    /// <summary>"Often appears with" (character, team), "Who's been here" (location), a publisher's characters. A team page lists characters first
    /// (its de-facto roster), then teams; otherwise both kinds are ranked together. The page's own entity is never listed.</summary>
    public static IReadOnlyList<EntityRelation> Seen(PaperbunkrDbContext context, IReadOnlyList<int> readingOrder, int take,
        bool includeTeams = true, (ComicMetadataEntityKind Kind, int Id)? exclude = null, bool charactersFirst = false)
    {
        if (readingOrder.Count == 0 || take <= 0) return Array.Empty<EntityRelation>();
        var ids = readingOrder.Distinct().ToList();
        int skipCharacter = exclude is { Kind: ComicMetadataEntityKind.Character } c ? c.Id : 0;
        int skipTeam = exclude is { Kind: ComicMetadataEntityKind.Team } t ? t.Id : 0;

        var characterRows = context.CharacterAppearances.AsNoTracking().Where(a => ids.Contains(a.IssueId) && a.CharacterId != skipCharacter)
            .Select(a => new { a.CharacterId, a.IssueId }).ToList()
            .Select(a => (Kind: ComicMetadataEntityKind.Character, Id: a.CharacterId, a.IssueId));
        var teamRows = includeTeams
            ? context.TeamAppearances.AsNoTracking().Where(a => ids.Contains(a.IssueId) && a.TeamId != skipTeam)
                .Select(a => new { a.TeamId, a.IssueId }).ToList().Select(a => (Kind: ComicMetadataEntityKind.Team, Id: a.TeamId, a.IssueId))
            : Enumerable.Empty<(ComicMetadataEntityKind Kind, int Id, int IssueId)>();

        var order = Order(readingOrder);
        var ranked = characterRows.Concat(teamRows)
            .GroupBy(r => (r.Kind, r.Id))
            .Select(g => (g.Key.Kind, g.Key.Id, Shared: g.Select(r => r.IssueId).Distinct().Count(), First: g.Select(r => r.IssueId).MinBy(i => order[i])))
            .OrderBy(r => charactersFirst && r.Kind != ComicMetadataEntityKind.Character ? 1 : 0)
            .ThenByDescending(r => r.Shared).ThenBy(r => r.Kind).ThenBy(r => r.Id)
            .Take(take).ToList();

        var characterIds = ranked.Where(r => r.Kind == ComicMetadataEntityKind.Character).Select(r => r.Id).ToList();
        var teamIds = ranked.Where(r => r.Kind == ComicMetadataEntityKind.Team).Select(r => r.Id).ToList();
        var profiles = context.Characters.AsNoTracking().Where(e => characterIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Name, e.CustomImagePath, e.ImageUrl }).ToList()
            .ToDictionary(e => (ComicMetadataEntityKind.Character, e.Id), e => (e.Name, e.CustomImagePath, e.ImageUrl));
        foreach (var e in context.Teams.AsNoTracking().Where(e => teamIds.Contains(e.Id)).Select(e => new { e.Id, e.Name, e.CustomImagePath, e.ImageUrl }).ToList())
            profiles[(ComicMetadataEntityKind.Team, e.Id)] = (e.Name, e.CustomImagePath, e.ImageUrl);

        return ranked.Where(r => profiles.ContainsKey((r.Kind, r.Id))).Select(r =>
        {
            var p = profiles[(r.Kind, r.Id)];
            return new EntityRelation(r.Kind, r.Id, p.Name, r.Shared, p.CustomImagePath, p.ImageUrl, r.First);
        }).ToList();
    }

    /// <summary>"Often works with": other creators credited on the same issues, by shared-issue count.</summary>
    public static IReadOnlyList<EntityRelation> Collaborators(PaperbunkrDbContext context, int creatorId, IReadOnlyList<int> readingOrder, int take)
    {
        if (readingOrder.Count == 0 || take <= 0) return Array.Empty<EntityRelation>();
        var ids = readingOrder.Distinct().ToList();
        var order = Order(readingOrder);
        var ranked = context.CreatorCredits.AsNoTracking().Where(c => ids.Contains(c.IssueId) && c.CreatorId != creatorId)
            .Select(c => new { c.CreatorId, c.IssueId }).ToList()
            .GroupBy(c => c.CreatorId)
            .Select(g => (Id: g.Key, Shared: g.Select(c => c.IssueId).Distinct().Count(), First: g.Select(c => c.IssueId).MinBy(i => order[i])))
            .OrderByDescending(r => r.Shared).ThenBy(r => r.Id).Take(take).ToList();
        var creatorIds = ranked.Select(r => r.Id).ToList();
        var profiles = context.Creators.AsNoTracking().Where(e => creatorIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Name, e.CustomImagePath, e.ImageUrl }).ToList().ToDictionary(e => e.Id);
        return ranked.Where(r => profiles.ContainsKey(r.Id)).Select(r => new EntityRelation(ComicMetadataEntityKind.Creator, r.Id, profiles[r.Id].Name,
            r.Shared, profiles[r.Id].CustomImagePath, profiles[r.Id].ImageUrl, r.First)).ToList();
    }

    /// <summary>A creator's runs (<see cref="CreatorRunDetector.FindAll"/>) over the series of <paramref name="readingOrder"/>, ordered by the year they start.</summary>
    public static IReadOnlyList<CareerRun> CreatorCareer(PaperbunkrDbContext context, int creatorId, IReadOnlyList<int> readingOrder)
    {
        if (readingOrder.Count == 0) return Array.Empty<CareerRun>();
        var ids = readingOrder.Distinct().ToList();
        var seriesIds = context.Issues.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => i.SeriesId).Distinct().ToList();
        var issues = context.Issues.AsNoTracking().Include(i => i.Series).Include(i => i.MetadataProposals)
            .Where(i => seriesIds.Contains(i.SeriesId)).ToList();
        var credits = context.CreatorCredits.AsNoTracking().Include(c => c.Creator)
            .Where(c => c.CreatorId == creatorId && seriesIds.Contains(c.Issue!.SeriesId)).ToList();
        var runs = new List<CareerRun>();
        foreach (var group in issues.GroupBy(i => i.SeriesId))
        {
            foreach (var run in CreatorRunDetector.FindAll(group, credits))
            {
                var years = run.Issues.Select(i => i.EffectiveYear()).Where(y => y is not null).Select(y => y!.Value).ToList();
                runs.Add(new CareerRun(group.Key, group.First().Series!.Name, run.Role, run.Range,
                    years.Count > 0 ? years.Min() : null, years.Count > 0 ? years.Max() : null, run.Issues.Select(i => i.Id).ToList(), run.ReadCount));
            }
        }
        return runs.OrderBy(r => r.FirstYear ?? int.MaxValue).ThenBy(r => r.Series, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Range).ToList();
    }

    /// <summary>Imprint per series (blank imprints are absent), for a publisher's imprint chips.</summary>
    public static IReadOnlyDictionary<int, string> SeriesImprints(PaperbunkrDbContext context, IReadOnlyCollection<int> seriesIds)
    {
        var ids = seriesIds.ToList();
        return context.Series.AsNoTracking().Where(s => ids.Contains(s.Id) && s.Imprint != null && s.Imprint != "")
            .Select(s => new { s.Id, s.Imprint }).ToList()
            .Where(s => !string.IsNullOrWhiteSpace(s.Imprint)).ToDictionary(s => s.Id, s => s.Imprint!.Trim());
    }

    private static Dictionary<int, int> Order(IReadOnlyList<int> readingOrder)
    {
        var order = new Dictionary<int, int>(readingOrder.Count);
        for (int index = 0; index < readingOrder.Count; index++) order.TryAdd(readingOrder[index], index);
        return order;
    }
}
