using Avalonia.Media;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Models;

/// <summary>
/// One card in the Detail screen's Related tab carousel - real data as of docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase3-media-relations-design.md (previously always-empty sample
/// data). <see cref="Kind"/> (docs/superpowers/specs/2026-08-30-media-relation-collection-nodes-
/// design.md) is <see langword="null"/> until Collection endpoints existed, so plain-Series
/// members stay first-class; <see cref="RelatedSeriesId"/>/<see cref="RelatedCollectionId"/> are
/// mutually exclusive per <see cref="Kind"/>. <see cref="MediaRelationId"/> backs the tab's
/// remove action regardless of which kind the other side is.
/// </summary>
public sealed class RelatedSeriesSample
{
    public required string Title { get; init; }
    public required string Name { get; init; }
    public required string Note { get; init; }
    public required IBrush CoverBrush { get; init; }
    public required MediaRelationEndpointKind Kind { get; init; }
    public int? RelatedSeriesId { get; init; }
    public int? RelatedCollectionId { get; init; }
    public required int MediaRelationId { get; init; }
}
