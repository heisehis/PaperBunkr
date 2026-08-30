using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Models;

/// <summary>
/// One candidate in the Detail screen's Related tab mixed add-flow (docs/superpowers/specs/
/// 2026-08-30-media-relation-collection-nodes-design.md) - a Series or a Collection, tagged by
/// <see cref="Kind"/> so the picker UI can show which. Deliberately separate from
/// <see cref="SeriesSearchResult"/>, which stays Series-only for its other consumers (Continuity's
/// "add series" flow, bulk selection).
/// </summary>
public sealed record RelationSearchResult(MediaRelationEndpointKind Kind, int? SeriesId, int? CollectionId, string Name);
