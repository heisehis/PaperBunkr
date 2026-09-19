namespace Paperbunkr.Data.Entities;

/// <summary>
/// A provider-sourced relation whose target series isn't in this library yet (docs/superpowers/
/// specs/2026-09-18-external-metadata-full-extraction-design.md §5) - <see cref="MediaRelation"/>
/// can only link two rows already present locally, so this is a separate placeholder table rather
/// than a nullable-target extension of that entity. Auto-upgrades into a real
/// <see cref="MediaRelation"/> (and is then deleted) once a local <see cref="Series"/> gets linked
/// to <see cref="Provider"/>/<see cref="TargetExternalId"/> via <see cref="ExternalMediaId"/> - see
/// <c>MetadataLinkResolver.LinkAsync</c>.
/// </summary>
public class ExternalMediaRelation
{
    public int Id { get; set; }

    public int SourceSeriesId { get; set; }

    public Series? SourceSeries { get; set; }

    public ExternalMetadataProvider Provider { get; set; }

    public string TargetExternalId { get; set; } = string.Empty;

    /// <summary>Cached display text - this row's whole point is showing something before the
    /// target series exists locally, so there's no local row to read a title from.</summary>
    public string TargetTitle { get; set; } = string.Empty;

    public string? TargetUrl { get; set; }

    public RelationType RelationType { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
