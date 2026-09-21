namespace Paperbunkr.Data.Entities;

/// <summary>
/// Another Paperbunkr instance whose shared library this one mirrors (docs/superpowers/specs/
/// 2026-09-19-remote-library-sharing-design.md §7.1). Its issues and series are stored as ordinary
/// <see cref="Issue"/>/<see cref="Series"/> rows tagged with <see cref="Issue.RemoteSourceId"/> and a
/// <c>null</c> <see cref="Issue.FilePath"/>, so every existing view works on them and the
/// <c>FilePath != null</c> filters keep local-only jobs away. Deleting a source cascades to its mirror.
/// </summary>
public class RemoteSource
{
    public int Id { get; set; }

    /// <summary>The host's stable instance id (<c>GET /v1/hello</c>). The mirror is keyed by this, never by address.</summary>
    public string InstanceId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 7614;

    /// <summary>Uppercase hex SHA-256 of the certificate the user trusted (trust-on-first-use pin). A mismatch blocks the connection until the user explicitly re-trusts.</summary>
    public string CertFingerprint { get; set; } = string.Empty;

    /// <summary>The password, DPAPI-protected for the current user and base64-encoded - never plaintext. Needed to re-authenticate when a session expires. Null when the user chose not to save it.</summary>
    public string? ProtectedPassword { get; set; }

    /// <summary>Catalog <c>ETag</c> from the last successful sync; sent as <c>If-None-Match</c> so an unchanged catalog costs one request.</summary>
    public string? LastCatalogEtag { get; set; }

    public DateTime? LastSyncedAt { get; set; }

    /// <summary>True while the host can't be reached; mirror rows stay visible with an Offline badge.</summary>
    public bool IsOffline { get; set; }

    /// <summary>Set when a later <c>/v1/hello</c> reports a different instance id at this address - the source is flagged "Host changed" and needs Relink / Add-as-new / Remove (spec §7.1). Sync is blocked meanwhile.</summary>
    public bool HostChanged { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
