namespace Paperbunkr.App.Models;

/// <summary>
/// A provider-sourced relation whose target isn't in the library yet (docs/superpowers/specs/
/// 2026-09-18-external-metadata-full-extraction-design.md §5) - rendered dimmed/badged in the
/// Related tab, separate from the real <see cref="RelatedSeriesSample"/> rail since there's no
/// local cover/id to drive that control's click-through/remove affordances.
/// </summary>
public sealed class ExternalRelationPlaceholderSample
{
    public required string TargetTitle { get; init; }
    public required string RelationTypeLabel { get; init; }
    public string? TargetUrl { get; init; }
}
