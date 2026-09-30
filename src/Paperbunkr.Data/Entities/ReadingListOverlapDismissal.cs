namespace Paperbunkr.Data.Entities;

/// <summary>
/// "Not a duplicate" for two overlapping reading lists (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §7).
/// Stored with the smaller list id in <see cref="ListAId"/>. Permanent for the pair; both foreign keys cascade, so it goes away with either list.
/// </summary>
public class ReadingListOverlapDismissal
{
    public int Id { get; set; }

    public int ListAId { get; set; }

    public int ListBId { get; set; }

    public DateTime CreatedAt { get; set; }
}
