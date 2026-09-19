using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The adapter contract every future external metadata provider integration implements
/// (docs/superpowers/specs/2026-08-17-metadata-model-phase5a-external-metadata-schema-design.md
/// §57) - defined now with zero implementations so the shape is settled before the first real
/// adapter (a separate future phase) needs to satisfy it. Deliberately excludes a
/// <c>GetRelationsAsync</c> method (present in the source doc's own sketch) - nothing in this
/// codebase can consume provider-driven <see cref="MediaRelation"/> data yet either, so adding a
/// method nothing can call would be speculative.
/// </summary>
public interface IMetadataProvider
{
    ExternalMetadataProvider ProviderKey { get; }

    Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken);

    Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken);
}

/// <summary>One candidate match from <see cref="IMetadataProvider.SearchAsync"/>.</summary>
public sealed record MetadataSearchResult(string ExternalId, string Title, string? Url);

/// <summary>
/// Optional capability for a provider with a browsable multi-cover archive (MangaBaka's, per
/// docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-design.md §2) rather than
/// a single canonical cover - AniList/MangaDex don't implement this, they use
/// <see cref="ExternalMediaMetadata.CoverImageUrl"/> directly. Same additive-interface pattern as
/// <c>ITrackerSearchProvider</c>.
/// </summary>
public interface IMultiCoverProvider
{
    Task<IReadOnlyList<CoverCandidate>> GetCoverCandidatesAsync(string externalId, CancellationToken cancellationToken);
}

/// <summary>One browsable cover option. <see cref="ThumbnailUrl"/> is a small variant for the
/// picker grid; <see cref="FullUrl"/> is the full-resolution image actually applied on selection.</summary>
public sealed record CoverCandidate(string ThumbnailUrl, string FullUrl, string Type, string? Source);

/// <summary>
/// Optional capability for a provider exposing typed relations to other media (AniList/MangaBaka,
/// docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-design.md §5) - MangaDex has
/// no confirmed relations endpoint, so it doesn't implement this. Fetched lazily, only when a
/// series' Related tab is opened, not as part of the default single-request Apply flow.
/// </summary>
public interface IRelationsProvider
{
    Task<IReadOnlyList<ProviderRelation>> GetRelationsAsync(string externalId, CancellationToken cancellationToken);
}

/// <summary>One typed relation to another series, as reported by an external provider - the target
/// may or may not exist locally yet (see <c>ExternalMediaRelation</c>).</summary>
public sealed record ProviderRelation(string TargetExternalId, string TargetTitle, string? TargetUrl, RelationType Type);

/// <summary>
/// Thrown by <see cref="IMetadataProvider.SearchAsync"/> when the underlying call itself failed
/// (network error, rate limit, service outage, malformed response) - distinct from a genuinely
/// empty result list, which means the call succeeded and found nothing. Collapsing both into "return
/// empty" (the original AniList adapter's behavior) made a real outage indistinguishable from "no
/// such series" in the UI - callers should catch this and show a distinct "provider unavailable"
/// message rather than "no matches found."
/// </summary>
public sealed class MetadataProviderUnavailableException : Exception
{
}

/// <summary>
/// Normalized provider metadata for one external entry (§56's "provider DTO -&gt; normalizer -&gt;
/// canonical model" pipeline) - enough fields to eventually feed a <see cref="MetadataProposal"/>,
/// not a passthrough of the provider's own raw schema.
/// </summary>
/// <param name="Title">The provider's single preferred display title (English when available,
/// romanized otherwise) - unchanged meaning from before <see cref="TitleEnglish"/>/
/// <see cref="TitleRomaji"/>/<see cref="TitleNative"/> existed.</param>
/// <param name="TitleEnglish">The provider's official localized/English title, when it has one
/// distinct from <paramref name="Title"/> - feeds <see cref="SeriesTitleType.Localized"/>
/// (docs/superpowers/specs/2026-08-19-metadata-model-anilist-search-and-link-design.md).</param>
/// <param name="TitleRomaji">Latin-alphabet transliteration - feeds <see cref="SeriesTitleType.Romanized"/>.</param>
/// <param name="TitleNative">Original-script title - feeds <see cref="SeriesTitleType.Native"/>.</param>
/// <param name="Genre">CSV-joined genre list (matching this codebase's established Genre/Teams/
/// Locations CSV convention - see <c>DetailPillsViewModel</c>'s own doc comment), when the provider
/// returns one. Null for a provider that doesn't expose genres on its get-by-id response
/// (MangaBaka's `v2` API, confirmed live - genres/tags are `v1`-only, see
/// docs/mangabaka-metadata-ui-research.md finding 14).</param>
public sealed record ExternalMediaMetadata(
    string ExternalId,
    string Title,
    string? Url,
    string? Description,
    string? Status,
    int? ChapterCount,
    int? VolumeCount,
    string? TitleEnglish = null,
    string? TitleRomaji = null,
    string? TitleNative = null,
    string? Genre = null,
    string? CoverImageUrl = null,
    string? Creator = null,
    int? PublicationYear = null,
    string? PublicationFormat = null,
    string? Demographic = null,
    IReadOnlyList<(ExternalMetadataProvider Provider, string ExternalId)>? CrossReferences = null,
    IReadOnlyList<string>? GenreTags = null,
    IReadOnlyList<(string Value, string Category)>? OtherTags = null);
