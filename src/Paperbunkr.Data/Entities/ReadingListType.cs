namespace Paperbunkr.Data.Entities;

/// <summary>Classification of a <see cref="ReadingList"/> (docs/superpowers/specs/2026-08-17-metadata-model-phase4c-reading-list-overhaul-design.md), straight from the source doc's §24.</summary>
public enum ReadingListType
{
    Official,
    Community,
    User,
    Event,
    Chronological,
    PublicationOrder,
    Recommended,
    Custom,
}
