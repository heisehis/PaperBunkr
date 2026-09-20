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
    Func<DateTime>? now = null,
    GrabService? grabService = null,
    Func<string, string, IComicVineClient>? createMetron = null,
    Func<string, string, IPullListSource>? createPullListSource = null)
{
    public const string NotConfiguredAlert = "acquisition-not-configured";
    public const string IndexerAlert = "acquisition-indexer";
    public const string ComicVineAlert = "acquisition-comicvine";
    public const string MetronAlert = "acquisition-metron";

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

        // Anything the library already owns (added by hand, by a scan, by another download) is closed first; an unfinished duplicate torrent is dropped.
        foreach (var closed in WantedService.CloseOwned(context))
        {
            if (grabService is not null)
            {
                await grabService.DiscardIfIncompleteAsync(closed.TorrentHash!, cancellationToken).ConfigureAwait(false);
            }
        }

        await RefreshFollowedVolumesAsync(context, cancellationToken).ConfigureAwait(false);
        await RefreshPullListAsync(context, manual, cancellationToken).ConfigureAwait(false);
        WantedService.PromoteFollowedUpcoming(context, today);
        PullListService.PromoteFollowedReleases(context, today);

        var searcher = new ReleaseSearcher(indexer, new ScoringOptions
        {
            MinSizeMb = settings.MinSizeMb,
            MaxSizeMb = settings.MaxSizeMb,
            PreferredGroups = ScoringOptions.SplitList(settings.PreferredReleaseGroups),
            IgnoredWords = ScoringOptions.SplitList(settings.IgnoredWords),
            PreferCbz = settings.PreferCbz,
        }, BlocklistService.Load(context));

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

            var stored = StoreCandidates(context, wanted, results);
            searched++;

            // Auto-grab (off by default): the best non-pack candidate, if it clears the user's score bar. Otherwise the user decides.
            if (settings.AutoGrab && grabService is not null && await TryAutoGrabAsync(stored, settings.AutoGrabMinScore, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            found += stored.Count;
            if (stored.Count > 0)
            {
                events.Publish(new CandidatesFoundEvent(wanted.Id, $"{wanted.WatchedSeries?.Name} #{wanted.IssueNumber}", stored.Count));
            }
        }

        ConsecutiveIndexerFailures = 0;
        events.Publish(new DaemonAlertClearedEvent(IndexerAlert));
        events.Publish(new CycleCompletedEvent(
            searched == 0 ? "Nothing to search for." : $"Searched {searched} issue{(searched == 1 ? "" : "s")}, found {found} candidate{(found == 1 ? "" : "s")}.",
            searched, found));
    }

    /// <summary>
    /// Refreshes the cached issue lists of followed, unpaused volumes that are stale, oldest first, within the per-cycle cap. Each series is refreshed through its own provider
    /// (ComicVine or Metron); a provider whose credentials aren't saved is simply skipped, and each provider has its own alert so a bad Metron login never hides a ComicVine problem.
    /// </summary>
    private async Task RefreshFollowedVolumesAsync(PaperbunkrDbContext context, CancellationToken cancellationToken)
    {
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

        var clients = new Dictionary<ComicProvider, IComicVineClient?>();
        var stopped = new HashSet<ComicProvider>();
        var touched = new HashSet<ComicProvider>();

        foreach (var watched in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = watched.Provider;
            if (stopped.Contains(provider))
            {
                continue;
            }

            if (!clients.TryGetValue(provider, out var client))
            {
                client = clients[provider] = ClientFor(context, provider);
            }

            if (client is null)
            {
                continue; // no credentials for this provider: its catalogs simply aren't refreshed; searching still works for what's already wanted
            }

            touched.Add(provider);
            var name = ComicProviderFactory.DisplayName(provider);
            var alert = provider == ComicProvider.Metron ? MetronAlert : ComicVineAlert;
            try
            {
                var issues = await client.GetVolumeIssuesAsync(watched.ExternalVolumeId, cancellationToken).ConfigureAwait(false);
                WantedService.RefreshCatalog(context, watched, issues);
            }
            catch (ComicVineException ex) when (ex.ApiStatusCode == 100)
            {
                events.Publish(new DaemonAlertEvent(alert, DaemonAlertSeverity.Warning, provider == ComicProvider.Metron ? "Metron rejected your login" : "ComicVine rejected your API key", "Update it in Preferences → Connections."));
                stopped.Add(provider);
            }
            catch (ComicVineException ex)
            {
                // Rate limit (107) or a hiccup: stop refreshing this provider for this cycle, keep going with what's already cached.
                events.Publish(new DaemonAlertEvent(alert, DaemonAlertSeverity.Info, $"{name} updates are paused", ex.Message));
                stopped.Add(provider);
            }
        }

        foreach (var provider in touched.Where(p => !stopped.Contains(p)))
        {
            events.Publish(new DaemonAlertClearedEvent(provider == ComicProvider.Metron ? MetronAlert : ComicVineAlert));
        }
    }

    /// <summary>
    /// The weekly pull list: refetched about twice a day (an hour apart at the soonest for "Search now"), and only with a Metron login saved. A failure never stops the cycle - the
    /// wants already made and the cached list stay as they are.
    /// </summary>
    private async Task RefreshPullListAsync(PaperbunkrDbContext context, bool manual, CancellationToken cancellationToken)
    {
        if (!PullListService.IsDue(context, _now(), manual))
        {
            return;
        }

        var user = CredentialStore.Get(context, "Metron", CredentialKind.Username);
        var password = CredentialStore.Get(context, "Metron", CredentialKind.Password);
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password))
        {
            return;
        }

        var source = (createPullListSource ?? ((u, p) => new MetronClient(u, p, ComicVineRequestPriority.Low)))(user, password);
        try
        {
            await PullListService.RefreshAsync(context, source, _now().Date, cancellationToken).ConfigureAwait(false);
            events.Publish(new DaemonAlertClearedEvent(MetronAlert));
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 100)
        {
            events.Publish(new DaemonAlertEvent(MetronAlert, DaemonAlertSeverity.Warning, "Metron rejected your login", "Update it in Preferences → Connections."));
        }
        catch (ComicVineException ex)
        {
            events.Publish(new DaemonAlertEvent(MetronAlert, DaemonAlertSeverity.Info, "The weekly pull list is paused", ex.Message));
        }
    }

    /// <summary>The background (low-priority) client for a provider, or <c>null</c> when its credentials aren't saved.</summary>
    private IComicVineClient? ClientFor(PaperbunkrDbContext context, ComicProvider provider)
    {
        if (provider == ComicProvider.Metron)
        {
            var user = CredentialStore.Get(context, "Metron", CredentialKind.Username);
            var password = CredentialStore.Get(context, "Metron", CredentialKind.Password);
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrEmpty(password))
            {
                return null;
            }

            return (createMetron ?? ((u, p) => new MetronClient(u, p, ComicVineRequestPriority.Low)))(user, password);
        }

        var apiKey = CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey);
        return string.IsNullOrWhiteSpace(apiKey) ? null : createComicVine(apiKey);
    }

    private async Task<bool> TryAutoGrabAsync(IReadOnlyList<ReleaseCandidate> candidates, int minScore, CancellationToken cancellationToken)
    {
        var best = candidates.Where(c => !c.IsPack && c.Score >= minScore).OrderByDescending(c => c.Score).FirstOrDefault();
        if (best is null)
        {
            return false;
        }

        var result = await grabService!.GrabAsync(best.Id, automatic: true, cancellationToken).ConfigureAwait(false);
        return result.Success;   // a failed grab (client down...) just leaves the candidates for the user
    }

    private List<ReleaseCandidate> StoreCandidates(PaperbunkrDbContext context, WantedIssue wanted, IReadOnlyList<ScoredRelease> results)
    {
        // Replace, not append: the latest search is the truth, and stale candidates would be offered for grabbing.
        var old = context.ReleaseCandidates.Where(c => c.WantedIssueId == wanted.Id).ToList();
        context.ReleaseCandidates.RemoveRange(old);

        var stored = new List<ReleaseCandidate>();
        foreach (var scored in results.Take(MaxCandidatesPerIssue))
        {
            var candidate = new ReleaseCandidate
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
            };
            context.ReleaseCandidates.Add(candidate);
            stored.Add(candidate);
        }

        wanted.LastSearchedAt = _now();
        context.SaveChanges();
        return stored;
    }
}
