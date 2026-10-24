namespace Paperbunkr.Data.Entities;

/// <summary>
/// Marks one inheritable field of one chapter as a deliberate exception to its manga series' value (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §5).
/// While a row exists, fan-out from Edit Series skips that field on that chapter. Removing the row ("Reset to series") re-applies the series value.
/// </summary>
public class IssueFieldOverride
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    /// <summary>A <c>MangaInheritedFields</c> key, e.g. "Publisher", "Writer", "Genre".</summary>
    public string FieldKey { get; set; } = string.Empty;
}
