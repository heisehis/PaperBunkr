namespace Paperbunkr.App.Models;

/// <summary>One already-linked external metadata provider shown on the Detail screen's Details tab (docs/superpowers/specs/2026-08-19-metadata-model-anilist-search-and-link-design.md).</summary>
public sealed class ExternalLinkSample
{
    public required string ProviderLabel { get; init; }
    public required string ExternalId { get; init; }
    public string? Url { get; init; }

    /// <summary>False for MangaBaka - its multi-cover archive is reached through the existing
    /// "Change Cover" hero action's own picker instead of a direct one-click apply here
    /// (docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-design.md §2).</summary>
    public bool CanApplyCoverDirectly => ProviderLabel != "MangaBaka";
}
