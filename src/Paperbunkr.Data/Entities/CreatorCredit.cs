namespace Paperbunkr.Data.Entities;

/// <summary>
/// One <see cref="Creator"/> holding one role on one <see cref="Issue"/>. Unlike
/// <see cref="CharacterAppearance"/>, this is not a plain M:M join - <see cref="Role"/> exists
/// because one creator can hold multiple roles on one issue (writer AND artist), each a separate
/// row. Both FKs cascade - these rows are a rebuildable index, not content.
/// </summary>
public class CreatorCredit
{
    public int Id { get; set; }

    public int CreatorId { get; set; }

    public Creator? Creator { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    /// <summary>Which per-issue credit field this row was derived from ("Writer", "Penciller", etc.) - see <see cref="Issue.Writer"/> and its siblings.</summary>
    public string? Role { get; set; }
}
