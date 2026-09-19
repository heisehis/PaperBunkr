namespace Paperbunkr.Data.Entities;

/// <summary>
/// Records that a <see cref="Character"/> resolved to no <c>P31</c>-filtered Wikidata match, so
/// <see cref="Metadata.ContinuityWikidataMatchResolver"/> doesn't re-query it every scheduled run
/// (docs/superpowers/specs/2026-09-17-storyevent-continuity-autopopulate-design.md). Same 30-day
/// expiry posture as <see cref="StoryEventVerificationNegativeCache"/>.
/// </summary>
public class ContinuityCharacterLookupNegativeCache
{
    public int Id { get; set; }

    public int CharacterId { get; set; }

    public Character? Character { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
