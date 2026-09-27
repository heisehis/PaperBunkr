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

    // ===== Continuous-scroll boundary metrics (docs/superpowers/specs/2026-09-25-comic-reader-performance-design.md B5) =====

    [Fact]
    public void BoundaryMetrics_ReportPercentiles_AndResetClearsThem()
    {
        var s = ReaderPerfStats.Current;
        s.Reset();
        for (int i = 1; i <= 100; i++)
        {
            s.RecordBoundaryHandlerMs(i);
            s.RecordDecodeLatencyMs(i * 2);
            s.RecordPositionSaveMs(i / 10.0);
        }

        var snap = s.Snapshot();
        Assert.Equal(100, snap.BoundaryEvents);
        Assert.InRange(snap.BoundaryP50Ms, 45, 56);
        Assert.InRange(snap.BoundaryP99Ms, 95, 100);
        Assert.InRange(snap.DecodeLatencyP99Ms, 190, 200);
        Assert.InRange(snap.PositionSaveP99Ms, 9.5, 10);

        s.Reset();
        var cleared = s.Snapshot();
        Assert.Equal(0, cleared.BoundaryEvents);
        Assert.Equal(0, cleared.BoundaryP99Ms);
        Assert.Equal(0, cleared.DecodeLatencyP99Ms);
    }

    [Fact]
    public void BlankPages_CountOnlyFramesWithAGap_AndKeepTheWorst()
    {
        var s = ReaderPerfStats.Current;
        s.Reset();

        s.RecordBlankPages(0);
        s.RecordBlankPages(1);
        s.RecordBlankPages(3);
        s.RecordBlankPages(2);

        var snap = s.Snapshot();
        Assert.Equal(3, snap.FramesWithBlankPage);
        Assert.Equal(3, snap.WorstBlankPages);
    }

    [Fact]
    public void LayoutShift_CountsEventsAndKeepsTheLargestMagnitude()
    {
        var s = ReaderPerfStats.Current;
        s.Reset();

        s.RecordLayoutShift(0);
        s.RecordLayoutShift(-40);
        s.RecordLayoutShift(12);

        var snap = s.Snapshot();
        Assert.Equal(2, snap.LayoutShiftEvents);
        Assert.Equal(40, snap.MaxLayoutShiftPx);
    }

    [Fact]
    public void SnapshotText_OnlyMentionsBoundaryMetrics_OnceSomethingWasRecorded()
    {
        var s = ReaderPerfStats.Current;
        s.Reset();
        Assert.DoesNotContain("boundary", s.Snapshot().ToString());

        s.RecordBoundaryHandlerMs(2.5);

        string text = s.Snapshot().ToString();
        Assert.Contains("boundary p50", text);
        Assert.Contains("blank frames", text);
    }

    [Fact]
    public void Pipeline_CountsEachPageOncePerWindowEntry_NotEveryFrame()
    {
        CbzFixture.Create(_cbz, pageCount: 8);
        ReaderPerfStats.Current.Reset();
        using var pipeline = ReaderImagePipeline.TryOpen(_cbz)!;

        for (int frame = 0; frame < 5; frame++)
        {
            Assert.Null(pipeline.TryGetCachedPage(3));
        }

        var snap = ReaderPerfStats.Current.Snapshot();
        Assert.Equal(1, snap.CacheMisses);
        Assert.Equal(0, snap.CacheHits);
    }

    [Fact]
    public void Pipeline_RecordsDecodeLatency_ForAPageThatLandsInTheWindow()
    {
        CbzFixture.Create(_cbz, pageCount: 8);
        ReaderPerfStats.Current.Reset();
        using var pipeline = ReaderImagePipeline.TryOpen(_cbz)!;
        var landed = new ManualResetEventSlim(false);
        pipeline.BackgroundDecodeCompleted += p => { if (p == 3) landed.Set(); };

        pipeline.SetVirtualizationWindow(3, 3);

        Assert.True(landed.Wait(TimeSpan.FromSeconds(5)), "page 3 never decoded in the background");
        Assert.True(ReaderPerfStats.Current.Snapshot().DecodeLatencyP99Ms > 0);
    }

    [Fact]
    public void StagedPipeline_DoesNotFeedTheProcessWideStats()
    {
        CbzFixture.Create(_cbz, pageCount: 8);
        ReaderPerfStats.Current.Reset();
        using var pipeline = ReaderImagePipeline.TryOpen(_cbz)!;
        pipeline.RecordStats = false;
        var landed = new ManualResetEventSlim(false);
        pipeline.BackgroundDecodeCompleted += p => { if (p == 3) landed.Set(); };

        pipeline.TryGetCachedPage(3);
        pipeline.SetVirtualizationWindow(3, 3);
        Assert.True(landed.Wait(TimeSpan.FromSeconds(5)));

        var snap = ReaderPerfStats.Current.Snapshot();
        Assert.Equal(0, snap.CacheMisses);
        Assert.Equal(0, snap.BackgroundDecodes);
        Assert.Equal(0, snap.DecodeLatencyP99Ms);
    }
}
