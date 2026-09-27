namespace Paperbunkr.Data.Entities;

/// <summary>
/// One alternate cover Metron lists for an <see cref="Issue"/> (docs/superpowers/specs/2026-09-23-
/// metron-api-utilization-design.md). No external id exists for a variant at all (confirmed against
/// Metron's own schema - only a name and image URL), so <see cref="Name"/>+<see cref="ImageUrl"/> is
/// the identity, not a <see cref="ComicMetadataExternalId"/> row like the other synced entities.
/// </summary>
public class IssueVariantCover
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public string? Name { get; set; }

    public string ImageUrl { get; set; } = string.Empty;
}
