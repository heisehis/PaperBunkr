namespace Paperbunkr.Data.Entities;

/// <summary>
/// A first-class named character (docs/superpowers/specs/2026-08-27-metadata-model-phase4g-age-
/// progression-design.md - the deferred "first-class Character entity" it flags as the gap in
/// graph-driven family scoping). Auto-materialized from the free-text <see cref="Issue.Characters"/>
/// ComicInfo field by <c>CharacterResolver</c>; that string field stays the editable source of
/// truth (this is a derived index over it), so there's no editor for <see cref="Character"/> itself.
/// </summary>
public class Character
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<CharacterAppearance> Appearances { get; set; } = new();
}
