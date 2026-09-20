using Microsoft.EntityFrameworkCore;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Services;

/// <summary>
/// Adds ComicVine's details to imported issues, and keeps trying until they arrive (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md 6.2).
/// The record of what still needs doing is the <see cref="WantedIssue.ScrapeStatus"/> column, never a transient UI alert: a row is <c>Pending</c> from the moment its
/// import is saved, becomes <c>Scraped</c> on success, and <c>Failed</c> otherwise. A retryable failure is tried again with a growing delay up to
/// <see cref="MaxAttempts"/>; a failure that retrying can't fix (no such issue upstream, no key) is terminal and left for the user, which also means ComicVine is
/// never asked again about an issue it has already said doesn't exist.
/// <para>
/// A pass handles at most <see cref="BatchSize"/> rows, at whatever ComicVine priority the injected service was built with (the host uses Low), so a big
/// backlog trickles through instead of spending the shared rate-limit budget in one go.
/// </para>
/// </summary>
public sealed class ScrapeSweeper(
    Func<PaperbunkrDbContext> createContext,
    ScrapeByIdService scraper,
    IEventPublisher events,
    Action<int>? onScraped = null,
    Func<DateTime>? now = null)
{
    public const int MaxAttempts = 5;
    public const int BatchSize = 5;

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);

    /// <summary>How long to wait after the given number of failed attempts before trying again (5, 20, 45, 80 minutes).</summary>
    public static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(5.0 * attempts * attempts);

    /// <summary>A cheap check the host loop can run every tick: is there anything that could be scraped right now?</summary>
    public bool HasWork()
    {
        using var context = createContext();
        return context.GetOrCreateAcquisitionSettings().ScrapeOnImport && Eligible(context).Any();
    }

    /// <summary>One pass. Returns how many rows were attempted.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        List<int> ids;
        using (var context = createContext())
        {
            if (!context.GetOrCreateAcquisitionSettings().ScrapeOnImport)
            {
                return 0;
            }

            ids = Eligible(context).OrderBy(w => w.ImportedAt).Select(w => w.Id).Take(BatchSize).ToList();
        }

        foreach (int id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScrapeOneAsync(id, cancellationToken).ConfigureAwait(false);
        }

        return ids.Count;
    }

    private IQueryable<WantedIssue> Eligible(PaperbunkrDbContext context)
    {
        var now = _now();
        return context.WantedIssues.Where(w =>
            w.IssueId != null
            && (w.ScrapeStatus == ScrapeStatus.Pending
                || (w.ScrapeStatus == ScrapeStatus.Failed
                    && !w.ScrapeFailureIsTerminal
                    && w.ScrapeAttempts < MaxAttempts
                    && w.ScrapeLastAttemptAt != null
                    && w.ScrapeLastAttemptAt <= now.AddMinutes(-5.0 * w.ScrapeAttempts * w.ScrapeAttempts))));
    }

    private async Task ScrapeOneAsync(int wantedId, CancellationToken cancellationToken)
    {
        var outcome = await scraper.ScrapeAsync(wantedId, cancellationToken).ConfigureAwait(false);

        string label;
        bool willRetry = false;
        using (var context = createContext())
        {
            var wanted = context.WantedIssues.Include(w => w.WatchedSeries).FirstOrDefault(w => w.Id == wantedId);
            if (wanted is null)
            {
                return;
            }

            label = $"{wanted.WatchedSeries?.Name} #{wanted.IssueNumber}";
            wanted.ScrapeAttempts++;
            wanted.ScrapeLastAttemptAt = _now();

            if (outcome.Kind == ScrapeResultKind.Scraped)
            {
                wanted.ScrapeStatus = ScrapeStatus.Scraped;
                wanted.ScrapeError = null;
                wanted.ScrapeFailureIsTerminal = false;
            }
            else
            {
                wanted.ScrapeStatus = ScrapeStatus.Failed;
                wanted.ScrapeError = outcome.Message;
                wanted.ScrapeFailureIsTerminal = outcome.Kind == ScrapeResultKind.Terminal;
                willRetry = outcome.Kind == ScrapeResultKind.Retryable && wanted.ScrapeAttempts < MaxAttempts;
            }

            context.SaveChanges();
        }

        if (outcome.Kind == ScrapeResultKind.Scraped)
        {
            // Tagging the file is the host's concern, and only worth doing when something actually changed.
            if (outcome.IssueId is int issueId && outcome.Changed.Count > 0)
            {
                onScraped?.Invoke(issueId);
            }

            events.Publish(new IssueScrapedEvent(wantedId, label, outcome.Changed.Count));
        }
        else
        {
            events.Publish(new IssueScrapeFailedEvent(wantedId, label, outcome.Message ?? "Couldn't add ComicVine details.", willRetry));
        }
    }

    /// <summary>Puts a failed row back in the queue now (the user's Retry, single or bulk). Returns false when there was nothing to retry.</summary>
    public static bool Requeue(PaperbunkrDbContext context, int wantedId)
    {
        var wanted = context.WantedIssues.FirstOrDefault(w => w.Id == wantedId && w.ScrapeStatus == ScrapeStatus.Failed);
        if (wanted is null)
        {
            return false;
        }

        wanted.ScrapeStatus = ScrapeStatus.Pending;
        wanted.ScrapeAttempts = 0;
        wanted.ScrapeFailureIsTerminal = false;
        wanted.ScrapeError = null;
        context.SaveChanges();
        return true;
    }

    /// <summary>Gives up on a failed row (the user's Dismiss): the issue stays as it is and the row leaves the needs-review list.</summary>
    public static bool Dismiss(PaperbunkrDbContext context, int wantedId)
    {
        var wanted = context.WantedIssues.FirstOrDefault(w => w.Id == wantedId && w.ScrapeStatus == ScrapeStatus.Failed);
        if (wanted is null)
        {
            return false;
        }

        wanted.ScrapeStatus = ScrapeStatus.NotApplicable;
        context.SaveChanges();
        return true;
    }
}
