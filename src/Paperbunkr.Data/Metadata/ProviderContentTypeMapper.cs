using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// What a provider's own type fields say about a series (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md).
/// </summary>
/// <param name="Type">The mapped type, or null when the provider's value tells us nothing usable (unknown string, missing field).</param>
/// <param name="QueueOnly">The mapping is a weak guess (an OEL or English-language original): it may be shown in the queue but never applied on its own.</param>
/// <param name="Ignored">The entry is not a comic at all (a light novel); it counts as no match.</param>
/// <param name="Raw">The provider's own value, shown as the evidence chip's tooltip ("manhwa", "KR", "ko").</param>
public sealed record MappedProviderType(ContentType? Type, bool QueueOnly, bool Ignored, string? Raw)
{
    public static MappedProviderType Typed(ContentType type, string? raw) => new(type, false, false, raw);

    public static MappedProviderType Weak(ContentType type, string? raw) => new(type, true, false, raw);

    public static MappedProviderType Novel(string? raw) => new(null, false, true, raw);

    public static MappedProviderType Unmapped(string? raw) => new(null, false, false, raw);
}

/// <summary>
/// Pure mapping from a provider's normalized metadata to a <see cref="ContentType"/>. MangaBaka states the type outright;
/// AniList and MangaDex only state a country / language of origin, which is what separates manhwa and manhua from manga.
/// </summary>
public static class ProviderContentTypeMapper
{
    /// <summary>The reading mode that goes with a type - the same pairs <c>LanguageIsoClassifier</c> and <c>PublisherContentTypeClassifier</c> use.</summary>
    public static ReadingMode ReadingModeFor(ContentType type) => type switch
    {
        ContentType.Manga => ReadingMode.RightToLeft,
        ContentType.Manhwa => ReadingMode.Webtoon,
        ContentType.Manhua => ReadingMode.Webtoon,
        _ => ReadingMode.LeftToRight,
    };

    public static MappedProviderType Map(ExternalMetadataProvider provider, ExternalMediaMetadata metadata) => provider switch
    {
        ExternalMetadataProvider.MangaBaka => MapMangaBaka(metadata.PublicationFormat),
        ExternalMetadataProvider.AniList => MapAniList(metadata.PublicationFormat, metadata.CountryOfOrigin),
        ExternalMetadataProvider.MangaDex => MapMangaDex(metadata.OriginalLanguage),
        _ => MappedProviderType.Unmapped(metadata.PublicationFormat),
    };

    public static MappedProviderType MapMangaBaka(string? type)
    {
        string key = (type ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "manga" => MappedProviderType.Typed(ContentType.Manga, type),
            "manhwa" => MappedProviderType.Typed(ContentType.Manhwa, type),
            "manhua" => MappedProviderType.Typed(ContentType.Manhua, type),
            "novel" => MappedProviderType.Novel(type),
            "oel" => MappedProviderType.Weak(ContentType.Comic, type),
            _ => MappedProviderType.Unmapped(type),
        };
    }

    public static MappedProviderType MapAniList(string? format, string? countryOfOrigin)
    {
        string fmt = (format ?? string.Empty).Trim().ToUpperInvariant();
        string country = (countryOfOrigin ?? string.Empty).Trim().ToUpperInvariant();

        if (fmt == "NOVEL")
        {
            return MappedProviderType.Novel(format);
        }

        string? raw = string.IsNullOrEmpty(country) ? format : country;
        return country switch
        {
            "JP" => MappedProviderType.Typed(ContentType.Manga, raw),
            "KR" => MappedProviderType.Typed(ContentType.Manhwa, raw),
            "CN" or "TW" or "HK" => MappedProviderType.Typed(ContentType.Manhua, raw),
            // A one-shot with no country is, on AniList, overwhelmingly a Japanese one; a plain MANGA with no country says nothing.
            "" when fmt == "ONE_SHOT" => MappedProviderType.Typed(ContentType.Manga, raw),
            _ => MappedProviderType.Unmapped(raw),
        };
    }

    public static MappedProviderType MapMangaDex(string? originalLanguage)
    {
        string lang = (originalLanguage ?? string.Empty).Trim().ToLowerInvariant();
        return lang switch
        {
            "ja" => MappedProviderType.Typed(ContentType.Manga, originalLanguage),
            "ko" => MappedProviderType.Typed(ContentType.Manhwa, originalLanguage),
            "zh" or "zh-hk" => MappedProviderType.Typed(ContentType.Manhua, originalLanguage),
            "en" => MappedProviderType.Weak(ContentType.Comic, originalLanguage),
            _ => MappedProviderType.Unmapped(originalLanguage),
        };
    }
}
