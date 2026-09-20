using Microsoft.EntityFrameworkCore;
using Paperbunkr.Daemon.Clients;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Services;

public sealed record GrabResult(bool Success, string Message)
{
    public static GrabResult Ok(string message) => new(true, message);
    public static GrabResult Fail(string message) => new(false, message);
}

/// <summary>
/// The user-facing acquisition actions that talk to the download client (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §5):
/// approve a candidate, reject one, retry a failure, cancel a grab. Each opens its own <see cref="PaperbunkrDbContext"/> and never throws for an
/// expected problem - it returns a message the UI can show.
/// </summary>
public sealed class GrabService(
    Func<PaperbunkrDbContext> createContext,
    Func<PaperbunkrDbContext, IDownloadClient?> createClient,
    IEventPublisher events)
{
    /// <summary>Sends a candidate to the download client: <c>Wanted</c>/<c>Failed</c> -> <c>Snatched</c>, hash stored, the other candidates dropped.</summary>
    public async Task<GrabResult> GrabAsync(int candidateId, bool automatic, CancellationToken cancellationToken)
    {
        using var context = createContext();
        var candidate = context.ReleaseCandidates.Include(c => c.WantedIssue).ThenInclude(w => w!.WatchedSeries).FirstOrDefault(c => c.Id == candidateId);
        if (candidate?.WantedIssue is not { } wanted)
        {
            return GrabResult.Fail("That release is no longer available. Search again to refresh the list.");
        }

        var label = $"{wanted.WatchedSeries?.Name} #{wanted.IssueNumber}";
        if (wanted.Status is not (WantedIssueStatus.Wanted or WantedIssueStatus.Failed))
        {
            return GrabResult.Fail($"{label} is already {wanted.Status.ToString().ToLowerInvariant()}.");
        }

        if (BlocklistService.IsBlocked(context, candidate.Title, candidate.DownloadUrl))
        {
            return GrabResult.Fail("That release is on the blocklist (it failed or was rejected before).");
        }

        var client = createClient(context);
        if (client is null)
        {
            return GrabResult.Fail("Set up qBittorrent first: Preferences → Acquisition.");
        }

        string hash;
        try
        {
            hash = await client.AddAsync(candidate.DownloadUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (DownloadClientException ex)
        {
            return GrabResult.Fail(ex.Message);
        }

        wanted.Status = WantedIssueStatus.Snatched;
        wanted.TorrentHash = hash;
        wanted.GrabbedTitle = candidate.Title;
        wanted.DownloadProgress = 0;
        wanted.FailureReason = null;
        context.ReleaseCandidates.RemoveRange(context.ReleaseCandidates.Where(c => c.WantedIssueId == wanted.Id));
        context.SaveChanges();

        events.Publish(new IssueSnatchedEvent(wanted.Id, label, candidate.Title, automatic));
        return GrabResult.Ok($"Sent {label} to qBittorrent.");
    }

    /// <summary>"Reject": blocklists the release (by name) and drops it from the list, so it is never offered again.</summary>
    public void Reject(int candidateId)
    {
        using var context = createContext();
        var candidate = context.ReleaseCandidates.FirstOrDefault(c => c.Id == candidateId);
        if (candidate is null)
        {
            return;
        }

        BlocklistService.Add(context, candidate.Title, torrentHash: null, BlocklistReason.UserRejected);
        context.ReleaseCandidates.Remove(candidate);
        context.SaveChanges();
    }

    /// <summary>Puts a failed issue back to <c>Wanted</c> so the next search (or "Search now") looks for a different release.</summary>
    public void Retry(int wantedIssueId)
    {
        using var context = createContext();
        var wanted = context.WantedIssues.FirstOrDefault(w => w.Id == wantedIssueId && w.Status == WantedIssueStatus.Failed);
        if (wanted is null)
        {
            return;
        }

        wanted.Status = WantedIssueStatus.Wanted;
        wanted.FailureReason = null;
        wanted.TorrentHash = null;
        wanted.DownloadProgress = null;
        wanted.LastSearchedAt = null;   // due for an immediate search
        context.SaveChanges();
    }

    /// <summary>
    /// Stops a grab: removes the torrent (only ever one in the Paperbunkr category) and returns the issue to <c>Wanted</c>. Deleting the files
    /// is optional and only applies to a torrent that hasn't finished.
    /// </summary>
    public async Task<GrabResult> CancelAsync(int wantedIssueId, bool deleteFiles, CancellationToken cancellationToken)
    {
        using var context = createContext();
        var wanted = context.WantedIssues.FirstOrDefault(w => w.Id == wantedIssueId
            && (w.Status == WantedIssueStatus.Snatched || w.Status == WantedIssueStatus.Downloading));
        if (wanted is null)
        {
            return GrabResult.Fail("Nothing to cancel.");
        }

        if (wanted.TorrentHash is { } hash && createClient(context) is { } client)
        {
            try
            {
                await client.RemoveAsync(hash, deleteFiles, cancellationToken).ConfigureAwait(false);
            }
            catch (DownloadClientException ex)
            {
                return GrabResult.Fail($"Couldn't remove it from qBittorrent: {ex.Message}");
            }
        }

        wanted.Status = WantedIssueStatus.Wanted;
        wanted.TorrentHash = null;
        wanted.DownloadProgress = null;
        wanted.GrabbedTitle = null;
        wanted.LastSearchedAt = null;   // its candidates were dropped at grab time: due for a fresh search
        context.SaveChanges();
        return GrabResult.Ok("Cancelled.");
    }
}
