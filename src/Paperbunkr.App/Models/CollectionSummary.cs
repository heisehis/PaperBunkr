namespace Paperbunkr.App.Models;

/// <summary>
/// Sidebar row for one <c>Collection</c> (docs/superpowers/specs/2026-08-27-collections-design.md) -
/// name, total member count (series + issues + books), optional accent colour, and whether it's the
/// currently active Library filter.
/// </summary>
public class CollectionSummary
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public int Count { get; init; }

    public string? AccentColor { get; init; }

    public bool IsActive { get; init; }
}
