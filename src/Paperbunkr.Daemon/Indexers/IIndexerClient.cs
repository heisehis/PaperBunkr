namespace Paperbunkr.Daemon.Indexers;

/// <summary>What to look for. <see cref="SeriesName"/> is the exact name; the client (via <c>QueryBuilder</c>) decides variants.</summary>
public sealed record IndexerQuery(string SeriesName, string IssueNumber, int? Year = null, int? Volume = null);

/// <summary>One search hit from an indexer, before filtering and scoring.</summary>
public sealed record IndexerRelease
{
    public required string Title { get; init; }
    /// <summary>Magnet URI or a <c>.torrent</c> download URL - whatever the download client is handed.</summary>
    public required string DownloadUrl { get; init; }
    public long SizeBytes { get; init; }
    public int Seeders { get; init; }
    public int Peers { get; init; }
    public string? Indexer { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    /// <summary>Indexer-side stable id (Torznab guid); used to de-duplicate candidates.</summary>
    public string? Guid { get; init; }
}

public sealed record ConnectionTestResult(bool Success, string Message)
{
    public static ConnectionTestResult Ok(string message) => new(true, message);
    public static ConnectionTestResult Fail(string message) => new(false, message);
}

/// <summary>Raised when an indexer can't be reached or rejects the request (bad key, HTTP error, malformed XML).</summary>
public sealed class IndexerException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Searches release sources. Slice 1 has one implementation (Prowlarr's Torznab endpoint); direct
/// Torznab/Newznab clients can slot in later without touching the loop.
/// </summary>
public interface IIndexerClient
{
    Task<IReadOnlyList<IndexerRelease>> SearchAsync(IndexerQuery query, CancellationToken cancellationToken);

    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken);
}
