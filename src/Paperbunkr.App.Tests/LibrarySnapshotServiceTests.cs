using System;
using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="LibrarySnapshotService"/> (docs/superpowers/specs/2026-09-22-insights-backlog-
/// burndown-design.md) - the write side of the nightly library snapshot.
/// </summary>
public class LibrarySnapshotServiceTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;

    public LibrarySnapshotServiceTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_libsnap_svc_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static Series SeedSeries(PaperbunkrDbContext ctx)
    {
        var s = new Series { Name = "S" };
        ctx.Series.Add(s);
        ctx.SaveChanges();
        return s;
    }

    private static Issue SeedIssue(PaperbunkrDbContext ctx, int seriesId, int pageCount, int lastPageRead, int? remoteSourceId = null)
    {
        var i = new Issue { SeriesId = seriesId, PageCount = pageCount, LastPageRead = lastPageRead, RemoteSourceId = remoteSourceId };
        ctx.Issues.Add(i);
        ctx.SaveChanges();
        return i;
    }

    [Fact]
    public void Capture_OnEmptyLibrary_WritesAZeroedRow_WithoutThrowing()
    {
        using var ctx = PaperbunkrDb.CreateContext();
        var (total, backlog) = new LibrarySnapshotService().Capture(ctx);

        Assert.Equal(0, total);
        Assert.Equal(0, backlog);
        Assert.Single(ctx.LibrarySnapshots);
    }

    [Fact]
    public void Capture_CountsUnreadIssuesAsBacklog_ExcludesInProgressAndFinished()
    {
        using var ctx = PaperbunkrDb.CreateContext();
        var s = SeedSeries(ctx);
        SeedIssue(ctx, s.Id, pageCount: 20, lastPageRead: 0);  // unread - backlog
        SeedIssue(ctx, s.Id, pageCount: 20, lastPageRead: 10); // in progress - not backlog
        SeedIssue(ctx, s.Id, pageCount: 20, lastPageRead: 19); // finished - not backlog

        var (total, backlog) = new LibrarySnapshotService().Capture(ctx);

        Assert.Equal(3, total);
        Assert.Equal(1, backlog);
    }

    [Fact]
    public void Capture_ExcludesRemoteIssues_FromBothTotalsAndBacklog()
    {
        using var ctx = PaperbunkrDb.CreateContext();
        var source = new RemoteSource { InstanceId = "h", DisplayName = "Remote", Host = "h", CertFingerprint = "x" };
        ctx.RemoteSources.Add(source);
        ctx.SaveChanges();

        var s = SeedSeries(ctx);
        SeedIssue(ctx, s.Id, pageCount: 20, lastPageRead: 0); // local, unread
        SeedIssue(ctx, s.Id, pageCount: 20, lastPageRead: 0, remoteSourceId: source.Id); // remote, unread - excluded

        var (total, backlog) = new LibrarySnapshotService().Capture(ctx);

        Assert.Equal(1, total);
        Assert.Equal(1, backlog);
    }

    [Fact]
    public void Capture_TwiceInOneDay_UpsertsRatherThanDuplicating()
    {
        using var ctx = PaperbunkrDb.CreateContext();
        var s = SeedSeries(ctx);
        SeedIssue(ctx, s.Id, pageCount: 20, lastPageRead: 0);

        var service = new LibrarySnapshotService();
        service.Capture(ctx);
        SeedIssue(ctx, s.Id, pageCount: 20, lastPageRead: 0); // a second unread issue shows up before the re-run
        var (total, backlog) = service.Capture(ctx);

        Assert.Single(ctx.LibrarySnapshots); // one row for today, not two
        Assert.Equal(2, total);
        Assert.Equal(2, backlog);
        Assert.Equal(2, ctx.LibrarySnapshots.Single().BacklogComics); // overwritten, not stale
    }
}
