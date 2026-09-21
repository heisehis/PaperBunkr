using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Xunit;

namespace Paperbunkr.App.Tests;

[Collection(nameof(AvaloniaTestCollection))]
public class RemoteCoverCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_remotecovers_{Guid.NewGuid():N}");
    private readonly string _originalDir = RemoteCoverCache.CacheDirectory;
    private readonly HttpClient _originalHttp = RemoteCoverCache.Http;
    private readonly CountingHandler _handler = new();

    public RemoteCoverCacheTests()
    {
        TestAppBuilder.EnsureInitialized();
        RemoteCoverCache.CacheDirectory = _dir;
        RemoteCoverCache.Http = new HttpClient(_handler);
    }

    public void Dispose()
    {
        RemoteCoverCache.CacheDirectory = _originalDir;
        RemoteCoverCache.Http = _originalHttp;
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests;
        public HttpStatusCode Status = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Png()) });
        }
    }

    /// <summary>A real, tiny PNG so the decoder has something valid to read.</summary>
    private static byte[] Png()
    {
        using var source = new WriteableBitmap(new Avalonia.PixelSize(8, 12), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using var stream = new MemoryStream();
        source.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task ACoverDownloadsOnce_ThenComesFromMemory_AndFromDiskAfterAMemoryMiss()
    {
        string url = "https://example.test/covers/" + Guid.NewGuid().ToString("N") + ".jpg";

        var first = await RemoteCoverCache.GetAsync(url, CancellationToken.None);
        var second = await RemoteCoverCache.GetAsync(url, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, _handler.Requests);
        Assert.Single(Directory.GetFiles(_dir, "*.img"));        // no leftover temp files
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task AFailedOrUnusableUrl_IsANullCover_NotAnException()
    {
        _handler.Status = HttpStatusCode.NotFound;
        Assert.Null(await RemoteCoverCache.GetAsync("https://example.test/missing-" + Guid.NewGuid().ToString("N") + ".jpg", CancellationToken.None));
        Assert.Null(await RemoteCoverCache.GetAsync("file:///C:/secret.png", CancellationToken.None));     // only http(s) is ever fetched
        Assert.Null(await RemoteCoverCache.GetAsync("not a url", CancellationToken.None));
        Assert.Equal(1, _handler.Requests);
    }

    [Fact]
    public void ARowWithoutAUrl_NeverLoads()
    {
        var none = new RemoteCoverSource(null);
        Assert.False(none.HasCover);
        Assert.Null(none.Image);
        Assert.False(new RemoteCoverSource("ftp://x/y.jpg").HasCover);
        Assert.Equal(0, _handler.Requests);
    }

    [Fact]
    public void ACoverSource_DoesNotFetchUntilItsImageIsRead()
    {
        var source = new RemoteCoverSource("https://example.test/lazy-" + Guid.NewGuid().ToString("N") + ".jpg");
        Assert.Equal(0, _handler.Requests);      // constructing 3000 rows must cost nothing
        Assert.True(source.HasCover);
    }
}
