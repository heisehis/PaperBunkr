namespace Paperbunkr.App.Models;

/// <summary>
/// Sidebar row for one <c>Category</c> ("Collections", docs/superpowers/specs/
/// 2026-08-09-library-sidebar-categorization-design.md) — name, real member-series count, and
/// whether it's the currently active Library filter. Category creation/assignment UI stays
/// Beta-scoped; this only surfaces whatever rows already exist.
/// </summary>
public class CategorySummary
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public int Count { get; init; }

    public bool IsActive { get; init; }
}
