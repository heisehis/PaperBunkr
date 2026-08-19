namespace Paperbunkr.Data.Entities;

/// <summary>
/// Why a series is recommended (docs/superpowers/specs/2026-08-18-metadata-model-phase6a-
/// recommendation-engine-design.md, source doc §46) - the subset that phase can actually produce
/// from shipped data. Dropped from the source doc's full list: <c>SameSeries</c> (doesn't fit this
/// project's Series-granularity for relation-style concepts), <c>ExternalRecommendation</c>/
/// <c>Trending</c>/<c>RecentlyAdded</c>/<c>ContinueReading</c>/<c>IncompleteReadingOrder</c> (no data
/// source or fundamentally different shape - see the spec's own scope note).
/// </summary>
public enum RecommendationReason
{
    Related,
    SameUniverse,
    SameContinuity,
    SameEvent,
    Crossover,
    Prequel,
    Sequel,
    SharedCharacters,
    SharedCreators,
    SharedGenre,
    SharedTags,
}
