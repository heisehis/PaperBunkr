using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>What one <see cref="EntityProfileCompletion.CompleteAsync"/> call did.</summary>
public enum EntityProfileOutcome
{
    /// <summary>Not a profile kind, no such entity, or checked recently.</summary>
    NotDue,

    /// <summary>No provider id, or no credentials for the providers it has ids for: nothing was asked.</summary>
    NoSource,

    /// <summary>A provider answered (a profile or "no such record"); the stamp was set.</summary>
    Checked,

    /// <summary>Every provider asked failed (network, quota, API error); not stamped, retried later.</summary>
    Failed,
}

/// <summary>
/// Fills a character's, team's, creator's or location's image, description and aliases from Metron, then ComicVine, then (creators only)
/// the AniList and MangaDex staff of their linked manga series
/// (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.3). Modelled on <see cref="StoryEventDetailsCompletion"/>, with
/// one deliberate difference (plan correction 7): a provider that answers "no such record" stamps <c>ImageCheckedAt</c>, but a network or API
/// failure does not, so the next open retries. Only entities with a provider id, or creators on a linked manga series, are looked up; there is no open search by name.
/// </summary>
public sealed class EntityProfileCompletion
{
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(30);

    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Func<PaperbunkrDbContext, ComicProvider, IEntityProfileSource?> _sourceFor;
    private readonly Func<string, CancellationToken, Task<bool>> _download;
    private readonly Func<DateTime> _utcNow;
    private readonly IMangaStaffSource _mangaStaff;

    /// <summary>At most this many linked series per manga provider are asked about one creator.</summary>
    public const int MangaSeriesPerProvider = 3;

    /// <summary>At most this many of an entity's issues are looked up to find its Metron or ComicVine id when it has none.</summary>
    public const int IdDiscoveryIssues = 2;

    public EntityProfileCompletion(
        Func<PaperbunkrDbContext>? contextFactory = null,
        Func<PaperbunkrDbContext, ComicProvider, IEntityProfileSource?>? sourceFor = null,
        Func<string, CancellationToken, Task<bool>>? download = null,
        Func<DateTime>? utcNow = null,
        IMangaStaffSource? mangaStaff = null,
        ComicVineRequestPriority priority = ComicVineRequestPriority.Low)
    {
        // Low for the background fill; the entity page passes High, since opening a page is a request the user is waiting on. At Low, a big scrape
        // left page lookups queued behind ComicVine's hourly window or refused by Metron's daily reserve, so nothing filled in (2026-11-12).
        _contextFactory = contextFactory ?? (() => PaperbunkrDb.CreateContext());
        _sourceFor = sourceFor ?? ((context, provider) => ComicProviderFactory.Create(context, provider, priority) as IEntityProfileSource);
        _download = download ?? EntityImageCache.DownloadAndCacheAsync;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _mangaStaff = mangaStaff ?? new MangaStaffProfiles();
    }

    /// <summary>The kinds that have provider profiles. Locations come from ComicVine only (Metron has none).</summary>
    public static bool Supports(ComicMetadataEntityKind kind) =>
        kind is ComicMetadataEntityKind.Character or ComicMetadataEntityKind.Team or ComicMetadataEntityKind.Creator or ComicMetadataEntityKind.Location;

    /// <summary>One provider to ask, in order: where its image counts as coming from, and the lookup itself.</summary>
    private sealed record Attempt(EntityImageSource Source, Func<Task<EntityProfile?>> Fetch);

    /// <summary>Whether a profile is due for a (re)check.</summary>
    public static bool IsDue(IHasEntityProfile entity, DateTime utcNow) =>
        entity.ImageCheckedAt is not DateTime checkedAt || checkedAt < utcNow - RecheckAfter;

    /// <summary>Looks the entity up when it is due.</summary>
    public async Task<EntityProfileOutcome> CompleteAsync(ComicMetadataEntityKind kind, int entityId, CancellationToken cancellationToken)
    {
        if (!Supports(kind))
        {
            return EntityProfileOutcome.NotDue;
        }

        using var context = _contextFactory();
        var entity = Find(context, kind, entityId);
        DateTime now = _utcNow();
        if (entity is null || !IsDue(entity, now))
        {
            return EntityProfileOutcome.NotDue;
        }

        var ids = context.ComicMetadataExternalIds
            .Where(e => e.EntityKind == kind && e.EntityId == entityId)
            .Select(e => new { e.Provider, e.ExternalId })
            .ToList();

        // No Metron or ComicVine id (ids are only recorded when an issue is scraped, and only since late September): look up one of the entity's
        // own issues that has a provider issue id. Its cast and credits carry ids, which are attached for every name on that issue.
        bool discoveryFailed = false;
        if (ids.Count == 0)
        {
            var discovery = await DiscoverIdsAsync(context, kind, entityId, cancellationToken).ConfigureAwait(false);
            discoveryFailed = discovery == IdDiscovery.Failed;
            if (discovery == IdDiscovery.Found)
            {
                ids = context.ComicMetadataExternalIds
                    .Where(e => e.EntityKind == kind && e.EntityId == entityId)
                    .Select(e => new { e.Provider, e.ExternalId })
                    .ToList();
            }
        }

        // Metron, then ComicVine, by the entity's own provider ids. Then, for a creator, the staff of the series they are credited on that are
        // linked to AniList, then MangaDex (manga creators rarely have Metron or ComicVine ids). Never an open search by name.
        // Lazy, so a later provider is only set up when the loop reaches it (it stops once everything is filled).
        IEnumerable<Attempt> Attempts()
        {
            foreach (var provider in new[] { ComicProvider.Metron, ComicProvider.ComicVine })
            {
                string? externalId = ids.FirstOrDefault(i => i.Provider == provider)?.ExternalId;
                if (string.IsNullOrWhiteSpace(externalId) || _sourceFor(context, provider) is not { } source)
                {
                    continue;
                }

                yield return new Attempt(provider == ComicProvider.Metron ? EntityImageSource.Metron : EntityImageSource.ComicVine,
                    () => source.GetEntityProfileAsync(kind, externalId, cancellationToken));
            }

            if (kind == ComicMetadataEntityKind.Creator)
            {
                foreach (var attempt in MangaAttempts(context, entityId, entity.Name, cancellationToken))
                {
                    yield return attempt;
                }
            }
        }

        bool answered = false;
        bool asked = false;
        bool changed = false;
        bool failed = false;
        bool descriptionFilled = false, aliasesFilled = false, imageFilled = false;
        foreach (var attempt in Attempts())
        {
            asked = true;
            EntityProfile? profile;
            try
            {
                profile = await attempt.Fetch().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                failed = true;
                continue; // offline, rate-limited or an API error: leave the stamp unset so the next open retries
            }

            if (profile is null)
            {
                answered = true; // the provider has no such record; try the next one
                continue;
            }

            if (!descriptionFilled && profile.Description is { } description)
            {
                descriptionFilled = true;
                if (entity.Description != description) { entity.Description = description; changed = true; }
            }

            if (!aliasesFilled && profile.Aliases.Count > 0)
            {
                aliasesFilled = true;
                string aliases = string.Join(", ", profile.Aliases);
                if (entity.Aliases != aliases)
                {
                    entity.Aliases = aliases;
                    changed = true;
                }
            }

            if (imageFilled || profile.ImageUrl is not { } url)
            {
                answered = true;
                if (imageFilled && descriptionFilled && aliasesFilled) break;
                continue; // no image here; the next provider may have one, even on a refresh
            }

            bool downloaded;
            try
            {
                downloaded = await _download(url, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                downloaded = false;
            }

            if (!downloaded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                failed = true;
                continue; // the image could not be fetched: not stamped, retried next time
            }

            answered = true;
            imageFilled = true;
            failed = false; // a successful fallback completes the check
            entity.ImageUrl = url;
            entity.ImageSource = entity.CustomImagePath is not null ? EntityImageSource.Custom : attempt.Source;
            changed = true;
            if (descriptionFilled && aliasesFilled) break;
        }

        if (answered && !failed)
        {
            entity.ImageCheckedAt = now;
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (imageFilled)
            {
                EntityChipImages.RaiseChanged(); // chips on a kept-alive Detail screen pick the new image up
            }
        }

        return failed ? EntityProfileOutcome.Failed : answered ? EntityProfileOutcome.Checked : asked || discoveryFailed ? EntityProfileOutcome.Failed : EntityProfileOutcome.NoSource;
    }

    public static IHasEntityProfile? Find(PaperbunkrDbContext context, ComicMetadataEntityKind kind, int entityId) => kind switch
    {
        ComicMetadataEntityKind.Character => context.Characters.FirstOrDefault(c => c.Id == entityId),
        ComicMetadataEntityKind.Team => context.Teams.FirstOrDefault(t => t.Id == entityId),
        ComicMetadataEntityKind.Creator => context.Creators.FirstOrDefault(c => c.Id == entityId),
        ComicMetadataEntityKind.Location => context.Locations.FirstOrDefault(l => l.Id == entityId),
        _ => null,
    };

    private enum IdDiscovery { NotFound, Found, Failed }

    /// <summary>
    /// Finds a Metron or ComicVine id for an entity that has none, from up to <see cref="IdDiscoveryIssues"/> of its own issues that carry a provider
    /// issue id (Metron first; ComicVine only for locations, which Metron does not have). Each issue's details attach ids for every name on it.
    /// </summary>
    private async Task<IdDiscovery> DiscoverIdsAsync(PaperbunkrDbContext context, ComicMetadataEntityKind kind, int entityId, CancellationToken cancellationToken)
    {
        IQueryable<int>? issueIds = kind switch
        {
            ComicMetadataEntityKind.Character => context.CharacterAppearances.Where(a => a.CharacterId == entityId).Select(a => a.IssueId),
            ComicMetadataEntityKind.Team => context.TeamAppearances.Where(a => a.TeamId == entityId).Select(a => a.IssueId),
            ComicMetadataEntityKind.Location => context.LocationAppearances.Where(a => a.LocationId == entityId).Select(a => a.IssueId),
            ComicMetadataEntityKind.Creator => context.CreatorCredits.Where(c => c.CreatorId == entityId).Select(c => c.IssueId),
            _ => null,
        };
        if (issueIds is null)
        {
            return IdDiscovery.NotFound;
        }

        bool failed = false;
        var providers = kind == ComicMetadataEntityKind.Location ? new[] { ComicProvider.ComicVine } : new[] { ComicProvider.Metron, ComicProvider.ComicVine };
        foreach (var provider in providers)
        {
            var candidates = context.ComicMetadataExternalIds
                .Where(e => e.EntityKind == ComicMetadataEntityKind.Issue && e.Provider == provider && issueIds.Contains(e.EntityId))
                .OrderByDescending(e => e.EntityId)
                .Select(e => new { e.EntityId, e.ExternalId })
                .Take(IdDiscoveryIssues)
                .ToList();
            if (candidates.Count == 0 || _sourceFor(context, provider) is not IComicVineIssueDetailsSource source)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (!int.TryParse(candidate.ExternalId, out int providerIssueId))
                {
                    continue;
                }

                ComicVineIssueDetails? details;
                try
                {
                    details = await source.GetIssueDetailsAsync(providerIssueId, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    failed = true;
                    continue;
                }

                if (details is null)
                {
                    continue;
                }

                ComicMetadataExternalIdSync.SyncFromIssueDetails(context, candidate.EntityId, provider, details);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (context.ComicMetadataExternalIds.Any(e => e.EntityKind == kind && e.EntityId == entityId))
                {
                    return IdDiscovery.Found;
                }
            }
        }

        return failed ? IdDiscovery.Failed : IdDiscovery.NotFound;
    }

    /// <summary>
    /// AniList, then MangaDex: the series this creator is credited on that are linked to that provider, most credited first, at most
    /// <see cref="MangaSeriesPerProvider"/> each. Each asks that series' staff for the creator by name.
    /// </summary>
    private IEnumerable<Attempt> MangaAttempts(PaperbunkrDbContext context, int creatorId, string name, CancellationToken cancellationToken)
    {
        var seriesIds = context.CreatorCredits
            .Where(c => c.CreatorId == creatorId)
            .GroupBy(c => c.Issue!.SeriesId)
            .Select(g => new { SeriesId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Select(g => g.SeriesId)
            .Take(20)
            .ToList();
        if (seriesIds.Count == 0)
        {
            return Array.Empty<Attempt>();
        }

        var links = context.ExternalMediaIds
            .Where(e => seriesIds.Contains(e.SeriesId) && (e.Provider == ExternalMetadataProvider.AniList || e.Provider == ExternalMetadataProvider.MangaDex))
            .Select(e => new { e.SeriesId, e.Provider, e.ExternalId })
            .ToList();

        var attempts = new List<Attempt>();
        foreach (var provider in new[] { ExternalMetadataProvider.AniList, ExternalMetadataProvider.MangaDex })
        {
            foreach (var link in links.Where(l => l.Provider == provider && !string.IsNullOrWhiteSpace(l.ExternalId))
                         .OrderBy(l => seriesIds.IndexOf(l.SeriesId)).Take(MangaSeriesPerProvider))
            {
                string externalId = link.ExternalId;
                attempts.Add(new Attempt(provider == ExternalMetadataProvider.AniList ? EntityImageSource.AniList : EntityImageSource.MangaDex,
                    () => _mangaStaff.FindAsync(provider, externalId, name, cancellationToken)));
            }
        }

        return attempts;
    }
}
