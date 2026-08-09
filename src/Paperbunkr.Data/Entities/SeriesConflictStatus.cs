namespace Paperbunkr.Data.Entities;

/// <summary>
/// Resolution state of a <see cref="SeriesConflict"/>. Stored as its string name (see
/// PaperbunkrDbContext.OnModelCreating), matching every other enum in this schema.
/// </summary>
public enum SeriesConflictStatus
{
    Pending,
    Merged,
    KeptSeparate
}
