namespace Paperbunkr.Data.Entities;

/// <summary>
/// A first-class named creator (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md).
/// Auto-materialized from the free-text per-role credit fields on <see cref="Issue"/>
/// (<see cref="Issue.Writer"/>, <see cref="Issue.Penciller"/>, etc.) by <c>CreatorResolver</c>; those
/// string fields stay the editable source of truth. Unlike <see cref="Character"/>/<see cref="Team"/>/
/// <see cref="Location"/>, one creator can hold multiple roles on the same issue, so the join
/// (<see cref="CreatorCredit"/>) carries a <c>Role</c> discriminator instead of being a plain M:M.
/// </summary>
public class Creator
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<CreatorCredit> Credits { get; set; } = new();
}
