using Paperbunkr.Daemon.Clients;
using Paperbunkr.Daemon.Indexers;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Services;

/// <summary>An in-memory copy of the blocklist for one search pass, so checking hundreds of results costs no queries.</summary>
public sealed class BlocklistSnapshot
{
    private readonly HashSet<string> _names;
    private readonly HashSet<string> _hashes;

    public BlocklistSnapshot(IEnumerable<string> names, IEnumerable<string> hashes)
    {
        _names = new HashSet<string>(names.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        _hashes = new HashSet<string>(hashes, StringComparer.OrdinalIgnoreCase);
    }

    public static BlocklistSnapshot Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>());

    /// <summary>Blocked when the title matches a blocklisted release name, or the release's magnet carries a blocklisted info-hash.</summary>
    public bool IsBlocked(IndexerRelease release) => IsBlocked(release.Title, release.DownloadUrl);

    public bool IsBlocked(string title, string? downloadUrl)
    {
        if (_names.Contains(title.Trim()))
        {
            return true;
        }

        var hash = downloadUrl is null ? null : TorrentHash.FromMagnet(downloadUrl);
        return hash is not null && _hashes.Contains(hash);
    }
}

/// <summary>
/// The release blocklist (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §4/§5): a release that failed, was corrupt, or was
/// rejected by the user is never fetched again. Matched by info-hash when one is known, otherwise by release name.
/// </summary>
public static class BlocklistService
{
    public static BlocklistSnapshot Load(PaperbunkrDbContext context)
    {
        var rows = context.ReleaseBlocklist.Select(b => new { b.ReleaseName, b.TorrentHash }).ToList();
        return new BlocklistSnapshot(
            rows.Select(r => r.ReleaseName),
            rows.Where(r => !string.IsNullOrEmpty(r.TorrentHash)).Select(r => r.TorrentHash!));
    }

    /// <summary>Adds a release; a repeat of the same name/hash is left as one row.</summary>
    public static void Add(PaperbunkrDbContext context, string releaseName, string? torrentHash, BlocklistReason reason, string? detail = null)
    {
        var name = releaseName.Trim();
        var hash = string.IsNullOrWhiteSpace(torrentHash) ? null : torrentHash.Trim().ToLowerInvariant();

        // SQLite's = is case-sensitive, but a release name differing only in case is the same release.
        var lowerName = name.ToLowerInvariant();
        bool exists = context.ReleaseBlocklist.Any(b =>
            (hash != null && b.TorrentHash == hash) || (hash == null && b.ReleaseName.ToLower() == lowerName && b.TorrentHash == null));
        if (exists)
        {
            return;
        }

        context.ReleaseBlocklist.Add(new ReleaseBlocklist
        {
            ReleaseName = name,
            TorrentHash = hash,
            Reason = reason,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
        context.SaveChanges();
    }

    public static bool IsBlocked(PaperbunkrDbContext context, string title, string? downloadUrl) => Load(context).IsBlocked(title, downloadUrl);
}
