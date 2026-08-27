namespace Paperbunkr.Data.Entities;

/// <summary>
/// One weighted, categorized tag on a <see cref="ReadingList"/> itself - a descriptor of the list
/// (e.g. "Dark", "Recommended Order"), distinct from what its member issues are tagged with via
/// <see cref="IssueTag"/> (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md). No
/// <c>Field</c> discriminator like <see cref="IssueTag"/> has - a Reading List has one tag concept,
/// not a Genre-vs-Tags split. Reuses <see cref="IssueTagWeight"/> directly rather than duplicating
/// an identical 5-tier enum.
/// </summary>
public class ReadingListTag
{
    public int Id { get; set; }

    public int ReadingListId { get; set; }

    public ReadingList? ReadingList { get; set; }

    public string Value { get; set; } = string.Empty;

    /// <summary>Free text; null renders as "Uncategorized" - same rule as <see cref="IssueTag.Category"/>.</summary>
    public string? Category { get; set; }

    public IssueTagWeight Weight { get; set; } = IssueTagWeight.Unset;
}
