namespace Paperbunkr.Data.Entities;

/// <summary>
/// A folder the user has registered for Novel (EPUB/PDF) library scanning — mirrors
/// <see cref="WatchedFolder"/> for the comic library (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §2/§3), kept as a separate table rather than
/// reusing <see cref="WatchedFolder"/> since a folder scanned for one library type isn't
/// necessarily meant to be scanned for the other.
/// </summary>
public class BookFolder
{
    public int Id { get; set; }

    public string Path { get; set; } = string.Empty;
}
