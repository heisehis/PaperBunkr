using Paperbunkr.Daemon.Clients;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Import;

/// <summary>What happened to a finished download.</summary>
/// <param name="ImportedWantedIssueIds">Every wanted issue this download satisfied (more than one for a pack).</param>
/// <param name="Failure">Why nothing usable came out (or, for a deferred outcome, what the user needs to fix).</param>
/// <param name="FailureReason">How to blocklist the release when it failed for a reason that would repeat.</param>
public sealed record ImportOutcome(IReadOnlyList<int> ImportedWantedIssueIds, string? Failure = null, BlocklistReason? FailureReason = null)
{
    public bool Success => ImportedWantedIssueIds.Count > 0;

    /// <summary>
    /// The import couldn't run for a reason that isn't the release's fault - no destination folder chosen, an invalid naming template. The
    /// download stays as it is and is retried later; nothing is failed or blocklisted.
    /// </summary>
    public bool IsDeferred { get; init; }

    /// <summary>Files in a multi-file download that matched nothing the user wanted (left alone, reported).</summary>
    public IReadOnlyList<string> Unmatched { get; init; } = Array.Empty<string>();

    /// <summary>Remove the torrent (and its files) from the client now that everything was moved out (the "move original" option).</summary>
    public bool RemoveTorrent { get; init; }

    public static ImportOutcome Failed(string failure, BlocklistReason reason) => new(Array.Empty<int>(), failure, reason);

    public static ImportOutcome Deferred(string message) => new(Array.Empty<int>(), message) { IsDeferred = true };
}

/// <summary>
/// Turns a completed download into library files (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §6). The download's own
/// files are never modified (they keep seeding): everything happens on a copy.
/// </summary>
public interface IImportProcessor
{
    /// <param name="wantedIssueId">The wanted issue whose grab this download is.</param>
    /// <param name="download">The finished torrent as the client reports it.</param>
    /// <param name="files">The torrent's files, relative to <see cref="DownloadStatus.SavePath"/>.</param>
    Task<ImportOutcome> ImportAsync(int wantedIssueId, DownloadStatus download, IReadOnlyList<DownloadFile> files, CancellationToken cancellationToken);
}

/// <summary>
/// How the host puts a finished file into the library. The daemon has no scanner of its own (it is UI-free); the app supplies one that runs its
/// real library scan on the file.
/// </summary>
public interface ILibraryIngester
{
    /// <summary>Adds the file to the library and returns the new issue's id, or <c>null</c> when the library didn't add a new issue (already present).</summary>
    Task<int?> IngestAsync(string filePath, CancellationToken cancellationToken);
}
