namespace Paperbunkr.Data.Entities;

/// <summary>
/// Which flat CE field this tag originated from - preserves the Genre/Tags distinction as a
/// discriminator on one table rather than two physically separate tables (docs/superpowers/specs/
/// 2026-08-23-weighted-categorized-tags-design.md), so Smart Lists/search can query both
/// uniformly while the UI still renders them as two distinct chip rows.
/// </summary>
public enum IssueTagField
{
    Genre,
    Tags,
}

/// <summary>
/// How strongly this tag applies to the issue it's on - ascending significance. <see cref="Unset"/>
/// is the honest default for anything not yet rated by the user; never inferred on migration or
/// import (docs/superpowers/specs/2026-08-23-weighted-categorized-tags-design.md).
/// </summary>
public enum IssueTagWeight
{
    Unset,
    Incidental,
    Recurrent,
    Defining,
    Core,
}

/// <summary>
/// One weighted, categorized tag value on an <see cref="Issue"/> - replaces the old flat
/// <c>Issue.Genre</c>/<c>Issue.Tags</c> CSV strings (docs/superpowers/specs/2026-08-23-weighted-
/// categorized-tags-design.md). <see cref="Category"/> is free text, not a fixed enum - extensible
/// per-value rather than globally enforced, since this is a personal library manager, not a
/// crowd-sourced taxonomy.
/// </summary>
public class IssueTag
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public IssueTagField Field { get; set; }

    public string Value { get; set; } = string.Empty;

    /// <summary>Free text; null renders as "Uncategorized". Migration seeds "Genre" for <see cref="IssueTagField.Genre"/> rows, "Uncategorized" for <see cref="IssueTagField.Tags"/> rows.</summary>
    public string? Category { get; set; }

    public IssueTagWeight Weight { get; set; } = IssueTagWeight.Unset;
}
