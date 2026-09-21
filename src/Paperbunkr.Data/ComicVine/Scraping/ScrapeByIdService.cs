using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>How one by-id scrape ended.</summary>
public enum ScrapeResultKind
{
    /// <summary>ComicVine's details were fetched and applied (possibly changing nothing, if the issue already had them all).</summary>
    Scraped,

    /// <summary>Worth trying again later: a network problem, a rate limit, a ComicVine hiccup.</summary>
    Retryable,

    /// <summary>Retrying won't help until the user acts: ComicVine has no such issue, the key is missing or rejected, or the local issue is gone.</summary>
    Terminal,
}

/// <param name="Kind">How it ended.</param>
/// <param name="Message">Plain-language reason for a failure; <c>null</c> on success.</param>
/// <param name="IssueId">The local issue that was updated (for the file write-back), when one was.</param>
/// <param name="Changed">The fields that actually changed.</param>
public sealed record ScrapeOutcome(ScrapeResultKind Kind, string? Message, int? IssueId, IReadOnlyList<ScrapeField> Changed)
{
    public static ScrapeOutcome Success(int issueId, IReadOnlyList<ScrapeField> changed) => new(ScrapeResultKind.Scraped, null, issueId, changed);

    public static ScrapeOutcome Retry(string message) => new(ScrapeResultKind.Retryable, message, null, Array.Empty<ScrapeField>());

    public static ScrapeOutcome Terminal(string message) => new(ScrapeResultKind.Terminal, message, null, Array.Empty<ScrapeField>());
}

/// <summary>
/// Adds ComicVine's details to an imported issue using the ComicVine issue id the want already carries: no series search, no scoring, no review
/// (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md section 6). It never throws for an expected failure - the caller gets an
/// <see cref="ScrapeOutcome"/> that says whether trying again could help. The caller owns the status bookkeeping and the file write-back.
/// </summary>
public sealed class ScrapeByIdService(Func<PaperbunkrDbContext> createContext, Func<ComicProvider, IComicVineIssueDetailsSource?> createSource, ScrapeFieldPolicy? policy = null)
{
    private readonly ScrapeFieldPolicy _policy = policy ?? ScrapeFieldPolicy.Default;

    public async Task<ScrapeOutcome> ScrapeAsync(int wantedIssueId, CancellationToken cancellationToken)
    {
        int? issueId;
        int comicVineIssueId;
        ComicProvider provider;
        using (var context = createContext())
        {
            var wanted = await context.WantedIssues.AsNoTracking().FirstOrDefaultAsync(w => w.Id == wantedIssueId, cancellationToken).ConfigureAwait(false);
            if (wanted is null)
            {
                return ScrapeOutcome.Terminal("This wanted issue no longer exists.");
            }

            issueId = wanted.IssueId;
            comicVineIssueId = wanted.ExternalIssueId;
            provider = wanted.Provider;
        }

        if (issueId is null)
        {
            return ScrapeOutcome.Terminal("The imported file isn't in your library, so there is nothing to add details to.");
        }

        var source = createSource(provider);
        if (source is null)
        {
            return ScrapeOutcome.Terminal(ComicProviderFactory.MissingCredentialsMessage(provider).Replace(" first.", " to add details to downloaded issues."));
        }

        ComicVineIssueDetails? details;
        try
        {
            details = await source.GetIssueDetailsAsync(comicVineIssueId, cancellationToken).ConfigureAwait(false);
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 100)
        {
            return ScrapeOutcome.Terminal(provider == ComicProvider.Metron ? "Metron rejected your login. Check it under Preferences → Connections." : "ComicVine rejected your API key. Check it under Preferences → Connections.");
        }
        catch (ComicVineException ex)
        {
            return ScrapeOutcome.Retry(ex.Message);
        }

        if (details is null)
        {
            return ScrapeOutcome.Terminal($"{ComicProviderFactory.DisplayName(provider)} has no issue with this id (it may have been merged or removed).");
        }

        using var write = createContext();
        var issue = await write.Issues.Include(i => i.Series).FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken).ConfigureAwait(false);
        if (issue is null)
        {
            return ScrapeOutcome.Terminal("The issue was removed from your library before its details could be added.");
        }

        var changed = IssueDetailsApplier.Apply(issue, details, _policy);
        if (changed.Count > 0 || issue.MetadataSource != provider)
        {
            issue.MetadataSource = provider;
            await write.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return ScrapeOutcome.Success(issue.Id, changed);
    }
}
