using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Plugins.Automation;

/// <summary>
/// Read access to the relationship / event / continuity / age graph shipped in Phase 3-4g
/// (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md §2). The sixth
/// <see cref="IPluginEnvironment"/> sub-interface — a thin curated read facade over the existing
/// <c>Paperbunkr.Data</c> resolvers, same "adapter wraps an existing service" pattern
/// <see cref="IApplication"/> already uses. No new query logic lives here; a plugin gets exactly
/// what the app's own Detail / Story Events screens compute for the same data.
///
/// Every method trusts only the <c>Id</c> of the entity passed in (matching
/// <c>PaperbunkrApplication.RemoveBook</c>), so a detached / stale instance from an earlier call
/// is fine as an argument.
/// </summary>
public interface IMetadataGraph
{
    /// <summary>Every <see cref="MediaRelation"/> row touching <paramref name="series"/> (as source or target).</summary>
    IReadOnlyList<MediaRelation> GetRelations(Series series);

    /// <summary>The related series for <paramref name="series"/>, each already resolved to the role it plays relative to <paramref name="series"/> (via <c>MediaRelationResolver</c>).</summary>
    IReadOnlyList<Series> GetRelatedSeries(Series series);

    /// <summary>The related collections for <paramref name="series"/> (docs/superpowers/specs/2026-08-30-media-relation-collection-nodes-design.md) - a Collection can now sit on either side of a <see cref="MediaRelation"/>.</summary>
    IReadOnlyList<Collection> GetRelatedCollections(Series series);

    /// <summary>Every <see cref="MediaRelation"/> row touching <paramref name="collection"/> (as source or target).</summary>
    IReadOnlyList<MediaRelation> GetRelations(Collection collection);

    /// <summary>The related series for <paramref name="collection"/>, each already resolved to the role it plays relative to <paramref name="collection"/>. A collection's own relations can only have a Series on the other side (Collection↔Collection is <c>CollectionRelation</c>'s job, not this graph's).</summary>
    IReadOnlyList<Series> GetRelatedSeries(Collection collection);

    /// <summary>The <see cref="Continuity"/> rows <paramref name="series"/> belongs to.</summary>
    IReadOnlyList<Continuity> GetContinuities(Series series);

    /// <summary>Every series in <paramref name="continuity"/> (via <c>ContinuityResolver</c>).</summary>
    IReadOnlyList<Series> GetOtherSeriesInContinuity(Continuity continuity);

    /// <summary>The <see cref="StoryEvent"/>s <paramref name="issue"/> is a member of.</summary>
    IReadOnlyList<StoryEvent> GetEvents(Issue issue);

    /// <summary>The ordered <see cref="EventMembership"/> rows of <paramref name="storyEvent"/> (via <c>EventMembershipResolver</c>).</summary>
    IReadOnlyList<EventMembership> GetMemberships(StoryEvent storyEvent);

    /// <summary>Every <see cref="EventRelation"/> row touching <paramref name="storyEvent"/> (as source or target).</summary>
    IReadOnlyList<EventRelation> GetEventRelations(StoryEvent storyEvent);

    /// <summary>The on-demand comic-age classification for <paramref name="issue"/> (via <c>BookAgeResolver</c>).</summary>
    (ComicAge? Age, decimal Confidence, string? Reason) GetAge(Issue issue);

    /// <summary>The connected "series family" of <paramref name="series"/> — relations + shared continuities, unioned (via <c>SeriesFamilyResolver</c>).</summary>
    IReadOnlyList<Series> GetSeriesFamily(Series series);
}
