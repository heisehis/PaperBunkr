using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Sidebar row for one <see cref="ContentType"/> bucket (docs/superpowers/specs/
/// 2026-08-09-library-sidebar-categorization-design.md) — name, real series count, and whether
/// it's the currently active Library filter.
/// </summary>
public class ContentTypeSummary
{
    public required ContentType ContentType { get; init; }

    public required string Name { get; init; }

    public int Count { get; init; }

    public bool IsActive { get; init; }
}
