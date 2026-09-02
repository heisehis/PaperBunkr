namespace Paperbunkr.Data.Entities;

/// <summary>
/// Reader font family choice (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md
/// §5). Lives in <c>Paperbunkr.Data.Entities</c> rather than <c>Paperbunkr.App.Models</c> (where it
/// originated) because <see cref="AppSettings"/>'s global-default columns
/// (docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-design.md) need to
/// reference it, and <c>Paperbunkr.Data</c> cannot depend on <c>Paperbunkr.App</c>.
/// </summary>
public enum BookFontFamilyOption
{
    Serif,
    Sans,
    Mono,
    OpenDyslexic,
}
