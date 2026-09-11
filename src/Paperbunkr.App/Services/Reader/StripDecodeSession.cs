using System;
using Avalonia;
using SkiaSharp;

namespace Paperbunkr.App.Services.Reader;

/// <summary>
/// A stateful, forward-only Skia scanline-decode walk through one webtoon-strip page (docs/
/// superpowers/specs/2026-09-09-reader-webtoon-strip-band-decode-design.md §4.1, rev 5). Exists
/// because <c>SKCodec.GetPixels(..., SKCodecOptions{Subset})</c> - the API an earlier revision of
/// this design assumed would give independent per-band decode - is rejected outright by Skia's own
/// JPEG and PNG codecs (<c>onGetPixels</c> returns <c>kUnimplemented</c> for any subset request,
/// verified by reading Skia's actual <c>SkJpegCodec.cpp</c>/<c>SkPngCodec.cpp</c> source). Real
/// row-range extraction only exists, for formats that support it at all, via
/// <see cref="SKCodec.StartScanlineDecode(SKImageInfo)"/> + <see cref="SKCodec.SkipScanlines"/> +
/// <see cref="SKCodec.GetScanlines"/>, which can only move forward through the image, never seek
/// backward - hence this class holds an open session rather than treating each band as an
/// independent, stateless decode call.
///
/// <b>Not every format supports this</b> (rev 5, found by actually running it, not just reading
/// source): PNG's codec implements neither a working scanline path nor a working incremental-
/// decode-with-subset path in this SkiaSharp build - <see cref="TryCreate"/> probes
/// <see cref="SKCodec.StartScanlineDecode(SKImageInfo)"/> up front and returns
/// <see langword="null"/> if it doesn't succeed, so the caller (<see cref="ReaderImagePipeline"/>)
/// can fall back to the ordinary whole-page decode path for that strip rather than assume banding
/// always works.
///
/// Deliberately has no lock of its own - the <em>caller</em> supplies one via
/// <paramref name="withLock"/> on every call to <see cref="ReadBand"/> (see that method's own doc
/// comment for why this should be a lock scoped to this one session, not the pipeline's global
/// container-read lock), chunked (design rev 4) so a large reverse-scroll-restart skip never holds
/// it for its full duration, only one <see cref="ReaderImagePipeline.BandHeight"/>-sized chunk at a
/// time. This class is otherwise a plain, independently-testable decode primitive: pass a no-op
/// passthrough for the lock in a test and every other behavior (forward-only contract, correct
/// pixels, chunk count) is verifiable without the pipeline at all.
/// </summary>
internal sealed class StripDecodeSession : IDisposable
{
    private readonly SKCodec _codec;
    private readonly SKImageInfo _fullImageInfo;
    private bool _faulted;

    private StripDecodeSession(SKCodec codec, SKImageInfo fullImageInfo)
    {
        _codec = codec;
        _fullImageInfo = fullImageInfo;
        PixelSize = new PixelSize(fullImageInfo.Width, fullImageInfo.Height);
    }

    /// <summary>
    /// Opens a scanline-decode session on <paramref name="bytes"/>, or returns
    /// <see langword="null"/> if this format/image doesn't actually support scanline decode (PNG,
    /// per rev 5's finding - <c>SKCodecResult.Unimplemented</c>) or the bytes aren't a decodable
    /// image at all. Uses the codec's own reported native <c>SKColorType</c>/<c>SKAlphaType</c>
    /// rather than forcing a fixed one - an earlier draft hardcoded <c>Rgba8888</c>/<c>Opaque</c>
    /// and PNG's <c>StartScanlineDecode</c> failed with <c>InvalidConversion</c> as a result; the
    /// codec's own native format always succeeds (no conversion requested at all).
    /// </summary>
    public static StripDecodeSession? TryCreate(byte[] bytes, Action<Action> withLock)
    {
        SKCodec? codec = null;
        try
        {
            codec = SKCodec.Create(new SKMemoryStream(bytes));
            if (codec is null)
            {
                return null;
            }

            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0)
            {
                codec.Dispose();
                return null;
            }

            var fullImageInfo = new SKImageInfo(info.Width, info.Height, info.ColorType, info.AlphaType);

            bool started = false;
            withLock(() => started = codec.StartScanlineDecode(fullImageInfo) == SKCodecResult.Success);

            if (!started)
            {
                codec.Dispose();
                return null;
            }

            return new StripDecodeSession(codec, fullImageInfo);
        }
        catch
        {
            codec?.Dispose();
            return null;
        }
    }

    /// <summary>The strip's true pixel size, read from the header at construction.</summary>
    public PixelSize PixelSize { get; }

    /// <summary>The next source row this session hasn't yet skipped past or read. 0 before any <see cref="ReadBand"/> call.</summary>
    public int NextUnreadRow { get; private set; }

    /// <summary>
    /// Reads <paramref name="bandHeightRows"/> rows starting at <paramref name="bandStart"/> -
    /// <b>forward-only</b>: <paramref name="bandStart"/> must be &gt;= <see cref="NextUnreadRow"/>.
    /// A caller that needs an earlier row than this session has already passed must dispose this
    /// session and open a fresh one (design §4.1's "backward or far-forward request" path) - this
    /// method does not attempt to detect or recover from that itself, by design, so the forward-only
    /// contract stays simple and testable.
    /// </summary>
    /// <param name="withLock">
    /// Invoked once per chunk (at most <see cref="ReaderImagePipeline.BandHeight"/> rows of
    /// skip-or-read work each) - the caller's lock-acquire-run-release wrapper.
    /// <b>Should be a lock scoped to this specific session</b> (design rev 5) - these calls touch
    /// only already-in-memory bytes on this session's own private <see cref="SKCodec"/>, never the
    /// container, so reusing a pipeline-wide container-read lock here would only add unrelated
    /// contention. A test can pass <c>action =&gt; action()</c> to run with no real locking at all.
    /// </param>
    /// <returns>The decoded band as an <see cref="SKBitmap"/> (caller disposes), or <see langword="null"/> if the strip's total height is exhausted before <paramref name="bandHeightRows"/> rows could be read, or any codec call failed/faulted (this session is then unusable and should be disposed).</returns>
    public SKBitmap? ReadBand(int bandStart, int bandHeightRows, Action<Action> withLock)
    {
        if (bandStart < NextUnreadRow)
        {
            throw new InvalidOperationException(
                $"StripDecodeSession is forward-only: requested band start {bandStart} is behind the already-passed row {NextUnreadRow}. Open a fresh session instead.");
        }
        if (_faulted)
        {
            return null;
        }
        if (bandHeightRows <= 0 || bandStart >= PixelSize.Height)
        {
            return null;
        }

        // Clip to the strip's real remaining height - the last band is usually shorter than a full
        // BandHeight.
        int rowsAvailable = PixelSize.Height - bandStart;
        int rowsToRead = Math.Min(bandHeightRows, rowsAvailable);
        if (rowsToRead <= 0)
        {
            return null;
        }

        if (!SkipTo(bandStart, withLock))
        {
            return null;
        }

        var bandInfo = new SKImageInfo(PixelSize.Width, rowsToRead, _fullImageInfo.ColorType, _fullImageInfo.AlphaType);
        var bandBitmap = new SKBitmap(bandInfo);
        IntPtr basePixels = bandBitmap.GetPixels();
        int rowBytes = bandBitmap.RowBytes;

        int rowsRead = 0;
        while (rowsRead < rowsToRead)
        {
            int chunk = Math.Min(rowsToRead - rowsRead, ReaderImagePipeline.BandHeight);
            int rowsReadThisChunk = rowsRead;
            bool chunkOk = true;

            withLock(() =>
            {
                IntPtr dst = IntPtr.Add(basePixels, rowsReadThisChunk * rowBytes);
                int got = _codec.GetScanlines(dst, chunk, rowBytes);
                if (got != chunk)
                {
                    chunkOk = false;
                    return;
                }
                NextUnreadRow += chunk;
            });

            if (!chunkOk)
            {
                _faulted = true;
                bandBitmap.Dispose();
                return null;
            }

            rowsRead += chunk;
        }

        return bandBitmap;
    }

    private bool SkipTo(int targetRow, Action<Action> withLock)
    {
        int remaining = targetRow - NextUnreadRow;
        while (remaining > 0)
        {
            int chunk = Math.Min(remaining, ReaderImagePipeline.BandHeight);
            bool chunkOk = true;

            withLock(() =>
            {
                if (!_codec.SkipScanlines(chunk))
                {
                    chunkOk = false;
                    return;
                }
                NextUnreadRow += chunk;
            });

            if (!chunkOk)
            {
                _faulted = true;
                return false;
            }

            remaining -= chunk;
        }
        return true;
    }

    public void Dispose() => _codec.Dispose();
}
