namespace Paperbunkr.Data.Entities;

/// <summary>
/// A genre or tag on a manga-family <see cref="Series"/> itself (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §4). Mirrors
/// <see cref="IssueTag"/>; saving Edit Series copies these into every chapter that has no override for that field.
/// </summary>
public class SeriesTag
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public IssueTagField Field { get; set; }

    public string Value { get; set; } = string.Empty;

    public string? Category { get; set; }

    public IssueTagWeight Weight { get; set; } = IssueTagWeight.Unset;
}
