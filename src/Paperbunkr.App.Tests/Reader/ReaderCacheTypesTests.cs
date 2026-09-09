using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using cYo.Common.Collections;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests.Reader;

/// <summary>
/// Step 4 of the reader-pipeline plan: the typed payloads for <see cref="Cache{K,T}"/> and that
/// its byte bound (<see cref="cYo.Common.ComponentModel.IDataSize"/>) evicts + disposes past
/// <c>SizeCapacity</c>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderCacheTypesTests
{
    private static WriteableBitmap MakeBitmap(int w, int h) =>
        new(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    [Fact]
    public void ReaderBitmap_DataSize_IsFourBytesPerPixel()
    {
        using var rb = new ReaderBitmap(MakeBitmap(100, 50));
        Assert.Equal(100 * 50 * 4, rb.DataSize);
    }

    [Fact]
    public void RawPageBytes_DataSize_IsByteLength()
    {
        Assert.Equal(1234, new RawPageBytes(new byte[1234]).DataSize);
        Assert.Equal(1, new RawPageBytes(System.Array.Empty<byte>()).DataSize);
    }

    [Fact]
    public void Cache_EvictsAndDisposes_WhenByteBudgetExceeded()
    {
        // 3 bitmaps of 40 KiB each; budget only fits 2.
        const int perBitmap = 100 * 100 * 4; // 40,000
        using var cache = new Cache<PageId, ReaderBitmap>(itemCapacity: 100, sizeCapacity: perBitmap * 2 + 1000);
        cache.MinimalTimeInCache = 0; // evict immediately for the test
        var disposed = new List<int>();
        cache.ItemRemoved += (_, e) => { e.Item.Dispose(); disposed.Add(e.Key.Index); };

        for (int i = 0; i < 3; i++)
        {
            var id = new PageId("c", 0, i, PageTier.Display);
            using (cache.LockItem(id, _ => new ReaderBitmap(MakeBitmap(100, 100)))) { }
        }

        Assert.True(cache.Size <= perBitmap * 2 + 1000, $"cache size {cache.Size} over budget");
        Assert.Contains(0, disposed); // the oldest went first
    }

    [Fact]
    public void Cache_SingleFlight_OneCreatePerKey()
    {
        using var cache = new Cache<PageId, RawPageBytes>(itemCapacity: 10);
        int creates = 0;
        var id = new PageId("c", 0, 0, PageTier.Display);

        using (cache.LockItem(id, _ => { creates++; return new RawPageBytes(new byte[10]); })) { }
        using (cache.LockItem(id, _ => { creates++; return new RawPageBytes(new byte[10]); })) { }

        Assert.Equal(1, creates);
    }
}
