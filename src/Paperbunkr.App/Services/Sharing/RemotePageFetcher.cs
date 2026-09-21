using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using cYo.Projects.ComicRack.Engine.IO.Provider.Readers;
using Paperbunkr.Sharing.Client;

namespace Paperbunkr.App.Services.Sharing;

/// <summary>
/// Fetches page bytes of a remote issue for the reader (docs/superpowers/specs/2026-09-19-remote-library-
/// sharing-design.md §7.3): cache first, then the network, with hard bounds so a fast page flip or an
/// aggressive prefetch can't flood the host or the disk - at most <see cref="MaxConcurrent"/> requests in
/// flight, identical concurrent requests share one fetch, and a cancelled request stores nothing and hands
/// nothing to the decoder.
/// </summary>
public sealed class RemotePageFetcher : IDisposable
{
    private readonly Func<int, ShareClient> _clientFor;
    private readonly PeerPageCache _cache;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentDictionary<(int Source, int Issue, int Page), Lazy<Task<byte[]?>>> _inflight = new();

    public RemotePageFetcher(Func<int, ShareClient> clientFor, PeerPageCache cache, int maxConcurrent = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrent, 1);
        _clientFor = clientFor;
        _cache = cache;
        MaxConcurrent = maxConcurrent;
        _slots = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    public int MaxConcurrent { get; }

    /// <summary>Requests currently holding a network slot - observable for the bounded-concurrency tests.</summary>
    public int InFlight => MaxConcurrent - _slots.CurrentCount;

    /// <summary>Test hook: observes the highest number of concurrent network fetches.</summary>
    public int PeakInFlight { get; private set; }

    /// <summary>
    /// The page's bytes, from disk if cached, else fetched. Returns null only when the host says the page
    /// no longer exists. Throws <see cref="OperationCanceledException"/> when cancelled (nothing cached,
    /// nothing returned) and a <see cref="ShareClientException"/> when the host can't be reached.
    /// </summary>
    public byte[]? GetPage(int sourceId, int remoteIssueId, int pageIndex, CancellationToken cancellationToken)
    {
        if (_cache.TryGet(sourceId, remoteIssueId, pageIndex, out byte[]? cached))
        {
            return cached;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var key = (sourceId, remoteIssueId, pageIndex);
        // Shared by every caller asking for this exact page at once. The fetch itself is deliberately NOT tied to
        // any one caller's token: another caller may still want the result. Each caller still leaves as soon as
        // its own token fires (the Wait below), and a fetch nobody waits for finishes into the cache harmlessly.
        Lazy<Task<byte[]?>> shared = _inflight.GetOrAdd(key, k => new Lazy<Task<byte[]?>>(() => FetchAsync(k.Source, k.Issue, k.Page)));
        try
        {
            Task<byte[]?> task = shared.Value;
            task.Wait(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return task.Result;
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        finally
        {
            if (shared.IsValueCreated && shared.Value.IsCompleted)
            {
                _inflight.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Page count of a remote issue from the host (a 404 - the issue is no longer shared - is 0).</summary>
    public async Task<int> GetPageCountAsync(int sourceId, int remoteIssueId, CancellationToken cancellationToken)
    {
        var pages = await _clientFor(sourceId).GetPagesAsync(remoteIssueId, cancellationToken).ConfigureAwait(false);
        return pages.PageCount;
    }

    public void Dispose() => _slots.Dispose();

    private async Task<byte[]?> FetchAsync(int sourceId, int remoteIssueId, int pageIndex)
    {
        await _slots.WaitAsync().ConfigureAwait(false);
        try
        {
            PeakInFlight = Math.Max(PeakInFlight, InFlight);

            // Another request may have cached it while this one waited for a slot.
            if (_cache.TryGet(sourceId, remoteIssueId, pageIndex, out byte[]? cached))
            {
                return cached;
            }

            byte[]? bytes = await _clientFor(sourceId).GetPageBytesAsync(remoteIssueId, pageIndex).ConfigureAwait(false);
            if (bytes is { Length: > 0 })
            {
                _cache.Put(sourceId, remoteIssueId, pageIndex, bytes);
            }

            return bytes;
        }
        finally
        {
            _slots.Release();
        }
    }
}

/// <summary>
/// A remote issue presented to the reader as an <see cref="ImageProvider"/> (docs/superpowers/specs/
/// 2026-09-19-remote-library-sharing-design.md §7.3): pages are just indexes, and their bytes come from a
/// <see cref="RemotePageFetcher"/>. Feeding <c>ReaderImagePipeline</c> through this keeps the whole decode,
/// cache, prefetch and strip-band machinery unchanged - remote reading is a new byte source, not a new reader.
/// </summary>
internal sealed class RemoteImageProvider : ImageProvider
{
    private readonly int _pageCount;
    private readonly Func<int, byte[]?> _readPage;
    private readonly string _hash;

    public RemoteImageProvider(int sourceId, int remoteIssueId, int pageCount, Func<int, byte[]?> readPage)
    {
        _pageCount = pageCount;
        _readPage = readPage;
        _hash = $"remote:{sourceId}:{remoteIssueId}:{pageCount}";
        SourceKey = $"remote://{sourceId}/{remoteIssueId}";
    }

    /// <summary>The synthetic "path" used as this provider's <c>Source</c>; there is no file behind it.</summary>
    public string SourceKey { get; }

    public override bool IsSlow => true;

    public override string CreateHash() => _hash;

    // The base class verifies the source is a real file before parsing and swallows the failure, which would
    // leave this provider with zero pages. There is no file behind a remote issue.
    protected override void OnCheckSource()
    {
    }

    protected override void OnParse()
    {
        for (int i = 0; i < _pageCount; i++)
        {
            FireIndexReady(new ProviderImageInfo(i, i.ToString(), 0));
        }
    }

    protected override byte[] OnRetrieveSourceByteImage(int index) =>
        _readPage(index) ?? throw new InvalidOperationException($"Page {index} is no longer available from the host.");
}

/// <summary>
/// Lock-free, concurrent page reads for the reader pipeline (the same shape as its PDF session: entries are
/// addressed by page index as a string). Disposing cancels any read still waiting on the network.
/// </summary>
internal sealed class RemoteAccessorSession : IComicAccessorSession
{
    private readonly Func<int, CancellationToken, byte[]?> _read;
    private readonly CancellationTokenSource _cts = new();

    public RemoteAccessorSession(int count, Func<int, CancellationToken, byte[]?> read)
    {
        Count = count;
        _read = read;
    }

    public int Count { get; }

    public byte[] ReadEntryBytes(string entryName)
    {
        if (!int.TryParse(entryName, out int page))
        {
            throw new ArgumentException("Remote entries are addressed by page index.", nameof(entryName));
        }

        return _read(page, _cts.Token) ?? throw new InvalidOperationException($"Page {page} is no longer available from the host.");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
