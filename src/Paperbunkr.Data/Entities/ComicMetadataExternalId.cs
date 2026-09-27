namespace Paperbunkr.Data.Entities;

/// <summary>
/// Cross-references one <see cref="Character"/>/<see cref="Team"/>/<see cref="Location"/>/
/// <see cref="Creator"/>/<see cref="Publisher"/> row to the id a comic provider (ComicVine or Metron)
/// uses for the same real-world thing (docs/superpowers/specs/2026-09-23-metron-api-utilization-
/// design.md). One generic table for all five kinds rather than five near-identical twin-nullable-
/// column pairs (the shape <see cref="StoryEvent"/> uses for its own two arc-id columns). Deliberately
/// typed <see cref="ComicProvider"/> here, not <see cref="ExternalMetadataProvider"/> - that's the
/// unrelated, larger manga-tracker enum <see cref="ExternalMediaId"/> uses; conflating the two would
/// be wrong, not just inconsistent.
/// </summary>
public class ComicMetadataExternalId
{
    public int Id { get; set; }

    public ComicMetadataEntityKind EntityKind { get; set; }

    public int EntityId { get; set; }

    public ComicProvider Provider { get; set; }

    public string ExternalId { get; set; } = string.Empty;
}
