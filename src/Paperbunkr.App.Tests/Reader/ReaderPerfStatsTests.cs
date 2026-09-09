using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests.Reader;

/// <summary>Phase 4 (§10): the always-on perf counters the `ReaderFrameStats` overlay will read.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderPerfStatsTests : IDisposable
{
    private readonly string _cbz = Path.Combine(Path.GetTempPath(), $"pb_perf_{Guid.NewGuid():N}.cbz");
    public void Dispose() { try { if (File.Exists(_cbz)) File.Delete(_cbz); } catch (IOException) { } }

    [Fact]
    public void Snapshot_ReflectsFrameTimesAndCacheActivity()
    {
        var s = ReaderPerfStats.Current;
        s.Reset();
        s.RecordComposeFrameMs(0.4);
        s.RecordComposeFrameMs(0.6);
        s.RecordComposeFrameMs(0.5);
        s.RecordCacheHit();
        s.RecordCacheHit();
        s.RecordCacheMiss();

        var snap = s.Snapshot();
        Assert.Equal(3, snap.FramesSampled);
        Assert.InRange(snap.FrameP50Ms, 0.4, 0.6);
        Assert.Equal(2.0 / 3.0, snap.CacheHitRatio, 3);
    }

    [Fact]
    public void Pipeline_FeedsSessionAndDecodeCounters()
    {
        CbzFixture.Create(_cbz, pageCount: 4);
        ReaderPerfStats.Current.Reset();

        using (var pipeline = ReaderImagePipeline.TryOpen(_cbz)!)
        {
            pipeline.GetPage(0); // cold -> synchronous decode + session read
            pipeline.GetPage(0); // warm -> cache hit
        }

        var snap = ReaderPerfStats.Current.Snapshot();
        Assert.True(snap.SynchronousDecodes >= 1);
        Assert.True(snap.SessionReads >= 1, "expected the held-open session read path");
        Assert.True(snap.CacheHits >= 1);
    }
}
