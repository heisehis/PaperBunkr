using Paperbunkr.Daemon.Indexers;

namespace Paperbunkr.Daemon.Clients;

public enum DownloadState
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Error,
    /// <summary>The client reports the torrent's files as missing on disk.</summary>
    Missing,
}

/// <summary>A torrent as the client reports it. <see cref="Progress"/> is 0..1.</summary>
public sealed record DownloadStatus(
    string Hash,
    string Name,
    double Progress,
    DownloadState State,
    long SizeBytes,
    long BytesPerSecond,
    TimeSpan? Eta,
    string? SavePath,
    string? ContentPath);

/// <summary>One file inside a torrent, path relative to the torrent's save path.</summary>
public sealed record DownloadFile(string Path, long SizeBytes);

/// <summary>The client couldn't be reached, rejected the credentials, or refused a request.</summary>
public sealed class DownloadClientException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The download client (qBittorrent today). Every operation is **locked to the Paperbunkr category**: status queries only ever return torrents
/// in it, and removal refuses anything outside it, so the user's other torrents can never be read into the pipeline or deleted
/// (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §5).
/// </summary>
public interface IDownloadClient
{
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken);

    /// <summary>Adds a magnet link or .torrent URL to the Paperbunkr category and returns its lower-case info-hash.</summary>
    Task<string> AddAsync(string downloadUrl, CancellationToken cancellationToken);

    /// <summary>Status of the Paperbunkr-category torrents with the given hashes (all of the category's when <paramref name="hashes"/> is null). Unknown hashes are simply absent.</summary>
    Task<IReadOnlyList<DownloadStatus>> GetStatusAsync(IReadOnlyCollection<string>? hashes, CancellationToken cancellationToken);

    Task<IReadOnlyList<DownloadFile>> GetFilesAsync(string hash, CancellationToken cancellationToken);

    /// <summary>Removes a torrent (optionally with its files). Returns false, doing nothing, when the torrent isn't in the Paperbunkr category.</summary>
    Task<bool> RemoveAsync(string hash, bool deleteFiles, CancellationToken cancellationToken);
}
