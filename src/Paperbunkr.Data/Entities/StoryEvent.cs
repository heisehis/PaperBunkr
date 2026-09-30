namespace Paperbunkr.Data.Entities;

/// <summary>
/// A named cross-series publishing/story event ("Rise of the Third Army", "Secret Wars") -
/// greenfield entity, no CE precedent (docs/superpowers/specs/2026-08-17-metadata-model-phase4b-
/// story-events-design.md). Distinct from <see cref="ReadingList"/>: order is useful but optional
/// here, and <see cref="EventMembershipRole"/> is the point of the feature, not an afterthought
/// (source doc §51).
/// </summary>
public class StoryEvent
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// ComicVine's story-arc resource id, set when a <see cref="Metadata.StoryArcGroupingResolver"/>
    /// candidate was verified against ComicVine (docs/superpowers/specs/2026-09-17-storyevent-
    /// continuity-autopopulate-design.md). Null when never verified or verified only against Metron.
    /// </summary>
    public string? ComicVineArcId { get; set; }

    /// <summary>Metron's arc resource id, same posture as <see cref="ComicVineArcId"/>.</summary>
    public string? MetronArcId { get; set; }

    /// <summary>
    /// The series the Event Map draws as its trunk lane (docs/superpowers/specs/2026-09-25-event-map-design.md §1).
    /// Null = pick automatically (a member series whose name matches the event's); 0 = never use a spine (relay
    /// layout); any other value = the user's choice. No FK: a stale id is simply ignored by the map.
    /// </summary>
    public int? SpineSeriesId { get; set; }

    /// <summary>
    /// Who made this event (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §1): accepted/looked up from provider
    /// data, or made or renamed by the user. Only two <see cref="StoryEventOrigin.Provider"/> events are ever merged silently.
    /// </summary>
    public StoryEventOrigin Origin { get; set; }

    /// <summary>When id completion last ran for this event; null = never (or the last run failed and should be retried).</summary>
    public DateTime? IdentityCheckedAt { get; set; }

    /// <summary>Fingerprint of the member issue ids when id completion last ran; a different fingerprint means "check again".</summary>
    public string? IdentityMemberKey { get; set; }

    /// <summary>Set when provider sources disagree about this event's arc ids ("ComicVine arc 4512 vs 6620"); blocks silent merges and puts it in review.</summary>
    public string? IdentityConflict { get; set; }

    /// <summary>The Wikidata item matched to this event by the smart connector (docs/superpowers/specs/2026-09-27-continuity-map-design.md §1); null = not looked up or nothing found.</summary>
    public string? WikidataQid { get; set; }

    /// <summary>When the smart connector last looked this event up on Wikidata; rechecked after 30 days.</summary>
    public DateTime? ChronologyCheckedAt { get; set; }

    /// <summary>Other names this event is known by: merged-away events' names and the other provider's spelling.</summary>
    public List<StoryEventAlias> Aliases { get; set; } = new();

    public List<EventMembership> Members { get; set; } = new();
}
