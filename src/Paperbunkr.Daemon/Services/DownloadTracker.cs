using Microsoft.EntityFrameworkCore;
using Paperbunkr.Daemon.Clients;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Import;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Services;

/// <summary>
/// Follows the downloads Paperbunkr started (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §5-6). Each tick asks the
/// client - **restricted to the Paperbunkr category and to the hashes Paperbunkr recorded** - how each grab is doing, keeps
/// <see cref="WantedIssue.DownloadProgress"/> current, and hands a finished download to the <see cref="IImportProcessor"/>. A torrent that
/// vanished from the client, or errored, fails the issue and blocklists the release so the next search can't pick it again.
/// </summary>
public sealed class DownloadTracker(
    Func<PaperbunkrDbContext> createContext,
    Func<PaperbunkrDbContext, IDownloadClient?> createClient,
    IImportProcessor? importer,
    IEventPublisher events)
{
    public const string ClientAlert = "acquisition-downloadclient";
    public const string ImportAlert = "acquisition-import";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _lastActive = -1;

    /// <summary>True right after the last running download settled but the host hasn't been told yet (so the timer ticks once more).</summary>
    public bool HasPendingReport => _lastActive > 0;

    /// <summary>True when any grab is in flight - the timer only runs the tracker when there is something to follow.</summary>
    public bool HasActiveDownloads()
    {
        using var context = createContext();
        return context.WantedIssues.Any(w => w.Status == WantedIssueStatus.Snatched || w.Status == WantedIssueStatus.Downloading);
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;   // a previous tick (possibly mid-import) is still running
        }

        try
        {
            await TickCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task TickCoreAsync(CancellationToken cancellationToken)
    {
        using var context = createContext();
        var active = context.WantedIssues
            .Include(w => w.WatchedSeries)
            .Where(w => (w.Status == WantedIssueStatus.Snatched || w.Status == WantedIssueStatus.Downloading) && w.TorrentHash != null)
            .ToList();

        if (active.Count == 0)
        {
            ReportActive(0, 0, null);
            return;
        }

        var client = createClient(context);
        if (client is null)
        {
            return;   // qBittorrent was un-configured while downloads were in flight: leave them alone rather than failing them
        }

        IReadOnlyList<DownloadStatus> statuses;
        try
        {
            statuses = await client.GetStatusAsync(active.Select(w => w.TorrentHash!).ToList(), cancellationToken).ConfigureAwait(false);
        }
        catch (DownloadClientException ex)
        {
            // Not the downloads' fault: keep them as they are and tell the user once.
            events.Publish(new DaemonAlertEvent(ClientAlert, DaemonAlertSeverity.Warning, "qBittorrent couldn't be reached", ex.Message));
            return;
        }

        events.Publish(new DaemonAlertClearedEvent(ClientAlert));
        var byHash = statuses.ToDictionary(s => s.Hash, StringComparer.OrdinalIgnoreCase);

        int running = 0;
        double progressSum = 0;
        string? detail = null;

        foreach (var wanted in active)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // An earlier import in this tick (a pack) may already have settled this row: re-read it, and skip it if so.
            context.Entry(wanted).Reload();
            if (wanted.Status is not (WantedIssueStatus.Snatched or WantedIssueStatus.Downloading))
            {
                continue;
            }

            var label = $"{wanted.WatchedSeries?.Name} #{wanted.IssueNumber}";

            if (!byHash.TryGetValue(wanted.TorrentHash!, out var status))
            {
                Fail(context, wanted, label, "The torrent was removed from qBittorrent before it finished.", BlocklistReason.DownloadFailed);
                continue;
            }

            switch (status.State)
            {
                case DownloadState.Error:
                case DownloadState.Missing:
                    Fail(context, wanted, label, status.State == DownloadState.Missing ? "qBittorrent lost the downloaded files." : "qBittorrent reported an error for this download.", BlocklistReason.DownloadFailed);
                    break;

                case DownloadState.Completed:
                    await ImportAsync(context, client, wanted, label, status, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    wanted.Status = WantedIssueStatus.Downloading;
                    wanted.DownloadProgress = status.Progress;
                    context.SaveChanges();
                    running++;
                    progressSum += status.Progress;
                    detail = label;
                    events.Publish(new DownloadProgressEvent(wanted.Id, label, status.Progress, status.BytesPerSecond, status.Eta));
                    break;
            }
        }

        ReportActive(running, running == 0 ? 0 : progressSum / running, detail);
    }

    private async Task ImportAsync(PaperbunkrDbContext context, IDownloadClient client, WantedIssue wanted, string label, DownloadStatus status, CancellationToken cancellationToken)
    {
        wanted.DownloadProgress = 1;

        if (importer is null)
        {
            // Nothing can import yet: show it as finished downloading and try again on the next tick.
            wanted.Status = WantedIssueStatus.Downloading;
            context.SaveChanges();
            return;
        }

        ImportOutcome outcome;
        try
        {
            var files = await client.GetFilesAsync(status.Hash, cancellationToken).ConfigureAwait(false);
            outcome = await importer.ImportAsync(wanted.Id, status, files, cancellationToken).ConfigureAwait(false);
        }
        catch (DownloadClientException ex)
        {
            events.Publish(new DaemonAlertEvent(ClientAlert, DaemonAlertSeverity.Warning, "qBittorrent couldn't be reached", ex.Message));
            return;   // try again next tick; the download itself is fine
        }

        if (outcome.IsDeferred)
        {
            // The user's to fix (no destination folder, a bad template, a full disk): keep the finished download and retry on a later tick.
            events.Publish(new DaemonAlertEvent(ImportAlert, DaemonAlertSeverity.Warning, "A finished download can't be imported yet", outcome.Failure));
            return;
        }

        events.Publish(new DaemonAlertClearedEvent(ImportAlert));

        if (!outcome.Success)
        {
            Fail(context, wanted, label, outcome.Failure ?? "Nothing usable was found in the download.", outcome.FailureReason ?? BlocklistReason.Unreadable);
            return;
        }

        if (outcome.Unmatched.Count > 0)
        {
            events.Publish(new DaemonAlertEvent($"{ImportAlert}-unmatched-{wanted.Id}", DaemonAlertSeverity.Info,
                $"{outcome.Unmatched.Count} file(s) in \"{status.Name}\" matched nothing you wanted", string.Join(", ", outcome.Unmatched.Take(3))));
        }

        if (outcome.RemoveTorrent)
        {
            try { await client.RemoveAsync(status.Hash, deleteFiles: true, cancellationToken).ConfigureAwait(false); }
            catch (DownloadClientException) { /* the comics are safely in the library; a leftover torrent is harmless */ }
        }

        // The importer records each satisfied issue itself (a pack can satisfy several), through its own context.
        context.Entry(wanted).Reload();
    }

    private void Fail(PaperbunkrDbContext context, WantedIssue wanted, string label, string reason, BlocklistReason blocklistReason)
    {
        BlocklistService.Add(context, wanted.GrabbedTitle ?? wanted.WatchedSeries?.Name ?? label, wanted.TorrentHash, blocklistReason, reason);
        wanted.Status = WantedIssueStatus.Failed;
        wanted.FailureReason = reason;
        wanted.DownloadProgress = null;
        context.SaveChanges();
        events.Publish(new IssueFailedEvent(wanted.Id, label, reason));
    }

    /// <summary>Publishes an aggregate update only when it would change what the host shows.</summary>
    private void ReportActive(int active, double averageProgress, string? detail)
    {
        if (active == 0 && _lastActive <= 0)
        {
            _lastActive = 0;
            return;
        }

        _lastActive = active;
        events.Publish(new DownloadsChangedEvent(active, averageProgress, detail));
    }
}
