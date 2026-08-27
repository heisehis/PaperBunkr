namespace Paperbunkr.Data.ReadingLists.Sources;

/// <summary>
/// One external story-arc/reading-order source (docs/superpowers/specs/2026-08-22-cbl-manager-arc-
/// lookup-design.md §3) - ported from CBL Manager's own <c>IReadingListSource</c>
/// (<c>_reference/CBLManager/src/CBLManager/IReadingListSource.cs</c>), translated to this
/// codebase's async/<see cref="CancellationToken"/> convention. No exact-match custom-field tier -
/// see the design doc for why that tier doesn't carry over.
/// </summary>
public interface IReadingListSource
{
    /// <summary>Stored on <see cref="Entities.ReadingList.Source"/> so Refresh can look the source back up later.</summary>
    string SourceKey { get; }

    string DisplayName { get; }

    bool RequiresCredentials { get; }

    /// <summary>
    /// True for a source whose catalog is a small, real, fixed list (docs/superpowers/specs/
    /// 2026-08-22-cbl-manager-curated-browse-design.md) - <see cref="SearchAsync"/> with an empty
    /// query returns that whole list for these, so the UI can offer "browse everything this source
    /// has" instead of requiring the user to already know a title to search for. False for an
    /// open-ended API-search source (ComicVine, Metron) with no fixed catalog to enumerate.
    /// </summary>
    bool HasBrowsableCatalog { get; }

    Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>Returns the arc's issues already in the source's own curated reading order.</summary>
    Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken);

    /// <summary>Best-effort synopsis + cover art for the arc itself. Never required to succeed - callers treat a thrown exception or an all-null result the same as "no overview info available."</summary>
    Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken);
}

/// <summary>Thrown by any <see cref="IReadingListSource"/> adapter on a request/parse failure - caught at the call site and shown as a status message, never crashes the search/create/refresh flow.</summary>
public sealed class ReadingListSourceException : Exception
{
    public ReadingListSourceException(string sourceDisplayName, string message)
        : base(message)
    {
        SourceDisplayName = sourceDisplayName;
    }

    public string SourceDisplayName { get; }
}
