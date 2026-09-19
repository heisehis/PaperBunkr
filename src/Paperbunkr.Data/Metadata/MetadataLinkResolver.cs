using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One provider search result, scored against a series' known titles.</summary>
public sealed record ScoredMetadataMatch(MetadataSearchResult Result, double Confidence, MatchTier Tier);

/// <summary>
/// Search + link workflow for any <see cref="IMetadataProvider"/> (docs/superpowers/specs/2026-08-19-
/// metadata-model-anilist-search-and-link-design.md) - the search/match UI that Phase 5b's own spec
/// explicitly deferred ("nothing in Paperbunkr.App calls this yet"). Provider-agnostic (takes
/// <see cref="IMetadataProvider"/>, not <see cref="AniListMetadataProvider"/> directly) even though
/// AniList is the only real implementation today, matching this codebase's existing provider-
/// interface discipline (<c>IMetadataProvider</c>'s own doc comment on why nothing provider-specific
/// belongs above this seam).
/// </summary>
public static class MetadataLinkResolver
{
    /// <summary>Searches <paramref name="provider"/> and scores each result against <paramref name="seriesId"/>'s known titles (primary name + <see cref="SeriesTitle"/> alternates), best match first. Empty when the series doesn't exist or the query is blank.</summary>
    public static async Task<IReadOnlyList<ScoredMetadataMatch>> SearchAsync(
        IMetadataProvider provider, PaperbunkrDbContext context, int seriesId, string query, CancellationToken cancellationToken)
    {
        var series = context.Series.Include(s => s.Titles).FirstOrDefault(s => s.Id == seriesId);
        if (series is null || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ScoredMetadataMatch>();
        }

        var knownTitles = new[] { series.Name }.Concat(series.Titles.Select(t => t.Value)).ToList();
        var results = await provider.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        return results
            .Select(r =>
            {
                double score = TitleMatchScorer.BestScore(knownTitles, r.Title);
                return new ScoredMetadataMatch(r, score, TitleMatchScorer.Tier(score));
            })
            .OrderByDescending(m => m.Confidence)
            .ToList();
    }

    /// <summary>
    /// Fetches full metadata for <paramref name="externalId"/> and links it to <paramref name="seriesId"/>:
    /// upserts the <see cref="ExternalMediaId"/> (one per series+provider - re-linking replaces the
    /// prior external id rather than accumulating duplicates), always appends a fresh
    /// <see cref="ExternalMetadataSnapshot"/> (append-only audit log, per its own doc comment), and adds
    /// any <see cref="SeriesTitle"/> rows the provider returned that this series doesn't already have
    /// (case-insensitively, and never duplicating <see cref="Series.Name"/> itself). Returns false if
    /// the series doesn't exist or the provider fetch failed - callers should leave the series
    /// unmodified either way rather than partially linking.
    /// </summary>
    public static async Task<bool> LinkAsync(
        IMetadataProvider provider, PaperbunkrDbContext context, int seriesId, string externalId, CancellationToken cancellationToken)
    {
        var series = context.Series.Include(s => s.Titles).FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return false;
        }

        var metadata = await provider.GetAsync(externalId, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            return false;
        }

        var existingLink = context.ExternalMediaIds
            .FirstOrDefault(e => e.SeriesId == seriesId && e.Provider == provider.ProviderKey);
        if (existingLink is null)
        {
            context.ExternalMediaIds.Add(new ExternalMediaId
            {
                SeriesId = seriesId,
                Provider = provider.ProviderKey,
                ExternalId = metadata.ExternalId,
                Url = metadata.Url,
                LastFetchedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existingLink.ExternalId = metadata.ExternalId;
            existingLink.Url = metadata.Url;
            existingLink.LastFetchedAt = DateTime.UtcNow;
        }

        context.ExternalMetadataSnapshots.Add(new ExternalMetadataSnapshot
        {
            SeriesId = seriesId,
            Provider = provider.ProviderKey,
            ExternalId = metadata.ExternalId,
            RetrievedAt = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(metadata),
            SchemaVersion = "1",
        });

        UpgradeMatchingPlaceholderRelations(context, seriesId, provider.ProviderKey, metadata.ExternalId);
        UpsertCrossReferences(context, seriesId, metadata.CrossReferences);

        AddTitleIfNew(series, metadata.TitleNative, SeriesTitleType.Native);
        AddTitleIfNew(series, metadata.TitleRomaji, SeriesTitleType.Romanized);
        AddTitleIfNew(series, metadata.TitleEnglish, SeriesTitleType.Localized);

        ProposeAndApply(context, series, MetadataProposalField.Summary, series.Summary, metadata.Description, provider.ProviderKey);
        ProposeAndApply(context, series, MetadataProposalField.Status, series.Status.ToString(), NormalizedStatusOrNull(metadata.Status), provider.ProviderKey);
        ProposeAndApply(context, series, MetadataProposalField.Genre, series.Genre, metadata.Genre, provider.ProviderKey);
        ProposeAndApply(context, series, MetadataProposalField.Creator, series.Creator, metadata.Creator, provider.ProviderKey);
        ExternalTagImportResolver.ApplyToSeries(context, series, metadata);

        context.SaveChanges();
        return true;
    }

    /// <summary>
    /// Null when the provider didn't supply a status, or supplied one <see cref="SeriesStatusNormalizer"/>
    /// doesn't recognize - either way there's no real value to propose. Without this, an unrecognized
    /// raw status (a normalizer gap, not a genuine "unknown" from the provider) would overwrite an
    /// already-correct stored <see cref="SeriesStatus"/> with <see cref="SeriesStatus.Unknown"/>,
    /// which is worse than leaving the stored value alone.
    /// </summary>
    private static string? NormalizedStatusOrNull(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return null;
        }

        var normalized = SeriesStatusNormalizer.Normalize(rawStatus);
        return normalized == SeriesStatus.Unknown ? null : normalized.ToString();
    }

    /// <summary>
    /// Creates a Series-scoped <see cref="MetadataProposal"/> and immediately applies it
    /// (docs/superpowers/specs/2026-08-23-apply-from-provider-design.md's auto-accept-and-overwrite
    /// decision) - unlike Issue-scoped proposals, which stay as review-queue-only rows until a human
    /// accepts them, these write straight to <paramref name="series"/> since there's no filename-
    /// parser-vs-embedded-XML race to arbitrate at read time for a provider-sourced Series field.
    /// No-op when <paramref name="providedValue"/> is empty - a provider that doesn't return a field
    /// (e.g. MangaBaka's Genre) must never overwrite an existing value with nothing.
    /// </summary>
    private static void ProposeAndApply(
        PaperbunkrDbContext context, Series series, MetadataProposalField field, string? currentValue, string? providedValue, ExternalMetadataProvider providerKey)
    {
        if (string.IsNullOrWhiteSpace(providedValue))
        {
            return;
        }

        context.MetadataProposals.Add(new MetadataProposal
        {
            SeriesId = series.Id,
            Field = field,
            CurrentValue = currentValue,
            ProposedValue = providedValue,
            Source = MetadataProposalSource.MetadataProvider,
            ProviderKey = providerKey,
            Confidence = 1.0m,
            Status = MetadataProposalStatus.Accepted,
            ResolvedAt = DateTime.UtcNow,
        });

        switch (field)
        {
            case MetadataProposalField.Summary:
                series.Summary = providedValue;
                break;
            case MetadataProposalField.Genre:
                series.Genre = providedValue;
                break;
            case MetadataProposalField.Status:
                series.Status = Enum.Parse<SeriesStatus>(providedValue);
                break;
            case MetadataProposalField.Creator:
                series.Creator = providedValue;
                break;
        }
    }

    /// <summary>Adds to the already-tracked <c>series.Titles</c> collection navigation only - EF Core
    /// picks up the new item on <c>SaveChanges</c> via the loaded navigation, no separate
    /// <c>context.SeriesTitles.Add</c> needed (that would double-insert the same row).</summary>
    private static void AddTitleIfNew(Series series, string? value, SeriesTitleType type)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        bool alreadyKnown = string.Equals(series.Name, value, StringComparison.OrdinalIgnoreCase)
            || series.Titles.Any(t => string.Equals(t.Value, value, StringComparison.OrdinalIgnoreCase));
        if (alreadyKnown)
        {
            return;
        }

        series.Titles.Add(new SeriesTitle { SeriesId = series.Id, Value = value, Type = type });
    }

    /// <summary>
    /// Converts any <see cref="ExternalMediaRelation"/> placeholder that already points at
    /// <paramref name="justLinkedExternalId"/> into a real <see cref="MediaRelation"/>, now that a
    /// local <see cref="Series"/> exists for it (docs/superpowers/specs/2026-09-18-external-
    /// metadata-full-extraction-design.md §5) - runs on every link, not just ones that fetch
    /// relations themselves, since this only needs the just-established <see cref="ExternalMediaId"/>,
    /// not a fresh relations fetch.
    /// </summary>
    private static void UpgradeMatchingPlaceholderRelations(PaperbunkrDbContext context, int seriesId, ExternalMetadataProvider provider, string justLinkedExternalId)
    {
        var placeholders = context.ExternalMediaRelations
            .Where(r => r.Provider == provider && r.TargetExternalId == justLinkedExternalId)
            .ToList();

        foreach (var placeholder in placeholders)
        {
            context.MediaRelations.Add(new MediaRelation
            {
                SourceSeriesId = placeholder.SourceSeriesId,
                TargetSeriesId = seriesId,
                RelationType = placeholder.RelationType,
            });
            context.ExternalMediaRelations.Remove(placeholder);
        }
    }

    /// <summary>
    /// Upserts an <see cref="ExternalMediaId"/> for each provider-asserted cross-reference
    /// (MangaDex's <c>links</c>, MangaBaka's <c>source</c> - docs/superpowers/specs/2026-09-18-
    /// external-metadata-full-extraction-design.md §8) - only when no row exists yet for that
    /// provider. An existing row with a <i>different</i> id is a genuine identity conflict, left
    /// untouched rather than silently overwritten (surfaced through the existing Needs-Review
    /// surfaces, not a new one here).
    /// </summary>
    private static void UpsertCrossReferences(PaperbunkrDbContext context, int seriesId, IReadOnlyList<(ExternalMetadataProvider Provider, string ExternalId)>? crossReferences)
    {
        if (crossReferences is null)
        {
            return;
        }

        foreach (var (crossProvider, crossExternalId) in crossReferences)
        {
            bool alreadyLinked = context.ExternalMediaIds.Any(e => e.SeriesId == seriesId && e.Provider == crossProvider);
            if (alreadyLinked)
            {
                continue;
            }

            context.ExternalMediaIds.Add(new ExternalMediaId
            {
                SeriesId = seriesId,
                Provider = crossProvider,
                ExternalId = crossExternalId,
                Url = null,
                LastFetchedAt = null,
            });
        }
    }

    /// <summary>
    /// Fetches <paramref name="provider"/>'s relations for <paramref name="externalId"/> and
    /// persists each one not already known - as a real <see cref="MediaRelation"/> when the target
    /// is already linked locally (via <see cref="UpgradeMatchingPlaceholderRelations"/>'s same
    /// matching logic, run inline here since <paramref name="provider"/> already knows the target's
    /// external id up front), otherwise as an <see cref="ExternalMediaRelation"/> placeholder.
    /// Lazy - called only when a series' Related tab is opened, not part of <see cref="LinkAsync"/>
    /// itself (docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-design.md §7).
    /// No-op when <paramref name="provider"/> doesn't implement <see cref="IRelationsProvider"/>.
    /// </summary>
    public static async Task RefreshRelationsAsync(
        IMetadataProvider provider, PaperbunkrDbContext context, int seriesId, string externalId, CancellationToken cancellationToken)
    {
        if (provider is not IRelationsProvider relationsProvider)
        {
            return;
        }

        var relations = await relationsProvider.GetRelationsAsync(externalId, cancellationToken).ConfigureAwait(false);
        foreach (var relation in relations)
        {
            var existingLink = context.ExternalMediaIds
                .FirstOrDefault(e => e.Provider == provider.ProviderKey && e.ExternalId == relation.TargetExternalId);

            bool alreadyKnown = existingLink is not null
                ? context.MediaRelations.Any(m => m.SourceSeriesId == seriesId && m.TargetSeriesId == existingLink.SeriesId)
                : context.ExternalMediaRelations.Any(r => r.SourceSeriesId == seriesId && r.Provider == provider.ProviderKey && r.TargetExternalId == relation.TargetExternalId);
            if (alreadyKnown)
            {
                continue;
            }

            if (existingLink is not null)
            {
                context.MediaRelations.Add(new MediaRelation
                {
                    SourceSeriesId = seriesId,
                    TargetSeriesId = existingLink.SeriesId,
                    RelationType = relation.Type,
                });
            }
            else
            {
                context.ExternalMediaRelations.Add(new ExternalMediaRelation
                {
                    SourceSeriesId = seriesId,
                    Provider = provider.ProviderKey,
                    TargetExternalId = relation.TargetExternalId,
                    TargetTitle = relation.TargetTitle,
                    TargetUrl = relation.TargetUrl,
                    RelationType = relation.Type,
                });
            }
        }

        context.SaveChanges();
    }
}
