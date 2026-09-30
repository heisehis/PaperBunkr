namespace Paperbunkr.App.Models;

/// <summary>
/// A provider-sourced relation whose target isn't in the library yet (docs/superpowers/specs/
/// 2026-09-18-external-metadata-full-extraction-design.md §5) - rendered dimmed/badged in the
/// Related tab, separate from the real <see cref="RelatedSeriesSample"/> rail since there's no
/// local cover/id to drive that control's click-through/remove affordances. Grand Comics Database
/// series bonds to series you don't have show here too (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4),
/// with <see cref="SourceLabel"/> "GCD" and a link to the series' page there.
/// </summary>
public sealed class ExternalRelationPlaceholderSample
{
    public required string TargetTitle { get; init; }
    public required string RelationTypeLabel { get; init; }
    public string? TargetUrl { get; init; }

    /// <summary>Where the relation came from, shown as a small mark ("GCD"); null for provider placeholders.</summary>
    public string? SourceLabel { get; init; }

    public bool HasUrl => !string.IsNullOrEmpty(TargetUrl);

    public bool HasSourceLabel => !string.IsNullOrEmpty(SourceLabel);

    public string LinkTip => SourceLabel is "GCD" ? $"View on the Grand Comics Database · {TargetUrl}" : TargetUrl ?? string.Empty;
}
