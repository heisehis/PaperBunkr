using System.Globalization;
using System.Linq;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Attaches <see cref="ComicMetadataExternalId"/> rows to the Character/Team/Location/Creator entities
/// a scrape just (re)materialized, using the ids <see cref="ComicVineIssueDetails"/> already carries
/// (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md). Deliberately separate from
/// the individual resolvers (<see cref="CharacterResolver"/> etc.): those only ever re-derive rows
/// from the persisted flat-text fields, which have no id information once written - id attachment has
/// to happen here, right after a scrape, while the raw provider ids are still in memory. Call this
/// after the matching <c>SyncFromIssue</c> calls, not before - the entities must already exist.
///
/// Cross-checks every name against the issue's current flat-text fields before attaching anything
/// (found the hard way: a restrictive <c>ScrapeFieldPolicy</c> - or <see cref="ComicProviderMerge"/>'s
/// deliberately narrow gap-fill policy - can leave <paramref name="details"/> carrying names that were
/// never actually written to the issue; attaching an id for one of those would silently materialize an
/// entity nothing in the issue's own text ever names, and risks re-using a stale external-id row if
/// that phantom entity's later pruned).
/// </summary>
public static class ComicMetadataExternalIdSync
{
    public static void SyncFromIssueDetails(PaperbunkrDbContext context, int issueId, ComicProvider provider, ComicVineIssueDetails details)
    {
        var issue = context.Issues.FirstOrDefault(i => i.Id == issueId);
        if (issue is null)
        {
            return;
        }

        var currentCharacters = CharacterResolver.ParseNames(issue.Characters);
        var currentTeams = TeamResolver.ParseNames(issue.Teams);
        var currentLocations = LocationResolver.ParseNames(issue.Locations);
        var currentCreators = new[] { issue.Writer, issue.Penciller, issue.Inker, issue.Colorist, issue.Letterer, issue.CoverArtist, issue.Editor, issue.Translator }
            .SelectMany(CharacterResolver.ParseNames)
            .ToList();

        foreach (var character in details.Characters.Where(c => currentCharacters.Contains(c.Name, StringComparer.OrdinalIgnoreCase)))
        {
            Attach(context, ComicMetadataEntityKind.Character, provider, character, () => CharacterResolver.GetOrCreate(context, character.Name).Id);
        }

        foreach (var team in details.Teams.Where(t => currentTeams.Contains(t.Name, StringComparer.OrdinalIgnoreCase)))
        {
            Attach(context, ComicMetadataEntityKind.Team, provider, team, () => TeamResolver.GetOrCreate(context, team.Name).Id);
        }

        foreach (var location in details.Locations.Where(l => currentLocations.Contains(l.Name, StringComparer.OrdinalIgnoreCase)))
        {
            Attach(context, ComicMetadataEntityKind.Location, provider, location, () => LocationResolver.GetOrCreate(context, location.Name).Id);
        }

        foreach (var credit in details.Credits.Where(c => c.CreatorExternalId is not null && currentCreators.Contains(c.Name, StringComparer.OrdinalIgnoreCase)))
        {
            Attach(context, ComicMetadataEntityKind.Creator, provider, new ComicVineIdName(credit.CreatorExternalId, credit.Name),
                () => CreatorResolver.GetOrCreate(context, credit.Name).Id);
        }
    }

    private static void Attach(PaperbunkrDbContext context, ComicMetadataEntityKind kind, ComicProvider provider, ComicVineIdName idName, Func<int> resolveEntityId)
    {
        if (idName.ExternalId is not int externalId || string.IsNullOrWhiteSpace(idName.Name))
        {
            return;
        }

        AttachEntityId(context, kind, provider, resolveEntityId(), externalId);
    }

    /// <summary>
    /// Upserts a <see cref="ComicMetadataExternalId"/> row directly, for a kind that already <i>is</i>
    /// the identified row rather than something resolved by name (<see cref="ComicMetadataEntityKind.Issue"/>/
    /// <see cref="ComicMetadataEntityKind.Series"/>, docs/superpowers/specs/2026-09-24-comicvine-scraper-
    /// fidelity-design.md §2.7) - the other five kinds go through <see cref="Attach"/> instead, since
    /// those need a resolver to find/create the row first.
    /// </summary>
    public static void AttachEntityId(PaperbunkrDbContext context, ComicMetadataEntityKind kind, ComicProvider provider, int entityId, int externalId)
    {
        string externalIdText = externalId.ToString(CultureInfo.InvariantCulture);

        var existing = context.ComicMetadataExternalIds
            .FirstOrDefault(e => e.EntityKind == kind && e.EntityId == entityId && e.Provider == provider);

        if (existing is null)
        {
            // (EntityKind, Provider, ExternalId) is unique - one provider id can belong to exactly one
            // local row. Two local rows resolving to the same provider id (a duplicate copy of a book, "1"
            // vs "01" both numerically matching one ComicVine issue, two local series folding onto one
            // volume) used to hit that index and throw out of the whole scrape batch. The first row keeps
            // the link; the second just doesn't get one - its scraped fields were already saved.
            bool ownedByAnotherEntity = context.ComicMetadataExternalIds
                .Any(e => e.EntityKind == kind && e.Provider == provider && e.ExternalId == externalIdText);
            if (ownedByAnotherEntity)
            {
                return;
            }

            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId
            {
                EntityKind = kind,
                EntityId = entityId,
                Provider = provider,
                ExternalId = externalIdText,
            });
            context.SaveChanges();
        }
        else if (existing.ExternalId != externalIdText)
        {
            bool takenByAnotherEntity = context.ComicMetadataExternalIds
                .Any(e => e.EntityKind == kind && e.Provider == provider && e.ExternalId == externalIdText && e.Id != existing.Id);
            if (takenByAnotherEntity)
            {
                return;
            }

            existing.ExternalId = externalIdText;
            context.SaveChanges();
        }
    }
}
