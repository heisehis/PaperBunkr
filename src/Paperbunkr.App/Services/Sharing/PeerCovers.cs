using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Sharing.Client;

namespace Paperbunkr.App.Services.Sharing;

/// <summary>
/// Where covers of remote (mirrored) issues live: <c>{data}/remote-library/covers/{localIssueId}.jpg</c>
/// (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md §7.4). Deliberately not
/// <see cref="CoverThumbnailPaths"/> - that directory is swept for orphans by the local cover maintenance,
/// which would treat a remote cover as one. Keyed by the <em>local</em> issue id (unique per mirror row),
/// resolved by <see cref="CoverImageCache"/> and <see cref="CoverThumbnailService.GetEffectiveCoverPath"/>
/// like any other cover.
/// </summary>
public static class PeerCoverPaths
{
    /// <summary>Mutable so tests can redirect it; never set outside a test's own constructor/teardown.</summary>
    public static string Directory { get; set; } = AppDataPaths.Combine("remote-library", "covers");

    public static string GetCachePath(int localIssueId) =>
        Path.Combine(Directory, localIssueId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".jpg");

    public static bool Exists(int localIssueId) => File.Exists(GetCachePath(localIssueId));

    /// <summary>Atomic write (temp + rename) so a decoder never reads half a cover.</summary>
    public static void Save(int localIssueId, byte[] bytes)
    {
        string path = GetCachePath(localIssueId);
        System.IO.Directory.CreateDirectory(Directory);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    public static void Delete(IEnumerable<int> localIssueIds)
    {
        foreach (int id in localIssueIds)
        {
            try
            {
                File.Delete(GetCachePath(id));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>
/// Downloads the covers a remote library's tiles need (docs/superpowers/specs/2026-09-19-remote-library-
/// sharing-design.md §7.4): only the missing ones, small (a thumbnail width), a few at a time, and quietly -
/// a failure just leaves the placeholder until the next sync. A stored cover invalidates the in-memory
/// image cache for that issue so the tile picks it up.
/// </summary>
public sealed class PeerCoverFetcher
{
    public const int ThumbnailWidth = 400;

    private readonly Func<bool, PaperbunkrDbContext> _contextFactory;
    private readonly Func<int, ShareClient> _clientFor;
    private readonly Action<int> _onCoverStored;
    private readonly int _maxConcurrent;

    public PeerCoverFetcher(Func<bool, PaperbunkrDbContext> contextFactory, Func<int, ShareClient> clientFor, Action<int>? onCoverStored = null, int maxConcurrent = 3)
    {
        _contextFactory = contextFactory;
        _clientFor = clientFor;
        _onCoverStored = onCoverStored ?? CoverImageCache.Invalidate;
        _maxConcurrent = Math.Max(1, maxConcurrent);
    }

    /// <summary>Fetches every missing cover of <paramref name="sourceId"/>; returns how many were stored.</summary>
    public async Task<int> FetchMissingAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        List<(int LocalId, int RemoteId)> missing;
        using (PaperbunkrDbContext context = _contextFactory(true))
        {
            missing = context.Issues.AsNoTracking()
                .Where(i => i.RemoteSourceId == sourceId && i.RemoteIssueId != null)
                .Select(i => new { i.Id, Remote = i.RemoteIssueId!.Value })
                .AsEnumerable()
                .Where(x => !PeerCoverPaths.Exists(x.Id))
                .Select(x => (x.Id, x.Remote))
                .ToList();
        }

        if (missing.Count == 0)
        {
            return 0;
        }

        ShareClient client;
        try
        {
            client = _clientFor(sourceId);
        }
        catch (ShareClientException)
        {
            return 0;
        }

        int stored = 0;
        using var slots = new SemaphoreSlim(_maxConcurrent);
        await Task.WhenAll(missing.Select(async item =>
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                byte[]? bytes = await client.GetCoverBytesAsync(item.RemoteId, ThumbnailWidth, cancellationToken).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                {
                    PeerCoverPaths.Save(item.LocalId, bytes);
                    _onCoverStored(item.LocalId);
                    Interlocked.Increment(ref stored);
                }
            }
            catch (ShareClientException)
            {
                // Host went away or refused: leave the placeholder; the next sync tries again.
            }
            finally
            {
                slots.Release();
            }
        })).ConfigureAwait(false);

        return stored;
    }
}

/// <summary>
/// Opens a remote issue for the reader (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md
/// §7.3). The result is the ordinary <see cref="Reader.ReaderImagePipeline"/> over a network-backed provider,
/// so everything the reader does for a local file - async decode, byte budget, prefetch, strips - just works.
/// </summary>
public sealed class RemoteReaderSource
{
    private readonly RemotePageFetcher _fetcher;

    public RemoteReaderSource(RemotePageFetcher fetcher) => _fetcher = fetcher;

    public RemotePageFetcher Fetcher => _fetcher;

    /// <summary>
    /// Null when the issue can't be opened (not a mirror row, or no page count at all). The page count comes
    /// from the mirror row: <c>LoadIssue</c> runs on the UI thread and must never wait on the network.
    /// </summary>
    public Reader.ReaderImagePipeline? TryOpen(Paperbunkr.Data.Entities.Issue issue, int? memoryLimitMb)
    {
        if (issue.RemoteSourceId is not int sourceId || issue.RemoteIssueId is not int remoteId || issue.PageCount is not > 0)
        {
            return null;
        }

        int count = issue.PageCount.Value;
        var provider = new RemoteImageProvider(sourceId, remoteId, count, page => _fetcher.GetPage(sourceId, remoteId, page, CancellationToken.None));
        provider.Open(provider.SourceKey, async: false);
        var session = new RemoteAccessorSession(count, (page, ct) => _fetcher.GetPage(sourceId, remoteId, page, ct));
        return Reader.ReaderImagePipeline.OpenProvider(provider, session, memoryLimitMb);
    }
}
