using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

public sealed record EntityPageIssue(int Id, int SeriesId, string Label, int? Year, bool IsRead, string SeriesName = "");
public sealed record EntityPageSeries(int Id, string Name, int Count, int ReadCount);
public sealed record EntityPageContinuity(int Id, string Name, int Count);
public sealed record EntityPageEvent(int Id, string Name, string? CoverUrl, int? Year, string Continuity, int Count);
public sealed record EntityPageYear(int? Year, int Count);
public sealed record EntityPageRole(string? Role, int Count);
public sealed record EntityPage(
    string Name, string? Aliases, string? Description, string? CustomImagePath, string? ImageUrl,
    IReadOnlyList<EntityPageIssue> Appearances, IReadOnlyList<EntityPageContinuity> Continuities,
    IReadOnlyList<EntityPageEvent> Events, IReadOnlyList<EntityPageSeries> Series,
    IReadOnlyList<EntityPageYear> ByYear, IReadOnlyList<EntityPageRole> Roles)
{
    public int AppearanceCount => Appearances.Count;
    public int SeriesCount => Series.Count;
    public int ReadCount => Appearances.Count(i => i.IsRead);
    public EntityPageIssue? FirstAppearance => Appearances.FirstOrDefault();
    public IReadOnlyList<EntityPageIssue> Unread => Appearances.Where(i => !i.IsRead).ToList();
    public IReadOnlyList<int> ReadingOrder => Appearances.Select(i => i.Id).ToList();
}

/// <summary>Read-only projection for the entity page. D1 is a deliberate extension beyond CE's flat metadata browser.</summary>
public static class EntityPageResolver
{
    /// <param name="role">Creator pages only: narrows every card to the issues credited in this role (the role chips keep their full counts).</param>
    public static EntityPage Build(PaperbunkrDbContext context, ComicMetadataEntityKind kind, int id, int? continuityId = null, string? role = null)
    {
        string name = kind switch
        {
            ComicMetadataEntityKind.Character => context.Characters.Where(e => e.Id == id).Select(e => e.Name).FirstOrDefault(),
            ComicMetadataEntityKind.Team => context.Teams.Where(e => e.Id == id).Select(e => e.Name).FirstOrDefault(),
            ComicMetadataEntityKind.Location => context.Locations.Where(e => e.Id == id).Select(e => e.Name).FirstOrDefault(),
            ComicMetadataEntityKind.Creator => context.Creators.Where(e => e.Id == id).Select(e => e.Name).FirstOrDefault(),
            ComicMetadataEntityKind.Publisher => context.Publishers.Where(e => e.Id == id).Select(e => e.Name).FirstOrDefault(),
            _ => null,
        } ?? string.Empty;
        IHasEntityProfile? profile = kind switch
        {
            ComicMetadataEntityKind.Character => context.Characters.AsNoTracking().FirstOrDefault(e => e.Id == id),
            ComicMetadataEntityKind.Team => context.Teams.AsNoTracking().FirstOrDefault(e => e.Id == id),
            ComicMetadataEntityKind.Creator => context.Creators.AsNoTracking().FirstOrDefault(e => e.Id == id),
            ComicMetadataEntityKind.Location => context.Locations.AsNoTracking().FirstOrDefault(e => e.Id == id),
            _ => null,
        };
        IQueryable<int> ids = kind switch
        {
            ComicMetadataEntityKind.Character => context.CharacterAppearances.Where(a => a.CharacterId == id).Select(a => a.IssueId),
            ComicMetadataEntityKind.Team => context.TeamAppearances.Where(a => a.TeamId == id).Select(a => a.IssueId),
            ComicMetadataEntityKind.Location => context.LocationAppearances.Where(a => a.LocationId == id).Select(a => a.IssueId),
            ComicMetadataEntityKind.Creator => context.CreatorCredits.Where(a => a.CreatorId == id).Select(a => a.IssueId),
            ComicMetadataEntityKind.Publisher => context.Issues.Where(i => i.Series!.PublisherEntityId == id).Select(i => i.Id),
            _ => context.Issues.Where(i => false).Select(i => i.Id),
        };
        var issues = context.Issues.AsNoTracking().Include(i => i.Series).Include(i => i.MetadataProposals)
            .Where(i => ids.Contains(i.Id) && !i.IsPlaceholder).ToList();
        var seriesIds = issues.Select(i => i.SeriesId).Distinct().ToList();
        var memberships = context.ContinuityMemberships.AsNoTracking().Include(m => m.Continuity)
            .Where(m => seriesIds.Contains(m.SeriesId)).ToList();
        // Keep the full switch available after narrowing, otherwise a selection could hide its siblings.
        var continuities = memberships.GroupBy(m => m.ContinuityId)
            .Select(g => new EntityPageContinuity(g.Key, g.First().Continuity.Name,
                issues.Count(i => g.Any(m => m.SeriesId == i.SeriesId))))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (continuityId is int selected)
        {
            var selectedSeries = memberships.Where(m => m.ContinuityId == selected).Select(m => m.SeriesId).ToHashSet();
            issues = issues.Where(i => selectedSeries.Contains(i.SeriesId)).ToList();
        }
        // Role chips count distinct issues (an issue pencilled twice by one creator is one issue), over the continuity-filtered set.
        var roles = new List<EntityPageRole>();
        if (kind == ComicMetadataEntityKind.Creator)
        {
            var filteredIds = issues.Select(i => i.Id).ToList();
            var credits = context.CreatorCredits.AsNoTracking().Where(c => c.CreatorId == id && filteredIds.Contains(c.IssueId))
                .Select(c => new { c.Role, c.IssueId }).ToList();
            roles = credits.GroupBy(c => c.Role).Select(g => new EntityPageRole(g.Key, g.Select(c => c.IssueId).Distinct().Count()))
                .OrderByDescending(r => r.Count).ThenBy(r => r.Role, StringComparer.OrdinalIgnoreCase).ToList();
            if (role is not null)
            {
                var credited = credits.Where(c => c.Role == role).Select(c => c.IssueId).ToHashSet();
                issues = issues.Where(i => credited.Contains(i.Id)).ToList();
            }
        }
        issues = issues.OrderBy(i => i.EffectiveYear() ?? int.MaxValue)
            .ThenBy(i => i.Month ?? 0).ThenBy(i => i.Day ?? 0)
            .ThenBy(i => i.Series!.SortName ?? i.Series.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.NumberSortKey() ?? float.MaxValue).ThenBy(i => i.Number, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id).ToList();
        var issueIds = issues.Select(i => i.Id).ToList();
        var appearances = issues.Select(i => new EntityPageIssue(i.Id, i.SeriesId,
            string.IsNullOrWhiteSpace(i.EffectiveNumber()) ? i.Series!.Name : $"{i.Series!.Name} #{i.EffectiveNumber()}",
            i.EffectiveYear(), i.HasBeenRead(), i.Series.Name)).ToList();
        var series = issues.GroupBy(i => i.SeriesId).Select(g => new EntityPageSeries(g.Key, g.First().Series!.Name,
            g.Count(), g.Count(i => i.HasBeenRead()))).OrderByDescending(s => s.Count).ThenBy(s => s.Name).ToList();
        if (kind == ComicMetadataEntityKind.Publisher)
        {
            var published = context.Series.AsNoTracking().Where(s => s.PublisherEntityId == id);
            if (continuityId is int selectedContinuity)
                published = published.Where(s => context.ContinuityMemberships.Any(m => m.SeriesId == s.Id && m.ContinuityId == selectedContinuity));
            foreach (var s in published.Where(s => !seriesIds.Contains(s.Id)).ToList())
                series.Add(new EntityPageSeries(s.Id, s.Name, 0, 0));
        }
        var eventMembers = context.EventMemberships.AsNoTracking().Include(m => m.StoryEvent)
            .Where(m => issueIds.Contains(m.IssueId)).ToList();
        var eventIds = eventMembers.Select(m => m.StoryEventId).Distinct().ToList();
        var spans = EventChronology.LoadSpans(context, eventIds);
        var orderedEvents = EventChronology.Order(spans.Values.ToList(), EventChronology.LoadRelations(context, eventIds));
        var events = orderedEvents.Select(eventId =>
        {
            var members = eventMembers.Where(m => m.StoryEventId == eventId).ToList();
            var e = members[0].StoryEvent!;
            var memberSeries = issues.Where(i => members.Any(m => m.IssueId == i.Id)).Select(i => i.SeriesId).ToHashSet();
            string continuity = string.Join(" · ", memberships.Where(m => memberSeries.Contains(m.SeriesId) &&
                (continuityId == null || m.ContinuityId == continuityId)).Select(m => m.Continuity.Name).Distinct().OrderBy(n => n));
            return new EntityPageEvent(e.Id, e.Name, e.CoverImageUrl, spans[eventId].Start / 100, continuity, members.Select(m => m.IssueId).Distinct().Count());
        }).ToList();
        return new EntityPage(name, profile?.Aliases, profile?.Description, profile?.CustomImagePath, profile?.ImageUrl,
            appearances, continuities, events, series,
            appearances.GroupBy(i => i.Year).OrderBy(g => g.Key ?? int.MaxValue).Select(g => new EntityPageYear(g.Key, g.Count())).ToList(), roles);
    }
}
