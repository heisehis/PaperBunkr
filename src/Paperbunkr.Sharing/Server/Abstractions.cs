using Paperbunkr.Sharing.Protocol;

namespace Paperbunkr.Sharing.Server;

/// <summary>
/// Host-side read model the server serves the catalog from. Implemented over the EF context in
/// <c>Paperbunkr.Data</c>; kept here as an interface so this project never depends on Avalonia or
/// EF and a headless host is a new entry point rather than a rewrite.
/// </summary>
public interface IShareCatalogSource
{
    /// <summary>Changes whenever anything a client could see changes; becomes the catalog <c>ETag</c>.</summary>
    Task<string> GetCatalogVersionAsync(CancellationToken cancellationToken);

    /// <summary>One page of the in-scope catalog. <paramref name="cursor"/> is null for the first page.</summary>
    Task<CatalogPage> GetCatalogPageAsync(string? cursor, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<SharedListDto>> GetListsAsync(CancellationToken cancellationToken);

    /// <summary>True only if the issue is inside the host's sharing scope. The server calls this before every page/cover request.</summary>
    Task<bool> IsIssueSharedAsync(int issueId, CancellationToken cancellationToken);
}

/// <summary>A page or cover body. The server disposes <see cref="Content"/> after sending it.</summary>
public sealed record PageContent(Stream Content, string ContentType);

/// <summary>Supplies page and cover bytes for issues that <see cref="IShareCatalogSource"/> already confirmed are shared.</summary>
public interface ISharePageSource
{
    /// <summary>Null when the issue's archive can't be opened.</summary>
    Task<PagesResponse?> GetPagesAsync(int issueId, CancellationToken cancellationToken);

    /// <summary>Original page bytes when <paramref name="maxWidth"/> is null, otherwise a JPEG downscaled to at most that width. Null when the page does not exist.</summary>
    Task<PageContent?> GetPageAsync(int issueId, int pageIndex, int? maxWidth, CancellationToken cancellationToken);

    Task<PageContent?> GetCoverAsync(int issueId, int? maxWidth, CancellationToken cancellationToken);
}
