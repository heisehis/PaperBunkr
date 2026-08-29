namespace Paperbunkr.Data.Entities;

/// <summary>
/// One member of a <see cref="Collection"/> (docs/superpowers/specs/2026-08-27-collections-design.md).
/// Polymorphic: <b>exactly one</b> of <see cref="SeriesId"/>, <see cref="IssueId"/>,
/// <see cref="BookId"/> is non-null — enforced by a DB <c>CHECK</c> constraint and guarded in
/// <c>CollectionService.AddItems</c>. <see cref="SortOrder"/> is the user-defined position within
/// the collection.
///
/// <see cref="BookId"/> is the first FK from the library-org layer into the <see cref="Book"/>
/// schema. The "no FK crossing between the two schemas" rule (2026-08-09-novels-epub-pdf-support-
/// design.md) was specifically about <see cref="Issue"/> ↔ <see cref="Book"/> so neither reading
/// path has to know about the other; <see cref="Collection"/> is an org-layer entity that
/// deliberately spans both, so this crossing is intentional.
/// </summary>
public class CollectionItem
{
    public int Id { get; set; }

    public int CollectionId { get; set; }

    public Collection? Collection { get; set; }

    public int SortOrder { get; set; }

    public int? SeriesId { get; set; }

    public Series? Series { get; set; }

    public int? IssueId { get; set; }

    public Issue? Issue { get; set; }

    public int? BookId { get; set; }

    public Book? Book { get; set; }
}
