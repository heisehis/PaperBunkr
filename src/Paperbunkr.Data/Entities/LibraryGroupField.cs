namespace Paperbunkr.Data.Entities;

/// <summary>
/// Library grid's grouping dimensions (docs/superpowers/specs/2026-08-09-library-toolbar-design.md
/// Phase C). Deliberately excludes Category/Collection - many-to-many, a series in 2 categories
/// would need to appear in 2 groups, not solved here.
/// </summary>
public enum LibraryGroupField
{
    None,
    ContentType,
    Publisher,
    Alphabetical,
}
