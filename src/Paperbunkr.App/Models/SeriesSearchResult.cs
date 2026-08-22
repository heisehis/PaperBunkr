namespace Paperbunkr.App.Models;

/// <summary>One candidate series shown while searching to create a <c>MediaRelation</c> (docs/superpowers/specs/2026-08-17-metadata-model-phase3-media-relations-design.md).</summary>
public sealed class SeriesSearchResult
{
    public required int SeriesId { get; init; }
    public required string Name { get; init; }
}
