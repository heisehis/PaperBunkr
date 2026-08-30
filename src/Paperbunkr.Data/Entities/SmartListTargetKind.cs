namespace Paperbunkr.Data.Entities;

/// <summary>
/// Which entity a <see cref="SmartList"/>'s condition tree evaluates against (docs/superpowers/
/// specs/2026-08-30-smart-collections-design.md). Set at creation and immutable afterward -
/// switching it on an existing list would invalidate every condition referencing a field the new
/// kind doesn't have. Every list created before this existed is <see cref="Issue"/> (the default),
/// which is exactly its only behavior until now.
/// </summary>
public enum SmartListTargetKind
{
    Issue,
    Series,
    Novel,
}
