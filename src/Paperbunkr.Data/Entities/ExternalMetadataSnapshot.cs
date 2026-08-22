namespace Paperbunkr.Data.Entities;

/// <summary>
/// A raw provider response captured for a <see cref="Series"/> at fetch time (docs/superpowers/
/// specs/2026-08-17-metadata-model-phase5a-external-metadata-schema-design.md §31) - for provider
/// debugging/reprocessing/audit only. <see cref="Payload"/> is opaque to the core domain; nothing
/// in the app reads it directly as the normal metadata model (§31: "Do not expose raw provider
/// payloads as the normal application metadata model").
/// </summary>
public class ExternalMetadataSnapshot
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public ExternalMetadataProvider Provider { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public DateTime RetrievedAt { get; set; }

    public string Payload { get; set; } = string.Empty;

    public string SchemaVersion { get; set; } = string.Empty;
}
