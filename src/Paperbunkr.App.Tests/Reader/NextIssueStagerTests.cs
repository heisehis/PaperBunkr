using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests.Reader;

/// <summary>
/// <see cref="NextIssueStager"/> (docs/superpowers/specs/2026-09-25-comic-reader-performance-design.md A): what gets staged, when it is
/// adopted, and every rule that discards it. Uses real generated CBZ files and the real pipeline; the next-issue lookup, file stamp,
/// disposal and clock are injected.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class NextIssueStagerTests : IDisposable
{
    private readonly List<string> _files = new();
    private readonly List<ReaderImagePipeline> _disposedByStager = new();
    private readonly List<ReaderImagePipeline> _leaked = new();
    private DateTime _now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
    private string? _stamp = "stamp-1";

    public void Dispose()
    {
        foreach (var p in _leaked)
        {
            try { p.Dispose(); } catch { }
        }

        foreach (string file in _files)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
        }
    }

    private string NewCbz(int pages = 6)
    {
        string path = Path.Combine(Path.GetTempPath(), $"pb_stager_{Guid.NewGuid():N}.cbz");
        CbzFixture.Create(path, pageCount: pages);
        _files.Add(path);
        return path;
    }

    private NextIssueStager CreateStager(Func<int, int?, int?, StagingTarget?> resolve, Func<string, int?, ReaderImagePipeline?>? open = null)
    {
        return new NextIssueStager(
            resolve,
            open,
            stampOf: _ => _stamp,
            dispose: p => { lock (_disposedByStager) { _disposedByStager.Add(p); } p.Dispose(); },
            utcNow: () => _now);
    }

    private static async Task Settled(NextIssueStager stager) => await stager.PendingWork.WaitAsync(TimeSpan.FromSeconds(60));

    private int DisposedCount()
    {
        lock (_disposedByStager) { return _disposedByStager.Count; }
    }

    private void WaitForDisposed(int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DisposedCount() < expected && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }
    }

    // ---- the position rule ----

    [Theory]
    [InlineData(0, 10, StagingAction.Discard)]
    [InlineData(3, 10, StagingAction.Discard)]   // 6 pages remain: below the last 5
    [InlineData(4, 10, StagingAction.Discard)]   // 5 remain
    [InlineData(5, 10, StagingAction.None)]      // 4 remain: hysteresis band
    [InlineData(6, 10, StagingAction.None)]      // 3 remain: hysteresis band
    [InlineData(7, 10, StagingAction.Ensure)]    // 2 remain: the last 3 pages
    [InlineData(8, 10, StagingAction.Ensure)]
    [InlineData(9, 10, StagingAction.Ensure)]    // the final page
    public void EvaluatePosition_StagesInTheLastThreePages_DropsBelowTheLastFive(int position, int pageCount, StagingAction expected)
    {
        Assert.Equal(expected, NextIssueStager.EvaluatePosition(position, pageCount));
    }

    [Fact]
    public void EvaluatePosition_ShortIssue_StagesImmediately()
    {
        Assert.Equal(StagingAction.Ensure, NextIssueStager.EvaluatePosition(0, 3));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    public void EvaluatePosition_NothingLoaded_DoesNothing(int position, int pageCount)
    {
        Assert.Equal(StagingAction.None, NextIssueStager.EvaluatePosition(position, pageCount));
    }

    // ---- staging and adoption ----

    [Fact]
    public async Task EnsureStaged_OpensTheNextIssueOnce_AndAdoptionHandsItOver()
    {
        string next = NewCbz();
        int opens = 0;
        using var stager = CreateStager(
            (_, _, _) => new StagingTarget(2, next, false, false),
            (path, limit) => { Interlocked.Increment(ref opens); return ReaderImagePipeline.TryOpen(path, limit); });

        stager.EnsureStaged(1, 10, null, null, 320);
        stager.EnsureStaged(1, 10, null, null, 320); // same current issue: no second open
        await Settled(stager);

        Assert.Equal(1, opens);
        Assert.True(stager.HasStaged);
        Assert.Equal(2, stager.StagedIssueId);

        var adopted = stager.TryAdopt(2, next);
        _leaked.Add(adopted!);

        Assert.NotNull(adopted);
        Assert.True(adopted!.RecordStats);
        Assert.False(stager.HasStaged);
        Assert.Equal(0, DisposedCount());
        Assert.Null(stager.TryAdopt(2, next)); // it is gone now
    }

    [Fact]
    public async Task StagedPipeline_HasTheViewportWidthApplied_AndItsFirstPagesDecoded()
    {
        string next = NewCbz(pages: 6); // native 64x96
        using var stager = CreateStager((_, _, _) => new StagingTarget(2, next, false, false));

        stager.EnsureStaged(1, 10, null, null, viewportWidth: 32);
        await Settled(stager);
        var adopted = stager.TryAdopt(2, next);
        _leaked.Add(adopted!);

        Assert.Equal(32, adopted!.ViewportWidth);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (adopted.TryGetCachedPage(0) is null && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.NotNull(adopted.TryGetCachedPage(0));
        Assert.Equal(32, adopted.TryGetCachedPage(0)!.PixelSize.Width);
    }

    [Fact]
    public async Task Staging_DoesNotFeedTheProcessWideStats_UntilAdopted()
    {
        string next = NewCbz();
        ReaderPerfStats.Current.Reset();
        using var stager = CreateStager((_, _, _) => new StagingTarget(2, next, false, false));

        stager.EnsureStaged(1, 10, null, null, 320);
        await Settled(stager);
        Thread.Sleep(300); // let pages 0-1 decode in the background

        var snap = ReaderPerfStats.Current.Snapshot();
        Assert.Equal(0, snap.BackgroundDecodes);
        Assert.Equal(0, snap.ArchiveReads + snap.SessionReads);

        _leaked.Add(stager.TryAdopt(2, next)!);
    }

    [Fact]
    public async Task AdoptingADifferentIssue_DiscardsWhatWasStaged()
    {
        string next = NewCbz();
        using var stager = CreateStager((_, _, _) => new StagingTarget(2, next, false, false));
        stager.EnsureStaged(1, 10, null, null, 320);
        await Settled(stager);

        Assert.Null(stager.TryAdopt(99, next));

        WaitForDisposed(1);
        Assert.Equal(1, DisposedCount());
        Assert.False(stager.HasStaged);
    }

    [Fact]
    public async Task AChangedFile_IsNotAdopted_AndIsDisposed()
    {
        string next = NewCbz();
        using var stager = CreateStager((_, _, _) => new StagingTarget(2, next, false, false));
        stager.EnsureStaged(1, 10, null, null, 320);
        await Settled(stager);

        _stamp = "stamp-2"; // the file changed on disk after staging

        Assert.Null(stager.TryAdopt(2, next));
        WaitForDisposed(1);
        Assert.Equal(1, DisposedCount());
    }

    [Fact]
    public async Task StagingForAnotherCurrentIssue_ReplacesTheOldOne()
    {
        string a = NewCbz();
        string b = NewCbz();
        using var stager = CreateStager((current, _, _) => current == 1
            ? new StagingTarget(2, a, false, false)
            : new StagingTarget(3, b, false, false));

        stager.EnsureStaged(1, 10, null, null, 320);
        await Settled(stager);
        stager.EnsureStaged(2, 10, null, null, 320);
        await Settled(stager);

        Assert.Equal(3, stager.StagedIssueId);
        WaitForDisposed(1);
        Assert.Equal(1, DisposedCount());
    }

    // ---- discard rules ----

    [Fact]
    public async Task Discard_DisposesTheStagedPipeline()
    {
        string next = NewCbz();
        using var stager = CreateStager((_, _, _) => new StagingTarget(2, next, false, false));
        stager.EnsureStaged(1, 10, null, null, 320);
        await Settled(stager);

        stager.Discard();

        WaitForDisposed(1);
        Assert.False(stager.HasStaged);
        Assert.Equal(1, DisposedCount());
    }

    [Fact]
    public async Task Discard_WhileOpening_DisposesTheResultWhenItLands()
    {
        string next = NewCbz();
        using var gate = new ManualResetEventSlim(false);
        using var opening = new ManualResetEventSlim(false);
        using var stager = CreateStager(
            (_, _, _) => new StagingTarget(2, next, false, false),
            (path, limit) =>
            {
                opening.Set();
                gate.Wait(TimeSpan.FromSeconds(5));
                return ReaderImagePipeline.TryOpen(path, limit);
            });

        stager.EnsureStaged(1, 10, null, null, 320);
        Assert.True(opening.Wait(TimeSpan.FromSeconds(5)));
        stager.Discard();
        gate.Set();
        await Settled(stager);

        Assert.False(stager.HasStaged);
        WaitForDisposed(1);
        Assert.Equal(1, DisposedCount());
    }

    [Fact]
    public async Task IdlePipeline_IsDroppedAfterTwoMinutes_ButNotBefore()
    {
        string next = NewCbz();
        using var stager = CreateStager((_, _, _) => new StagingTarget(2, next, false, false));
        stager.EnsureStaged(1, 10, null, null, 320);
        await Settled(stager);

        _now += TimeSpan.FromSeconds(119);
        stager.ExpireIfIdle();
        Assert.True(stager.HasStaged);

        _now += TimeSpan.FromSeconds(2);
        stager.ExpireIfIdle();

        Assert.False(stager.HasStaged);
        WaitForDisposed(1);
        Assert.Equal(1, DisposedCount());
    }

    [Fact]
    public async Task AskingAgainForTheSameIssue_KeepsItFresh()
    {
        string next = NewCbz();
        using var stager = CreateStager((_, _, _) => new StagingTarget(2, next, false, false));
        stager.EnsureStaged(1, 10, null, null, 320);
        await Settled(stager);

        _now += TimeSpan.FromSeconds(100);
        stager.EnsureStaged(1, 10, null, null, 320); // reader still near the end: touches it
        _now += TimeSpan.FromSeconds(100);
        stager.ExpireIfIdle();

        Assert.True(stager.HasStaged);
    }

    [Fact]
    public async Task Dispose_DisposesTheStagedPipeline_AndStagesNothingAfterwards()
    {
        string next = NewCbz();
        var stager = CreateStager((_, _, _) => new StagingTarget(2, next, false, false));
        stager.EnsureStaged(1, 10, null, null, 320);
        await Settled(stager);

        stager.Dispose();
        WaitForDisposed(1);
        stager.EnsureStaged(5, 10, null, null, 320);
        await Settled(stager);

        Assert.Equal(1, DisposedCount());
        Assert.False(stager.HasStaged);
    }

    // ---- what is never staged ----

    [Fact]
    public async Task NothingIsStaged_ForNoNextIssue_RemoteIssues_MissingFiles_OrNoFile()
    {
        var targets = new StagingTarget?[]
        {
            null,
            new StagingTarget(2, NewCbz(), IsRemote: true, FileIsMissing: false),
            new StagingTarget(2, NewCbz(), IsRemote: false, FileIsMissing: true),
            new StagingTarget(2, null, false, false),
            new StagingTarget(2, "", false, false),
        };

        foreach (var target in targets)
        {
            int opens = 0;
            using var stager = CreateStager((_, _, _) => target, (path, limit) => { Interlocked.Increment(ref opens); return ReaderImagePipeline.TryOpen(path, limit); });

            stager.EnsureStaged(1, 10, null, null, 320);
            await Settled(stager);

            Assert.False(stager.HasStaged);
            Assert.Equal(0, opens);
        }
    }

    [Fact]
    public async Task AFileThatCannotBeOpened_StagesNothing_AndDoesNotThrow()
    {
        string broken = Path.Combine(Path.GetTempPath(), $"pb_stager_broken_{Guid.NewGuid():N}.cbz");
        File.WriteAllBytes(broken, [1, 2, 3]);
        _files.Add(broken);
        using var stager = CreateStager((_, _, _) => new StagingTarget(2, broken, false, false));

        stager.EnsureStaged(1, 10, null, null, 320);
        await Settled(stager);

        Assert.False(stager.HasStaged);
    }

    [Fact]
    public async Task AResolverThatThrows_IsSwallowed()
    {
        using var stager = CreateStager((_, _, _) => throw new InvalidOperationException("database is locked"));

        stager.EnsureStaged(1, 10, null, null, 320);
        await Settled(stager);

        Assert.False(stager.HasStaged);
    }

    [Fact]
    public async Task ADeletedFile_IsNotStaged()
    {
        string next = NewCbz();
        _stamp = null; // the file is gone by the time the stamp is taken
        using var stager = CreateStager((_, _, _) => new StagingTarget(2, next, false, false));

        stager.EnsureStaged(1, 10, null, null, 320);
        await Settled(stager);

        Assert.False(stager.HasStaged);
    }
}
