using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Sharing;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Sharing.Client;
using Paperbunkr.Sharing.Protocol;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.App.Tests;

/// <summary>A page source that is slow on purpose and counts what the server is asked for.</summary>
internal sealed class SlowPages : ISharePageSource
{
    private int _concurrent;

    public int Requests;
    public int PeakConcurrent;
    public int DelayMs = 150;
    public int PageCount = 6;
    public byte[][] Png = Enumerable.Range(0, 6).Select(i => MakePng(800, 1200, i)).ToArray();

    public static byte[] MakePng(int w, int h, int seed)
    {
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.FromArgb(40 + (seed % 6) * 30, 90, 160));
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public Task<PagesResponse?> GetPagesAsync(int issueId, CancellationToken ct) =>
        Task.FromResult<PagesResponse?>(new PagesResponse(PageCount, Enumerable.Range(0, PageCount).Select(i => new PageInfoDto(i, 800, 1200, null)).ToList()));

    public async Task<PageContent?> GetPageAsync(int issueId, int pageIndex, int? maxWidth, CancellationToken ct)
    {
        Interlocked.Increment(ref Requests);
        int now = Interlocked.Increment(ref _concurrent);
        int seen;
        while (now > (seen = Volatile.Read(ref PeakConcurrent)) && Interlocked.CompareExchange(ref PeakConcurrent, now, seen) != seen) { }
        try
        {
            await Task.Delay(DelayMs, ct);
            return pageIndex >= PageCount ? null : new PageContent(new MemoryStream(Png[pageIndex % Png.Length]), "image/png");
        }
        finally
        {
            Interlocked.Decrement(ref _concurrent);
        }
    }

    public Task<PageContent?> GetCoverAsync(int issueId, int? maxWidth, CancellationToken ct) =>
        Task.FromResult<PageContent?>(new PageContent(new MemoryStream(MakePng(400, 600, issueId)), "image/jpeg"));
}

internal sealed class AllSharedCatalog : IShareCatalogSource
{
    public Task<string> GetCatalogVersionAsync(CancellationToken ct) => Task.FromResult("v1");
    public Task<CatalogPage> GetCatalogPageAsync(string? cursor, int pageSize, CancellationToken ct) =>
        Task.FromResult(new CatalogPage("v1", Array.Empty<CatalogSeriesDto>(), Array.Empty<CatalogIssueDto>(), null));
    public Task<IReadOnlyList<SharedListDto>> GetListsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SharedListDto>>(Array.Empty<SharedListDto>());
    public Task<bool> IsIssueSharedAsync(int issueId, CancellationToken ct) => Task.FromResult(true);
}

/// <summary><see cref="PeerPageCache"/> (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md §7.3): bounded by quota and by idle time.</summary>
public sealed class PeerPageCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_pagecache_{Guid.NewGuid():N}");

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    private static byte[] Bytes(int n, byte fill = 1) => Enumerable.Repeat(fill, n).ToArray();

    [Fact]
    public void PutThenGet_RoundTripsTheExactBytes_AndTracksTotal()
    {
        var cache = new PeerPageCache(_dir);

        cache.Put(1, 10, 0, Bytes(100, 7));

        Assert.True(cache.Contains(1, 10, 0));
        Assert.True(cache.TryGet(1, 10, 0, out var got));
        Assert.Equal(Bytes(100, 7), got);
        Assert.Equal(100, cache.TotalBytes);
        Assert.False(cache.TryGet(1, 10, 1, out _));
    }

    [Fact]
    public void Overwriting_AdjustsTheTotalRatherThanDoubleCounting()
    {
        var cache = new PeerPageCache(_dir);
        cache.Put(1, 10, 0, Bytes(100));

        cache.Put(1, 10, 0, Bytes(40));

        Assert.Equal(40, cache.TotalBytes);
    }

    [Fact]
    public void ExceedingTheQuota_EvictsTheLeastRecentlyUsed_AndKeepsWhatWasJustTouched()
    {
        var time = new FakeTimeProvider();
        var cache = new PeerPageCache(_dir, quotaBytes: 300, time: time);
        cache.Put(1, 10, 0, Bytes(100)); time.Advance(TimeSpan.FromMinutes(1));
        cache.Put(1, 10, 1, Bytes(100)); time.Advance(TimeSpan.FromMinutes(1));
        cache.Put(1, 10, 2, Bytes(100)); time.Advance(TimeSpan.FromMinutes(1));
        cache.TryGet(1, 10, 0, out _);                       // page 0 is used again: now the freshest of the three
        time.Advance(TimeSpan.FromMinutes(1));

        cache.Put(1, 10, 3, Bytes(100));                     // 400 > 300: the oldest-used goes

        Assert.False(cache.Contains(1, 10, 1));              // least recently used
        Assert.True(cache.Contains(1, 10, 0));               // saved by the touch
        Assert.True(cache.Contains(1, 10, 2));
        Assert.True(cache.Contains(1, 10, 3));
        Assert.Equal(300, cache.TotalBytes);
    }

    [Fact]
    public void APageLargerThanTheWholeQuota_IsStillCached_ButNothingElseSurvivesIt()
    {
        var cache = new PeerPageCache(_dir, quotaBytes: 100);
        cache.Put(1, 10, 0, Bytes(60));

        cache.Put(1, 10, 1, Bytes(500));

        Assert.True(cache.Contains(1, 10, 1));               // the newest write is never evicted by its own pass
        Assert.False(cache.Contains(1, 10, 0));
    }

    [Fact]
    public void SweepExpired_RemovesIdlePages_KeepsRecentOnes_AndPrunesEmptyFolders()
    {
        var time = new FakeTimeProvider();
        var cache = new PeerPageCache(_dir, timeToLive: TimeSpan.FromDays(30), time: time);
        cache.Put(1, 10, 0, Bytes(10));
        cache.Put(2, 20, 0, Bytes(10));
        time.Advance(TimeSpan.FromDays(20));
        cache.Put(2, 20, 1, Bytes(10));                      // fresh
        cache.TryGet(1, 10, 0, out _);                       // source 1's page is used again, resetting its clock
        time.Advance(TimeSpan.FromDays(15));                 // 35 days since the first writes, 15 since the fresher ones

        int removed = cache.SweepExpired();

        Assert.Equal(1, removed);                            // only (2,20,0) is >30 days idle
        Assert.False(cache.Contains(2, 20, 0));
        Assert.True(cache.Contains(2, 20, 1));
        Assert.True(cache.Contains(1, 10, 0));
        Assert.Equal(20, cache.TotalBytes);
    }

    [Fact]
    public void SweepExpired_PrunesFoldersThatBecameEmpty()
    {
        var time = new FakeTimeProvider();
        var cache = new PeerPageCache(_dir, timeToLive: TimeSpan.FromDays(1), time: time);
        cache.Put(3, 30, 0, Bytes(10));
        time.Advance(TimeSpan.FromDays(2));

        cache.SweepExpired();

        Assert.False(Directory.Exists(Path.Combine(_dir, "3")));
    }

    [Fact]
    public void PurgeSource_RemovesOnlyThatLibrary()
    {
        var cache = new PeerPageCache(_dir);
        cache.Put(1, 10, 0, Bytes(10));
        cache.Put(2, 20, 0, Bytes(10));

        cache.PurgeSource(1);

        Assert.False(cache.Contains(1, 10, 0));
        Assert.True(cache.Contains(2, 20, 0));
        Assert.Equal(10, cache.TotalBytes);
    }

    [Fact]
    public void PurgeIssue_RemovesOnlyThatIssue()
    {
        var cache = new PeerPageCache(_dir);
        cache.Put(1, 10, 0, Bytes(10));
        cache.Put(1, 11, 0, Bytes(10));

        cache.PurgeIssue(1, 10);

        Assert.False(cache.Contains(1, 10, 0));
        Assert.True(cache.Contains(1, 11, 0));
        Assert.Equal(10, cache.TotalBytes);
    }

    [Fact]
    public void NoTemporaryFilesAreLeftBehind()
    {
        var cache = new PeerPageCache(_dir);
        for (int i = 0; i < 5; i++) cache.Put(1, 10, i, Bytes(50));

        Assert.Empty(Directory.EnumerateFiles(_dir, "*.tmp", SearchOption.AllDirectories));
    }
}

/// <summary>
/// Reading a remote issue (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md §7.3): a real
/// server with a slow page source, the real fetcher/cache, and the real reader pipeline on top.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class RemoteReadingTests : IAsyncLifetime
{
    private const string Password = "hunter2-hunter2";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_remoteread_{Guid.NewGuid():N}");
    private readonly SlowPages _pages = new();
    private ShareServer _server = null!;
    private ShareClient _client = null!;
    private PeerPageCache _cache = null!;
    private RemotePageFetcher _fetcher = null!;
    private string _fingerprint = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var certs = new CertificateManager(Path.Combine(_root, "cert"));
        _fingerprint = certs.Fingerprint;
        _server = new ShareServer(new ShareServerOptions { Port = 0, PasswordHash = PasswordHasher.Hash(Password, 1_000) }, new AllSharedCatalog(), _pages, certs.GetOrCreate());
        await _server.StartAsync();
        _port = _server.Port;
        _client = new ShareClient("127.0.0.1", _port, _fingerprint, Password);
        _cache = new PeerPageCache(Path.Combine(_root, "pages"));
        _fetcher = new RemotePageFetcher(_ => _client, _cache);
    }

    public async Task DisposeAsync()
    {
        _fetcher.Dispose();
        _client.Dispose();
        await _server.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ASecondReadOfAPage_ComesFromTheCache_NotTheNetwork()
    {
        byte[]? first = _fetcher.GetPage(1, 100, 0, CancellationToken.None);
        byte[]? second = _fetcher.GetPage(1, 100, 0, CancellationToken.None);

        Assert.Equal(_pages.Png[0], first);
        Assert.Equal(first, second);
        Assert.Equal(1, _pages.Requests);
        Assert.True(_cache.Contains(1, 100, 0));
    }

    [Fact]
    public async Task ManyCallersAskingForTheSamePage_ShareOneFetch()
    {
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() => _fetcher.GetPage(1, 100, 2, CancellationToken.None))).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(_pages.Png[2], r));
        Assert.Equal(1, _pages.Requests);
    }

    [Fact]
    public async Task AFloodOfPageRequests_NeverExceedsTheConcurrencyBound()
    {
        var tasks = Enumerable.Range(0, 6).Select(i => Task.Run(() => _fetcher.GetPage(1, 100 + i, 0, CancellationToken.None))).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(6, _pages.Requests);
        Assert.True(_pages.PeakConcurrent <= 3, $"the host saw {_pages.PeakConcurrent} concurrent requests");
        Assert.True(_fetcher.PeakInFlight <= 3);
    }

    [Fact]
    public async Task ACancelledRequest_ThrowsPromptly_ReturnsNothing_AndDoesNotWedgeTheFetcher()
    {
        _pages.DelayMs = 800;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => _fetcher.GetPage(1, 100, 1, cts.Token));
        Assert.True(watch.ElapsedMilliseconds < 600, $"took {watch.ElapsedMilliseconds}ms - the caller waited for the network instead of leaving");

        // The fetcher still works afterwards (its slot was released once the abandoned fetch finished).
        _pages.DelayMs = 10;
        byte[]? page = await Task.Run(() => _fetcher.GetPage(1, 100, 3, CancellationToken.None));
        Assert.Equal(_pages.Png[3], page);
    }

    [Fact]
    public void AnAlreadyCancelledToken_NeverTouchesTheNetwork()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => _fetcher.GetPage(1, 100, 0, cts.Token));

        Assert.Equal(0, _pages.Requests);
    }

    [Fact]
    public void APageTheHostNoLongerHas_IsNull_AndNotCached()
    {
        byte[]? gone = _fetcher.GetPage(1, 100, 99, CancellationToken.None);

        Assert.Null(gone);
        Assert.False(_cache.Contains(1, 100, 99));
    }

    [Fact]
    public async Task PageCountComesFromTheHost()
    {
        Assert.Equal(6, await _fetcher.GetPageCountAsync(1, 100, CancellationToken.None));
    }

    [Fact]
    public void WhenTheHostGoesAway_CachedPagesStillRead_AndUncachedOnesFailCleanly()
    {
        _fetcher.GetPage(1, 100, 0, CancellationToken.None);          // page 0 read while online
        _server.StopAsync().GetAwaiter().GetResult();

        Assert.Equal(_pages.Png[0], _fetcher.GetPage(1, 100, 0, CancellationToken.None));                                       // offline, from disk
        Assert.ThrowsAny<ShareClientException>(() => _fetcher.GetPage(1, 100, 1, CancellationToken.None));                      // never fetched
    }

    // ---- through the real reader pipeline ----

    private Issue MirrorIssue(int pageCount = 6) => new() { Id = 5, RemoteSourceId = 1, RemoteIssueId = 100, PageCount = pageCount };

    [Fact]
    public void TheReaderPipeline_DecodesRealPages_FromTheRemoteHost()
    {
        var source = new RemoteReaderSource(_fetcher);

        using var pipeline = source.TryOpen(MirrorIssue(), memoryLimitMb: 64)!;

        Assert.NotNull(pipeline);
        Assert.Equal(6, pipeline.PageCount);
        using var page = pipeline.GetPage(1);
        Assert.Equal(800, page.PixelSize.Width);
        Assert.Equal(1200, page.PixelSize.Height);
        Assert.True(_cache.Contains(1, 100, 1));                       // read-through populated the disk cache
    }

    [Fact]
    public void TheReaderPipeline_ReadsPreviouslyViewedPages_WhileTheHostIsOffline()
    {
        var source = new RemoteReaderSource(_fetcher);
        using (var online = source.TryOpen(MirrorIssue(), 64)!)
        {
            using var _ = online.GetPage(0);
        }
        _server.StopAsync().GetAwaiter().GetResult();

        using var offline = source.TryOpen(MirrorIssue(), 64)!;
        using var page = offline.GetPage(0);                            // from the disk cache

        Assert.Equal(800, page.PixelSize.Width);
        Assert.ThrowsAny<Exception>(() => offline.GetPage(4));          // never fetched, host gone: an error, not a hang or crash
    }

    [Fact]
    public void ReopeningARemoteBook_NeverServesAPreviousOpensPages_FromTheProcessWideRawCache()
    {
        // Regression: the reader pipeline keeps a process-wide cache of raw page bytes keyed by container + a file mtime/size stamp.
        // A remote book has no file, so its stamp was constant and one open's bytes were served to the next forever - stale content
        // after the host replaced the book or a Relink re-keyed the id. Each open must now fetch (or read the disk cache) afresh.
        var source = new RemoteReaderSource(_fetcher);
        using (var first = source.TryOpen(MirrorIssue(), 64)!)
        {
            using var _ = first.GetPage(0);
        }
        Assert.Equal(1, _pages.Requests);

        // Same book id, but the host's page changed; the client's disk cache is empty (e.g. purged after a Relink).
        _pages.Png[0] = SlowPages.MakePng(800, 1200, 5);
        _cache.PurgeSource(1);
        using var second = source.TryOpen(MirrorIssue(), 64)!;
        using var page = second.GetPage(0);

        Assert.Equal(2, _pages.Requests);                                  // fetched again - not served from the shared static cache
        Assert.Equal(_pages.Png[0], _cache.TryGet(1, 100, 0, out var bytes) ? bytes : null);   // and what is on disk is the NEW content
    }

    [Theory]
    [InlineData(null, null, 6)]      // a local issue: not this factory's business
    [InlineData(1, 100, null)]       // no page count known
    [InlineData(1, 100, 0)]
    public void TryOpen_RefusesWhatItCannotOpen(int? source, int? remote, int? pages)
    {
        var issue = new Issue { RemoteSourceId = source, RemoteIssueId = remote, PageCount = pages };

        Assert.Null(new RemoteReaderSource(_fetcher).TryOpen(issue, 64));
    }

    // ---- covers ----

    [Fact]
    public async Task CoverFetcher_DownloadsOnlyMissingCovers_IntoTheirOwnDirectory_AndTheCoverCacheResolvesThem()
    {
        string covers = Path.Combine(_root, "covers");
        string saved = PeerCoverPaths.Directory;
        PeerCoverPaths.Directory = covers;
        var stored = new List<int>();
        try
        {
            string dbPath = Path.Combine(_root, "c.db");
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={dbPath}").Options;
            int a, b;
            using (var context = new PaperbunkrDbContext(options) { IncludeRemote = true })
            {
                context.Database.EnsureCreated();
                var src = new RemoteSource { InstanceId = "x", DisplayName = "H", Host = "h", CertFingerprint = "F" };
                context.RemoteSources.Add(src);
                context.SaveChanges();
                var s = new Series { Name = "Saga", RemoteSourceId = src.Id, RemoteSeriesId = 1 };
                context.Series.Add(s);
                context.SaveChanges();
                var i1 = new Issue { SeriesId = s.Id, RemoteSourceId = src.Id, RemoteIssueId = 100 };
                var i2 = new Issue { SeriesId = s.Id, RemoteSourceId = src.Id, RemoteIssueId = 101 };
                context.Issues.AddRange(i1, i2);
                context.SaveChanges();
                (a, b) = (i1.Id, i2.Id);
                Assert.Equal(1, src.Id);
            }
            PeerCoverPaths.Save(a, new byte[] { 1, 2, 3 });               // one already cached
            var fetcher = new PeerCoverFetcher(inc => new PaperbunkrDbContext(options) { IncludeRemote = inc }, _ => _client, stored.Add);

            int fetched = await fetcher.FetchMissingAsync(1);

            Assert.Equal(1, fetched);                                     // only the missing one
            Assert.Equal(new[] { b }, stored);
            Assert.True(PeerCoverPaths.Exists(b));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(PeerCoverPaths.GetCachePath(a)));   // untouched
            Assert.Equal(PeerCoverPaths.GetCachePath(b), CoverImageCache.ResolveCoverFile(b.ToString()));
            Assert.Equal(PeerCoverPaths.GetCachePath(b), CoverThumbnailService.GetEffectiveCoverPath(b));
            Assert.Equal(0, await fetcher.FetchMissingAsync(1));          // nothing left to do
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        finally
        {
            PeerCoverPaths.Directory = saved;
        }
    }

    [Fact]
    public async Task CoverFetcher_WithTheHostDown_ReturnsZero_InsteadOfThrowing()
    {
        await _server.StopAsync();
        string saved = PeerCoverPaths.Directory;
        PeerCoverPaths.Directory = Path.Combine(_root, "covers2");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "d.db")}").Options;
            using (var context = new PaperbunkrDbContext(options) { IncludeRemote = true })
            {
                context.Database.EnsureCreated();
                var src = new RemoteSource { InstanceId = "x", DisplayName = "H", Host = "h", CertFingerprint = "F" };
                context.RemoteSources.Add(src);
                context.SaveChanges();
                var s = new Series { Name = "Saga", RemoteSourceId = src.Id, RemoteSeriesId = 1 };
                context.Series.Add(s);
                context.SaveChanges();
                context.Issues.Add(new Issue { SeriesId = s.Id, RemoteSourceId = src.Id, RemoteIssueId = 100 });
                context.SaveChanges();
            }

            var fetcher = new PeerCoverFetcher(inc => new PaperbunkrDbContext(options) { IncludeRemote = inc }, _ => _client);

            Assert.Equal(0, await fetcher.FetchMissingAsync(1));
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        finally
        {
            PeerCoverPaths.Directory = saved;
        }
    }
}
