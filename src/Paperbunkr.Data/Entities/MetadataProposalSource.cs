namespace Paperbunkr.Data.Entities;

/// <summary>
/// Where a <see cref="MetadataProposal"/>'s value came from (docs/superpowers/specs/2026-08-17-
/// metadata-model-phase2a-metadata-proposals-design.md). Only <see cref="FilenameParser"/> is
/// actually produced this phase (by <c>LibraryFolderScanner</c>'s filename-fallback path) - the
/// rest exist now so later phases (embedded-XML-vs-filename conflicts, external providers) don't
/// need another migration just to add enum members additive to an existing column.
/// </summary>
public enum MetadataProposalSource
{
    FilenameParser,
    ComicInfoXml,
    MetadataProvider,
    Manual,
    Import,
    AI,
    Other
}
