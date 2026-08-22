using Avalonia.Media;

namespace Paperbunkr.App.Models;

/// <summary>
/// One card in a derived Related-tab grouping section - "Same Continuity" (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase4a-continuity-design.md) and "Same Event" (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase4b-story-events-design.md). Unlike <see cref="RelatedSeriesSample"/>,
/// there's no removable underlying row here - membership is a read-only derived view (shared
/// continuity/event), not a stored per-pair relation, so there's no id to remove by.
/// </summary>
public sealed class RelatedGroupSeriesSample
{
    public required string Title { get; init; }
    public required string Name { get; init; }
    public required string Note { get; init; }
    public required IBrush CoverBrush { get; init; }
    public required int SeriesId { get; init; }
}
