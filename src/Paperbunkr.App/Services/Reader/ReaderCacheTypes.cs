using System;
using Avalonia.Media.Imaging;
using cYo.Common.ComponentModel;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace Paperbunkr.App.Services.Reader;

/// <summary>Which resolution tier a cached bitmap is (docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md §6).</summary>
public enum PageTier
{
    /// <summary>Downsampled to the viewport/fit size - the 95% case, held in the byte-bounded cache.</summary>
    Display,
    /// <summary>Small thumbnail-rail image, in the separate thumbnail sub-cache.</summary>
    Thumbnail
}

/// <summary>
/// Identity for one cached page image. <see cref="ContainerStamp"/> is the archive/PDF file's
/// last-write-ticks-plus-length, so a file replaced on disk mid-session (rare, but Library Health
/// tracks it) never serves a stale decode.
/// </summary>
public readonly record struct PageId(string Container, long ContainerStamp, int Index, PageTier Tier);

/// <summary>
/// A decoded reader page, sized so <see cref="Cache{K,T}"/>'s byte bound (<see cref="IDataSize"/>)
/// accounts for it. Owns its <see cref="AvaloniaBitmap"/> - disposed when the cache evicts it
/// (wired via <c>Cache.ItemRemoved</c>).
/// </summary>
public sealed class ReaderBitmap : IDataSize, IDisposable
{
    public ReaderBitmap(AvaloniaBitmap bitmap)
    {
        Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        var size = bitmap.PixelSize;
        // 4 bytes/px (Bgra8888/Premul, Avalonia's decode target). Clamp to >=1 so a degenerate
        // 0-px decode still counts as *something* against the budget rather than being free.
        DataSize = Math.Max(1, checked(size.Width * size.Height * 4));
    }

    public AvaloniaBitmap Bitmap { get; }

    public int DataSize { get; }

    public void Dispose() => Bitmap.Dispose();
}

/// <summary>Compressed (undecoded) bytes of one archive/PDF page, held in the raw-bytes tier so a re-decode (detail tier, cache miss after eviction) costs no container I/O. §4/§6.2.</summary>
public sealed class RawPageBytes : IDataSize
{
    public RawPageBytes(byte[] bytes)
    {
        Bytes = bytes ?? Array.Empty<byte>();
        DataSize = Math.Max(1, Bytes.Length);
    }

    public byte[] Bytes { get; }

    public int DataSize { get; }
}

/// <summary>
/// A borrowed reference to a cached <see cref="ReaderBitmap"/>. While held, the cache entry cannot
/// be evicted (it wraps <see cref="Cache{K,T}"/>'s ref-counted <see cref="IItemLock{T}"/>).
/// <see cref="PageCanvas"/> / the reader view-models hold one only for pages they are actively
/// drawing; dispose releases the pin.
/// </summary>
public sealed class ReaderPageHandle : IDisposable
{
    private readonly IItemLock<ReaderBitmap> _lock;

    internal ReaderPageHandle(IItemLock<ReaderBitmap> itemLock)
    {
        _lock = itemLock;
    }

    public AvaloniaBitmap Bitmap => _lock.Item.Bitmap;

    public void Dispose() => _lock.Dispose();
}
