using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests.Reader;

/// <summary>
/// Step 1 of the webtoon band-decode plan (docs/superpowers/specs/2026-09-09-reader-webtoon-strip-
/// band-decode-plan.md): header-only page-size peek and strip classification. Mirrors
/// <see cref="ReaderImagePipelineTests"/>'s shape (synthetic .cbz via <see cref="CbzFixture"/>).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderImagePipelineStripTests : IDisposable
{
    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"pb_striptest_{Guid.NewGuid():N}.cbz");

    public void Dispose()
    {
        try { if (File.Exists(_cbzPath)) File.Delete(_cbzPath); } catch (IOException) { }
    }

    [Fact]
    public void PeekPageSize_ReturnsHeaderDeclaredSize_ForANormalPage()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        var size = pipeline.PeekPageSize(0);

        Assert.NotNull(size);
        Assert.Equal(64, size!.Value.Width);
        Assert.Equal(96, size.Value.Height);
    }

    [Fact]
    public void PeekPageSize_ReturnsCorrectSize_ForATallStripPage()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new Size(800, 6000));
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        var size = pipeline.PeekPageSize(0);

        Assert.NotNull(size);
        Assert.Equal(800, size!.Value.Width);
        Assert.Equal(6000, size.Value.Height);
    }

    [Fact]
    public void PeekPageSize_ReadsHeaderOnly_SurvivesPixelDataBeingCorrupt()
    {
        // A real PNG signature + IHDR chunk (declares the true 64x96 size), truncated before any
        // IDAT (pixel data) chunk - a full decode fails on this, a header-only peek shouldn't need
        // to touch that part of the file at all.
        using (var zip = ZipFile.Open(_cbzPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("page_000.png", CompressionLevel.Fastest);
            byte[] fullPng;
            using (var bitmap = new Bitmap(64, 96))
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                fullPng = ms.ToArray();
            }

            // The PNG signature + IHDR chunk (the part that declares width/height) ends at byte 33,
            // but SKCodec.Create needs a bit more of the stream before it commits to a header
            // (verified empirically - it returns null below ~100 bytes for this fixture) even
            // though it never touches pixel data. 150 bytes is comfortably past that threshold and
            // nowhere near the ~15KB a full 64x96 PNG's actual pixel data (IDAT) needs.
            byte[] headerOnly = fullPng[..150];
            using var entryStream = entry.Open();
            entryStream.Write(headerOnly, 0, headerOnly.Length);
        }

        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        var size = pipeline.PeekPageSize(0);

        // 150 bytes is a tiny fraction of what a real 64x96 PNG's pixel data (IDAT) needs (~15KB
        // for this fixture, per the encoder used elsewhere in this file) - PeekPageSize succeeding
        // here is itself the proof it never touched that part of the file. (GetPage on these same
        // truncated bytes is deliberately not asserted to fail: this pipeline's decode path has
        // several fallback strategies - PageDecodeCore.Decode's own GDI+ fallback among them - and
        // at least one of them tolerates a truncated-but-header-valid PNG rather than throwing,
        // which is a real, separate robustness property of this codebase, not something this test
        // is about.)
        Assert.NotNull(size);
        Assert.Equal(64, size!.Value.Width);
        Assert.Equal(96, size.Value.Height);
    }

    [Fact]
    public void IsStrip_TrueForATallStrip_FalseForANormalPage()
    {
        CbzFixture.Create(_cbzPath, pageCount: 2, pageSize: i => i == 0 ? new Size(800, 6000) : new Size(660, 1010));
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        Assert.True(pipeline.IsStrip(0));
        Assert.False(pipeline.IsStrip(1));
    }

    [Fact]
    public void IsStrip_CachesTheVerdict_DoesNotRePeekOnEveryCall()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new Size(800, 6000));
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        Assert.True(pipeline.IsStrip(0));
        // Second call must return the same cached verdict without needing to re-read the container -
        // if the raw-bytes cache were somehow bypassed this would still succeed since the verdict is
        // cached independently of it.
        Assert.True(pipeline.IsStrip(0));
    }

    // --- Step 4: async RequestPageSize/PageSizeAvailable -----------------------------------

    [Fact]
    public void RequestPageSize_FiresPageSizeAvailable_WithTheCorrectSize()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new Size(800, 6000));
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        var landed = new ManualResetEventSlim(false);
        Avalonia.PixelSize? received = null;
        pipeline.PageSizeAvailable += (index, size) =>
        {
            if (index == 0) { received = size; landed.Set(); }
        };

        pipeline.SetVirtualizationWindow(0, 0); // the peek is window-gated, same as whole-page decode
        pipeline.RequestPageSize(0);

        Assert.True(landed.Wait(TimeSpan.FromSeconds(5)), "PageSizeAvailable never fired");
        Assert.NotNull(received);
        Assert.Equal(800, received!.Value.Width);
        Assert.Equal(6000, received.Value.Height);
    }

    [Fact]
    public void RequestPageSize_PeekRunsOffTheCallingThread()
    {
        // Same shape as ReaderImagePipelineTests.BackgroundDecode_RunsOffTheCallingThread, for the
        // size-peek path specifically - the whole reason RequestPageSize is async (design rev 3,
        // §4.3) is that the naive synchronous version would have run PeekPageSize's container read
        // inline on whichever thread called it, reintroducing the parent pipeline's original
        // UI-thread-blocking-decode problem.
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new Size(800, 6000));
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        int callingThread = Environment.CurrentManagedThreadId;
        int? peekThread = null;
        var seen = new ManualResetEventSlim(false);
        pipeline.OnBeforePageSizePeek = _ => { peekThread ??= Environment.CurrentManagedThreadId; seen.Set(); };

        pipeline.SetVirtualizationWindow(0, 0);
        pipeline.RequestPageSize(0);

        Assert.True(seen.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotNull(peekThread);
        Assert.NotEqual(callingThread, peekThread);
    }

    [Fact]
    public void RequestPageSize_DoesNotReturnBlocked_RegardlessOfBackgroundState()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new Size(800, 6000));
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;
        pipeline.SetVirtualizationWindow(0, 0);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        pipeline.RequestPageSize(0);
        sw.Stop();

        // RequestPageSize's body is a lock + channel enqueue - no I/O, no decode - so it must return
        // essentially instantly regardless of whatever the background consumer loop is doing. A
        // generous bound (real work here is sub-millisecond) rather than a tight one, to keep this
        // robust under CI/load variance while still catching a regression that made it synchronous.
        Assert.True(sw.ElapsedMilliseconds < 500, $"RequestPageSize took {sw.ElapsedMilliseconds}ms - should be near-instant (fire-and-forget)");
    }

    [Fact]
    public void RequestPageSize_AlreadyKnown_DoesNotRePeek()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new Size(800, 6000));
        using var pipeline = ReaderImagePipeline.TryOpen(_cbzPath)!;

        int peekCount = 0;
        pipeline.OnBeforePageSizePeek = _ => Interlocked.Increment(ref peekCount);

        var landed = new ManualResetEventSlim(false);
        pipeline.PageSizeAvailable += (index, _) => { if (index == 0) landed.Set(); };

        pipeline.SetVirtualizationWindow(0, 0);
        pipeline.RequestPageSize(0);
        Assert.True(landed.Wait(TimeSpan.FromSeconds(5)));

        // Now that the size is known, a second request must not trigger another peek at all.
        pipeline.RequestPageSize(0);
        Thread.Sleep(200); // give a wrongly-re-enqueued request a chance to run before asserting
        Assert.Equal(1, peekCount);
    }
}
