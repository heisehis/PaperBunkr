namespace Paperbunkr.Daemon.Indexers;

/// <summary>What to look for. <see cref="SeriesName"/> is the exact name; <see cref="QueryBuilder"/> decides the variants.</summary>
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
    /// <summary>Indexer-side stable id; used to de-duplicate candidates.</summary>
    public string? Guid { get; init; }
}

public sealed record ConnectionTestResult(bool Success, string Message)
{
    public static ConnectionTestResult Ok(string message) => new(true, message);
    public static ConnectionTestResult Fail(string message) => new(false, message);
}

/// <summary>Raised when an indexer can't be reached or rejects the request (bad key, HTTP error, malformed response).</summary>
public sealed class IndexerException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Plain transport to a release source: takes literal query text, returns raw hits. It deliberately knows
/// nothing about comics - variants, the strict-then-sanitized cascade, filtering and scoring live in
/// <see cref="QueryBuilder"/>, <see cref="ReleaseEvaluator"/> and <see cref="ReleaseSearcher"/>, so a second
/// implementation (direct Torznab/Newznab) can slot in later without touching any of that.
/// Slice 1 has one implementation: <see cref="ProwlarrSearchClient"/>.
/// </summary>
public interface IIndexerClient
{
    Task<IReadOnlyList<IndexerRelease>> SearchAsync(string queryText, CancellationToken cancellationToken);

    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken);
}
