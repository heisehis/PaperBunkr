using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Merges a Metron issue-detail fetch and an (optional) ComicVine one onto the same tracked
/// <see cref="Issue"/>, by the fixed precedence table from docs/superpowers/specs/2026-09-23-metron-
/// api-utilization-design.md: Metron wins for credits/characters/teams/arcs/universes/genre; ComicVine
/// only ever fills a gap Metron left (Locations - Metron has none at all - plus AgeRating/Isbn/Upc/
/// Genre when Metron's own fetch didn't carry them), never overwriting what Metron just wrote.
/// Everything outside those two lists (title, number, summary, dates, webpage) is left untouched -
/// "whichever provider originally scraped it keeps it" is a no-op for this engine, not something it
/// has to actively track, since those fields already reflect whichever provider last scraped them
/// through the ordinary single-provider scrape pipeline.
///
/// Scope note: like <see cref="ScrapeByIdService"/> (the closest existing precedent), this only
/// touches the fields <see cref="IssueDetailsApplier"/> knows about - Publisher/Imprint/series name
/// are volume-level concerns handled by <see cref="ScrapeOrchestrator"/> instead, and this merge
/// doesn't duplicate that path. Idempotent: every resolver/sync call it makes already is (see their
/// own tests), so running a merge twice with the same inputs changes nothing the second time.
/// </summary>
public static class ComicProviderMerge
{
    private static readonly ScrapeFieldPolicy MetronWinsPolicy = new()
    {
        Enabled = new HashSet<ScrapeField>
        {
            ScrapeField.Crossovers, ScrapeField.Characters, ScrapeField.Teams,
            ScrapeField.Writer, ScrapeField.Penciller, ScrapeField.Inker, ScrapeField.Colorist,
            ScrapeField.Letterer, ScrapeField.CoverArtist, ScrapeField.Editor, ScrapeField.Translator,
            // AgeRating/Isbn/Upc are fields Metron itself exposes (added Phase 2) - included here so
            // Metron's own value wins when present, leaving the ComicVine gap-fill pass below nothing
            // to overwrite it with. Without this, Metron's data was silently skipped and ComicVine's
            // gap-fill "won" by default even when Metron actually had the field (caught by
            // ComicProviderMergeTests.MergeIssue_MetronWinsForCharacters_ComicVineFillsLocationsGap).
            ScrapeField.AgeRating, ScrapeField.Isbn, ScrapeField.Upc, ScrapeField.Genre,
        },
        OverwriteExisting = true,
        IgnoreBlankValues = true,
    };

    private static readonly ScrapeFieldPolicy ComicVineGapFillPolicy = new()
    {
        Enabled = new HashSet<ScrapeField> { ScrapeField.Locations, ScrapeField.AgeRating, ScrapeField.Isbn, ScrapeField.Upc, ScrapeField.Genre },
        OverwriteExisting = false,   // never clobber what Metron's pass just wrote
        IgnoreBlankValues = true,
    };

    /// <summary>Applies both passes to one issue; returns every field either pass actually changed. No-op (empty result) if the issue no longer exists.</summary>
    public static IReadOnlyList<ScrapeField> MergeIssue(PaperbunkrDbContext context, int issueId, ComicVineIssueDetails fromMetron, ComicVineIssueDetails? fromComicVine)
    {
        var issue = context.Issues.Include(i => i.Tags).FirstOrDefault(i => i.Id == issueId);
        if (issue is null)
        {
            return Array.Empty<ScrapeField>();
        }

        var changed = IssueDetailsApplier.Apply(issue, fromMetron, MetronWinsPolicy).ToList();
        issue.MetadataSource = ComicProvider.Metron;
        context.SaveChanges();

        CharacterResolver.SyncFromIssue(context, issue.Id);
        TeamResolver.SyncFromIssue(context, issue.Id);
        CreatorResolver.SyncFromIssue(context, issue.Id);
        ComicMetadataExternalIdSync.SyncFromIssueDetails(context, issue.Id, ComicProvider.Metron, fromMetron);
        ContinuityMetronMatchResolver.SyncFromIssueDetails(context, issue.SeriesId, ComicProvider.Metron, fromMetron.Universes);

        if (fromComicVine is not null)
        {
            var gapFilled = IssueDetailsApplier.Apply(issue, fromComicVine, ComicVineGapFillPolicy);
            context.SaveChanges();
            LocationResolver.SyncFromIssue(context, issue.Id);
            ComicMetadataExternalIdSync.SyncFromIssueDetails(context, issue.Id, ComicProvider.ComicVine, fromComicVine);
            changed.AddRange(gapFilled);
        }

        return changed.Distinct().ToList();
    }
}
