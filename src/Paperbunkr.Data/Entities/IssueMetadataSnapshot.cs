namespace Paperbunkr.Data.Entities;

/// <summary>
/// The chapter's inheritable field values as they were before the first series-level fan-out touched it (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md D8).
/// Written once per issue, never updated. Reclassifying Manga to Comic restores from it, so a deliberate per-issue history is never lost to inheritance.
/// </summary>
public class IssueMetadataSnapshot
{
    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    /// <summary>JSON object keyed by <c>MangaInheritedFields</c> key; tag fields are JSON arrays of [value, category, weight].</summary>
    public string FieldsJson { get; set; } = "{}";

    public DateTime TakenUtc { get; set; } = DateTime.UtcNow;
}
