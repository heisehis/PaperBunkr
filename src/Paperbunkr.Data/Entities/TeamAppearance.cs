namespace Paperbunkr.Data.Entities;

/// <summary>
/// One <see cref="Team"/> appearing in one <see cref="Issue"/> - mirrors <see cref="CharacterAppearance"/>
/// exactly. Both FKs cascade - these rows are a rebuildable index, not content.
/// </summary>
public class TeamAppearance
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public Team? Team { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }
}
