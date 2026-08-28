namespace Paperbunkr.Data.Entities;

/// <summary>
/// One <see cref="Character"/> appearing in one <see cref="Issue"/> - the join backing
/// character-aware series-family scoping (docs/superpowers/specs/2026-08-27-metadata-model-phase4g-
/// age-progression-design.md). Explicit join entity (not an implicit M:M) so nothing needs to be
/// added to <see cref="Issue"/>, whose <see cref="Issue.Characters"/> string property name would
/// otherwise collide with the navigation. Both FKs cascade - these rows are a rebuildable index,
/// not content.
/// </summary>
public class CharacterAppearance
{
    public int Id { get; set; }

    public int CharacterId { get; set; }

    public Character? Character { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }
}
