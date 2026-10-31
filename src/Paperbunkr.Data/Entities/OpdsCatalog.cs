namespace Paperbunkr.Data.Entities;

/// <summary>
/// An OPDS catalog the user added (docs/superpowers/specs/2026-10-27-books-slice4c-opds-design.md §3). The password is DPAPI-protected at rest
/// (<see cref="Credentials.DpapiSecrets"/>) and only sent to the catalog's own host.
/// </summary>
public class OpdsCatalog
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Username { get; set; }

    public string? ProtectedPassword { get; set; }

    public DateTime AddedTime { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// One book downloaded from a catalog: lets the browser show "In library" and keeps the catalog's descriptive values until they are applied to the
/// imported book (docs/superpowers/specs/2026-10-27-books-slice4c-opds-design.md §5).
/// </summary>
public class OpdsDownload
{
    public int Id { get; set; }

    public int CatalogId { get; set; }

    /// <summary>The Atom entry id.</summary>
    public string EntryId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Author { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public DateTime DownloadedUtc { get; set; }

    public int? BookId { get; set; }
}
