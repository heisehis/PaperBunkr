namespace Paperbunkr.App.Models;

/// <summary>One entry in the arc-search source picker (docs/superpowers/specs/2026-08-22-cbl-manager-curated-browse-design.md).</summary>
public sealed record ArcSourceOption(string Key, string DisplayName, bool RequiresCredentials, bool HasBrowsableCatalog);
