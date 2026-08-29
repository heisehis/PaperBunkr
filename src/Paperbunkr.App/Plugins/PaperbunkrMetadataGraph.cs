using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real adapter for <see cref="IMetadataGraph"/> (docs/superpowers/specs/2026-08-28-plugin-api-v3-
/// data-manager-design.md §2). A read facade: it opens its own short-lived
/// <see cref="PaperbunkrDb.CreateContext"/> per call (same per-call-context convention
/// <see cref="PaperbunkrApplication"/> uses) and delegates straight to the existing
/// <c>Paperbunkr.Data</c> resolver statics — reachable here because <c>Paperbunkr.Data</c> grants
/// <c>InternalsVisibleTo("Paperbunkr.App")</c> (§7). No new query logic beyond a couple of thin
/// row lookups the resolvers don't already expose in exactly this shape.
///
/// Every method trusts only the <c>Id</c> of the entity handed in.
/// </summary>
public sealed class PaperbunkrMetadataGraph : IMetadataGraph
{
    public IReadOnlyList<MediaRelation> GetRelations(Series series)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.MediaRelations
            .Include(m => m.SourceSeries)
            .Include(m => m.TargetSeries)
            .Where(m => m.SourceSeriesId == series.Id || m.TargetSeriesId == series.Id)
            .OrderBy(m => m.CreatedAt)
            .ToList();
    }

    public IReadOnlyList<Series> GetRelatedSeries(Series series)
    {
        using var context = PaperbunkrDb.CreateContext();
        return MediaRelationResolver.GetRelatedSeries(context, series.Id).Select(r => r.OtherSeries).ToList();
    }

    public IReadOnlyList<Continuity> GetContinuities(Series series)
    {
        using var context = PaperbunkrDb.CreateContext();
        return ContinuityResolver.GetContinuities(context, series.Id);
    }

    public IReadOnlyList<Series> GetOtherSeriesInContinuity(Continuity continuity)
    {
        using var context = PaperbunkrDb.CreateContext();
        return ContinuityResolver.GetSeriesInContinuity(context, continuity.Id);
    }

    public IReadOnlyList<StoryEvent> GetEvents(Issue issue)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.EventMemberships
            .Where(m => m.IssueId == issue.Id)
            .Select(m => m.StoryEvent!)
            .Distinct()
            .ToList();
    }

    public IReadOnlyList<EventMembership> GetMemberships(StoryEvent storyEvent)
    {
        using var context = PaperbunkrDb.CreateContext();
        return EventMembershipResolver.GetOrderedMembers(context, storyEvent.Id);
    }

    public IReadOnlyList<EventRelation> GetEventRelations(StoryEvent storyEvent)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.EventRelations
            .Include(r => r.SourceEvent)
            .Include(r => r.TargetEvent)
            .Where(r => r.SourceEventId == storyEvent.Id || r.TargetEventId == storyEvent.Id)
            .OrderBy(r => r.CreatedAt)
            .ToList();
    }

    public (ComicAge? Age, decimal Confidence, string? Reason) GetAge(Issue issue)
    {
        // BookAgeResolver.Resolve reads only scalar Issue columns (BookAge/Year) - no context needed.
        return BookAgeResolver.Resolve(issue);
    }

    public IReadOnlyList<Series> GetSeriesFamily(Series series)
    {
        using var context = PaperbunkrDb.CreateContext();
        return SeriesFamilyResolver.GetFamily(context, series.Id);
    }
}
