namespace Paperbunkr.Data.Entities;

/// <summary>
/// A persisted "don't suggest this Wikidata universe for this series again" marker for
/// <see cref="Metadata.ContinuityWikidataMatchResolver"/> suggestions (docs/superpowers/specs/
/// 2026-09-17-storyevent-continuity-autopopulate-design.md). Both endpoints of the key are needed:
/// a series could plausibly match more than one candidate universe across separate scans, so the
/// dismissal must be scoped to the specific QID, not the whole series.
/// </summary>
public class ContinuitySuggestionDismissal
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public string WikidataQid { get; set; } = string.Empty;

    public DateTime DismissedAt { get; set; } = DateTime.UtcNow;
}
