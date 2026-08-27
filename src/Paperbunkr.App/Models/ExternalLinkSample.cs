namespace Paperbunkr.App.Models;

/// <summary>One already-linked external metadata provider shown on the Detail screen's Details tab (docs/superpowers/specs/2026-08-19-metadata-model-anilist-search-and-link-design.md).</summary>
public sealed class ExternalLinkSample
{
    public required string ProviderLabel { get; init; }
    public required string ExternalId { get; init; }
    public string? Url { get; init; }
}
