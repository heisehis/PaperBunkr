using Avalonia.Media;

namespace Paperbunkr.App.Models;

/// <summary>
/// One card in the Detail screen's Related tab carousel - real data as of docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase3-media-relations-design.md (previously always-empty sample
/// data). <see cref="RelatedSeriesId"/>/<see cref="MediaRelationId"/> back the tab's
/// navigate/remove actions.
/// </summary>
public sealed class RelatedSeriesSample
{
    public required string Title { get; init; }
    public required string Name { get; init; }
    public required string Note { get; init; }
    public required IBrush CoverBrush { get; init; }
    public required int RelatedSeriesId { get; init; }
    public required int MediaRelationId { get; init; }
}
