using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>
/// The metadata providers the app can actually fetch from, by <see cref="ExternalMetadataProvider"/> (the same three the Details tab can link). A provider with no fetcher
/// returns null and the caller says so, rather than inventing one (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §7.1).
/// </summary>
public static class MetadataProviderCatalog
{
    public static IMetadataProvider? For(ExternalMetadataProvider provider) => provider switch
    {
        ExternalMetadataProvider.MangaBaka => MangaBakaMetadataProvider.Shared,
        ExternalMetadataProvider.MangaDex => MangaDexMetadataProvider.Shared,
        ExternalMetadataProvider.AniList => new AniListMetadataProvider(AniListHttpClient.Shared),
        _ => null,
    };
}
