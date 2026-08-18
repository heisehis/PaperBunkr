namespace Paperbunkr.Data.Entities;

/// <summary>
/// Third-party metadata provider (docs/superpowers/specs/2026-08-17-metadata-model-phase5a-
/// external-metadata-schema-design.md), straight from the source doc's §29 list. Complete now even
/// though no adapter exists for any of these yet, so a future single-provider adapter doesn't force
/// a schema migration just to add sibling values later.
/// </summary>
public enum ExternalMetadataProvider
{
    AniList,
    MyAnimeList,
    MangaDex,
    MangaBaka,
    MangaUpdates,
    Kitsu,
    AnimePlanet,
    Shikimori,
    GrandComicsDatabase,
    LeagueOfComicGeeks,
}
