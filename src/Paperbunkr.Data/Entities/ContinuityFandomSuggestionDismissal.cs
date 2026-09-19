namespace Paperbunkr.Data.Entities;

/// <summary>
/// A persisted "don't suggest this universe for this series again" marker for
/// <see cref="Metadata.ContinuityWikidataMatchResolver"/> suggestions sourced from
/// <see cref="Continuity.FandomKey"/> rather than a real Wikidata QID - genuinely separate table
/// from <see cref="ContinuitySuggestionDismissal"/> rather than overloading its
/// <c>WikidataQid</c> column with a non-Wikidata value.
/// </summary>
public class ContinuityFandomSuggestionDismissal
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public string FandomKey { get; set; } = string.Empty;

    public DateTime DismissedAt { get; set; } = DateTime.UtcNow;
}
