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
/// Normalized provider metadata for one external entry (§56's "provider DTO -&gt; normalizer -&gt;
/// canonical model" pipeline) - enough fields to eventually feed a <see cref="MetadataProposal"/>,
/// not a passthrough of the provider's own raw schema.
/// </summary>
public sealed record ExternalMediaMetadata(
    string ExternalId,
    string Title,
    string? Url,
    string? Description,
    string? Status,
    int? ChapterCount,
    int? VolumeCount);
