namespace Paperbunkr.App.Models;

/// <summary>
/// Which entity table a <see cref="MetadataEditHistoryEntry"/> restores against
/// (docs/superpowers/specs/2026-08-27-book-properties-editor-design.md). The comic issue editors
/// predate books entirely, so <see cref="Issue"/> is the default - every existing
/// <see cref="MetadataEditHistoryEntry"/> initializer omits <c>Target</c> and keeps its behaviour.
/// </summary>
public enum MetadataEditTarget
{
    Issue,
    Book,
}
