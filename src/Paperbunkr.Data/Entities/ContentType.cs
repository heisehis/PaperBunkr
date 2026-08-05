namespace Paperbunkr.Data.Entities;

/// <summary>
/// Origin/format classification for a <see cref="Series"/>. New in Paperbunkr — CE's flat
/// <c>Manga</c> (MangaYesNo) field conflates this with reading direction (see
/// docs/onboarding.md §6). Stored as its string name in the database (see
/// PaperbunkrDbContext.OnModelCreating) so migrations/manual queries stay human-readable.
/// </summary>
public enum ContentType
{
    Comic,
    Manga,
    Manhua,
    Manhwa,
    Unknown
}
