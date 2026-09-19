using Microsoft.EntityFrameworkCore;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Indexers;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Services;

/// <summary>
/// One pass of the acquisition loop (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §5), slice-1 form:
/// refresh followed volumes from ComicVine (low priority), promote due upcoming issues, search Prowlarr for every due
/// <c>Wanted</c> issue, and store scored <see cref="ReleaseCandidate"/>s for the user to approve. <b>It never contacts a
/// download client</b> - grabbing arrives in slice 2. Kept separate from the timer (<see cref="AcquisitionService"/>) so it can be
/// driven one tick at a time in tests.
/// </summary>
public sealed class AcquisitionCycle(
    Func<PaperbunkrDbContext> createContext,
    Func<string, string, IIndexerClient> createIndexer,
    Func<string, IComicVineClient> createComicVine,
    IEventPublisher events,
    Func<DateTime>? now = null)
{
    public const string NotConfiguredAlert = "acquisition-not-configured";
    public const string IndexerAlert = "acquisition-indexer";
    public const string ComicVineAlert = "acquisition-comicvine";

    /// <summary>Volumes refreshed per cycle at most: each costs one or more ComicVine requests from a 200/hour budget shared with the UI.</summary>
    internal const int MaxVolumeRefreshesPerCycle = 15;
    internal const int MaxSearchesPerCycle = 25;
    internal const int MaxCandidatesPerIssue = 10;
    internal static readonly TimeSpan CatalogRefreshAge = TimeSpan.FromHours(12);

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);

    /// <summary>How many cycles in a row ended because an indexer was unreachable; the service backs off on this.</summary>
    public int ConsecutiveIndexerFailures { get; private set; }

    public async Task RunAsync(bool manual, CancellationToken cancellationToken)
    {
        using var context = createContext();
        var settings = context.GetOrCreateAcquisitionSettings();

        if (!settings.Enabled)
        {
            if (manual)
            {
                events.Publish(new DaemonAlertEvent(NotConfiguredAlert, DaemonAlertSeverity.Info, "Comic acquisition is turned off", "Turn it on in Preferences → Acquisition."));
            }

            return;
        }

        var prowlarrKey = CredentialStore.Get(context, "Prowlarr", CredentialKind.ApiKey);
        if (string.IsNullOrWhiteSpace(settings.ProwlarrUrl) || string.IsNullOrWhiteSpace(prowlarrKey))
        {
            events.Publish(new DaemonAlertEvent(NotConfiguredAlert, DaemonAlertSeverity.Warning, "Acquisition needs a Prowlarr address and API key", "Add them in Preferences → Acquisition."));
            return;
        }

        events.Publish(new DaemonAlertClearedEvent(NotConfiguredAlert));
        events.Publish(new CycleStartedEvent(manual));

        try
        {
            await RunCoreAsync(context, settings, createIndexer(settings.ProwlarrUrl, prowlarrKey), manual, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IndexerException ex)
        {
            ConsecutiveIndexerFailures++;
            events.Publish(new DaemonAlertEvent(IndexerAlert, DaemonAlertSeverity.Warning, "Prowlarr couldn't be reached", ex.Message));
            events.Publish(new CycleFailedEvent(ex.Message));
        }
        catch (Exception ex)
        {
            // A cycle must never take the daemon down.
            events.Publish(new CycleFailedEvent($"Acquisition failed: {ex.Message}"));
        }
    }

    private async Task RunCoreAsync(PaperbunkrDbContext context, AcquisitionSettings settings, IIndexerClient indexer, bool manual, CancellationToken cancellationToken)
    {
        var today = _now().Date;

        await RefreshFollowedVolumesAsync(context, cancellationToken).ConfigureAwait(false);
        WantedService.PromoteFollowedUpcoming(context, today);

        var searcher = new ReleaseSearcher(indexer, new ScoringOptions
        {
            MinSizeMb = settings.MinSizeMb,
            MaxSizeMb = settings.MaxSizeMb,
            PreferredGroups = ScoringOptions.SplitList(settings.PreferredReleaseGroups),
            IgnoredWords = ScoringOptions.SplitList(settings.IgnoredWords),
            PreferCbz = settings.PreferCbz,
        });

        // A scheduled cycle skips issues searched within the poll interval; "Search now" (manual) searches everything that is due.
        var recheckBefore = manual ? _now().AddMinutes(1) : _now() - TimeSpan.FromMinutes(Math.Max(15, settings.PollIntervalMinutes));
        var due = WantedService.Due(context, today)
            .Include(w => w.WatchedSeries)
            .Where(w => w.LastSearchedAt == null || w.LastSearchedAt < recheckBefore)
            .OrderBy(w => w.LastSearchedAt ?? DateTime.MinValue)
            .ThenBy(w => w.CreatedAt)
            .Take(MaxSearchesPerCycle)
            .ToList();

        int searched = 0, found = 0;
        foreach (var wanted in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Publish(new CycleProgressEvent(searched, due.Count, $"{wanted.WatchedSeries?.Name} #{wanted.IssueNumber}"));

            var query = new IndexerQuery(wanted.WatchedSeries?.Name ?? string.Empty, wanted.IssueNumber, wanted.StoreDate?.Year);
            var results = await searcher.SearchAsync(query, cancellationToken).ConfigureAwait(false);

            StoreCandidates(context, wanted, results);
            searched++;
            found += Math.Min(results.Count, MaxCandidatesPerIssue);

            if (results.Count > 0)
            {
                events.Publish(new CandidatesFoundEvent(wanted.Id, $"{wanted.WatchedSeries?.Name} #{wanted.IssueNumber}", Math.Min(results.Count, MaxCandidatesPerIssue)));
            }
        }

        ConsecutiveIndexerFailures = 0;
        events.Publish(new DaemonAlertClearedEvent(IndexerAlert));
        events.Publish(new CycleCompletedEvent(
            searched == 0 ? "Nothing to search for." : $"Searched {searched} issue{(searched == 1 ? "" : "s")}, found {found} candidate{(found == 1 ? "" : "s")}.",
            searched, found));
    }

    /// <summary>Refreshes the cached ComicVine issue lists of followed, unpaused volumes that are stale, oldest first, within the per-cycle cap.</summary>
    private async Task RefreshFollowedVolumesAsync(PaperbunkrDbContext context, CancellationToken cancellationToken)
    {
        var apiKey = CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return; // no key: catalogs simply aren't refreshed; searching still works for what's already wanted
        }

        var staleBefore = _now() - CatalogRefreshAge;
        var stale = context.WatchedSeries
            .Where(w => w.WatchFutureReleases && !w.IsPaused && (w.LastRefreshedAt == null || w.LastRefreshedAt < staleBefore))
            .OrderBy(w => w.LastRefreshedAt ?? DateTime.MinValue)
            .Take(MaxVolumeRefreshesPerCycle)
            .ToList();

        if (stale.Count == 0)
        {
            return;
        }

        var comicVine = createComicVine(apiKey);
        foreach (var watched in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var issues = await comicVine.GetVolumeIssuesAsync(watched.ComicVineVolumeId, cancellationToken).ConfigureAwait(false);
                WantedService.RefreshCatalog(context, watched, issues);
            }
            catch (ComicVineException ex) when (ex.ApiStatusCode == 100)
            {
                events.Publish(new DaemonAlertEvent(ComicVineAlert, DaemonAlertSeverity.Warning, "ComicVine rejected your API key", "Update it in Preferences → Connections."));
                return;
            }
            catch (ComicVineException ex)
            {
                // Rate limit (107) or a hiccup: stop refreshing this cycle, keep going with what's already cached.
                events.Publish(new DaemonAlertEvent(ComicVineAlert, DaemonAlertSeverity.Info, "ComicVine updates are paused", ex.Message));
                return;
            }
        }

        events.Publish(new DaemonAlertClearedEvent(ComicVineAlert));
    }

    private void StoreCandidates(PaperbunkrDbContext context, WantedIssue wanted, IReadOnlyList<ScoredRelease> results)
    {
        // Replace, not append: the latest search is the truth, and stale candidates would be offered for grabbing.
        var old = context.ReleaseCandidates.Where(c => c.WantedIssueId == wanted.Id).ToList();
        context.ReleaseCandidates.RemoveRange(old);

        foreach (var scored in results.Take(MaxCandidatesPerIssue))
        {
            context.ReleaseCandidates.Add(new ReleaseCandidate
            {
                WantedIssueId = wanted.Id,
                Title = scored.Release.Title,
                DownloadUrl = scored.Release.DownloadUrl,
                Guid = scored.Release.Guid,
                SizeBytes = scored.Release.SizeBytes,
                Seeders = scored.Release.Seeders,
                Indexer = scored.Release.Indexer,
                Score = scored.Score,
                IsPack = scored.IsPack,
                FoundAt = _now(),
            });
        }

        wanted.LastSearchedAt = _now();
        context.SaveChanges();
    }
}
