namespace Paperbunkr.Data.Entities;

/// <summary>
/// One <see cref="Location"/> appearing in one <see cref="Issue"/> - mirrors <see cref="CharacterAppearance"/>
/// exactly. Both FKs cascade - these rows are a rebuildable index, not content.
/// </summary>
public class LocationAppearance
{
    public int Id { get; set; }

    public int LocationId { get; set; }

    public Location? Location { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }
}
