namespace Paperbunkr.Data.Entities;

/// <summary>
/// Where a character's, team's or creator's image came from (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.3).
/// <see cref="Custom"/> is the user's own choice and always wins over a provider image.
/// </summary>
public enum EntityImageSource
{
    None = 0,
    Metron = 1,
    ComicVine = 2,
    Custom = 3,
    AniList = 4,
    MangaDex = 5,
}
