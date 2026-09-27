using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// Fetches one issue from both Metron and (optionally) ComicVine and merges them via
/// <see cref="ComicProviderMerge"/> (docs/superpowers/specs/2026-09-23-metron-api-utilization-
/// design.md) - the "adopt" action the design calls for once a <c>cv_id</c> cross-link is confirmed.
///
/// <b>Scope note, disclosed rather than papered over:</b> this class does the actual fetch-and-merge
/// and is fully usable given both providers' issue ids for the same local issue. It has **no UI
/// trigger wired to it** in this pass, because neither <see cref="Issue"/> nor <see cref="Series"/>
/// currently stores its own external id for either provider anywhere durable - <see cref="WantedIssue"/>/
/// <see cref="CatalogIssue"/> carry one provider's id each, tied to the acquisition pipeline
/// specifically, not a general "this issue's ComicVine id AND Metron id, both" store. Wiring a real
/// "Adopt data from the other provider" button onto an issue/series screen needs that store built
/// first - a smaller follow-up, not attempted here. What's real today: given a caller that already
/// knows both ids (e.g. a future backfill, or a future screen that adds that store), this class does
/// the actual work correctly and is tested end-to-end against fake sources.
/// </summary>
public static class ComicProviderAdopt
{
    /// <summary>
    /// Fetches Metron's details (required) and ComicVine's (optional - a null <paramref name="comicVineIssueId"/>,
    /// a missing ComicVine credential, or a ComicVine fetch failure all just mean the merge runs with
    /// Metron alone, matching <see cref="ComicProviderMerge.MergeIssue"/>'s own null-tolerant contract).
    /// </summary>
    public static async Task<ScrapeOutcome> AdoptAsync(
        Func<PaperbunkrDbContext> createContext,
        Func<ComicProvider, IComicVineIssueDetailsSource?> createSource,
        int issueId,
        int metronIssueId,
        int? comicVineIssueId,
        CancellationToken cancellationToken)
    {
        var metronSource = createSource(ComicProvider.Metron);
        if (metronSource is null)
        {
            return ScrapeOutcome.Terminal(ComicProviderFactory.MissingCredentialsMessage(ComicProvider.Metron));
        }

        ComicVineIssueDetails? metronDetails;
        try
        {
            metronDetails = await metronSource.GetIssueDetailsAsync(metronIssueId, cancellationToken).ConfigureAwait(false);
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 100)
        {
            return ScrapeOutcome.Terminal("Metron rejected your login. Check it under Preferences → Connections.");
        }
        catch (ComicVineException ex)
        {
            return ScrapeOutcome.Retry(ex.Message);
        }

        if (metronDetails is null)
        {
            return ScrapeOutcome.Terminal("Metron has no issue with this id (it may have been merged or removed).");
        }

        ComicVineIssueDetails? comicVineDetails = null;
        if (comicVineIssueId is int cvId)
        {
            var comicVineSource = createSource(ComicProvider.ComicVine);
            if (comicVineSource is not null)
            {
                try
                {
                    comicVineDetails = await comicVineSource.GetIssueDetailsAsync(cvId, cancellationToken).ConfigureAwait(false);
                }
                catch (ComicVineException)
                {
                    // Best-effort: adopt still proceeds with Metron alone rather than failing the whole
                    // operation over the secondary, gap-filling provider.
                }
            }
        }

        using var context = createContext();
        var changed = ComicProviderMerge.MergeIssue(context, issueId, metronDetails, comicVineDetails);
        return ScrapeOutcome.Success(issueId, changed);
    }
}
