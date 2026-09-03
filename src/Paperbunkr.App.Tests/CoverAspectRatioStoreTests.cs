using System.Threading;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="CoverAspectRatioStore"/> - the process-wide learned cache that lets
/// Panorama render each cover at its real aspect ratio while virtualized. In
/// <see cref="AvaloniaTestCollection"/> to serialize against the cover-decode-path tests that also
/// feed the store.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CoverAspectRatioStoreTests : IDisposable
{
    public CoverAspectRatioStoreTests() => CoverAspectRatioStore.ResetForTests();

    public void Dispose() => CoverAspectRatioStore.ResetForTests();

    [Fact]
    public void Get_ReturnsNull_ForAnUnknownIssue()
    {
        Assert.Null(CoverAspectRatioStore.Get(999));
    }

    [Fact]
    public void Prime_SeedsRatios_WithoutRaisingRatiosLearned()
    {
        bool raised = false;
        CoverAspectRatioStore.RatiosLearned += (_, _) => raised = true;

        CoverAspectRatioStore.Prime(new[] { (1, 1.5), (2, 0.7) });

        Assert.Equal(1.5, CoverAspectRatioStore.Get(1));
        Assert.Equal(0.7, CoverAspectRatioStore.Get(2));
        Thread.Sleep(50);
        Assert.False(raised);
    }

    [Fact]
    public void Prime_IgnoresDegenerateRatios()
    {
        CoverAspectRatioStore.Prime(new[] { (1, 0.0), (2, double.NaN), (3, -1.0), (4, double.PositiveInfinity) });

        Assert.Null(CoverAspectRatioStore.Get(1));
        Assert.Null(CoverAspectRatioStore.Get(2));
        Assert.Null(CoverAspectRatioStore.Get(3));
        Assert.Null(CoverAspectRatioStore.Get(4));
    }

    [Fact]
    public void ReportRatio_StoresANewValue_Immediately()
    {
        CoverAspectRatioStore.ReportRatio(10, 1.42);
        Assert.Equal(1.42, CoverAspectRatioStore.Get(10)!.Value, precision: 3);
    }

    [Fact]
    public void ReportRatio_IgnoresANearIdenticalRepeat_ButAcceptsAMaterialChange()
    {
        int raisedCount = 0;
        CoverAspectRatioStore.RatiosLearned += (_, _) => Interlocked.Increment(ref raisedCount);

        CoverAspectRatioStore.ReportRatio(10, 1.5000);
        CoverAspectRatioStore.ReportRatio(10, 1.5005); // within epsilon -> no-op
        Thread.Sleep(700);
        Assert.Equal(1, raisedCount);

        CoverAspectRatioStore.ReportRatio(10, 0.66); // materially different -> another learn
        Thread.Sleep(700);
        Assert.Equal(2, raisedCount);
        Assert.Equal(0.66, CoverAspectRatioStore.Get(10)!.Value, precision: 2);
    }

    [Fact]
    public void ReportRatio_Report_IgnoresDegeneratePixelSizes()
    {
        CoverAspectRatioStore.Report(10, 0, 100);
        CoverAspectRatioStore.Report(11, 100, 0);
        Assert.Null(CoverAspectRatioStore.Get(10));
        Assert.Null(CoverAspectRatioStore.Get(11));
    }

    [Fact]
    public void Flush_WritesPendingRatios_ToTheIssueRows_WhenAContextFactoryIsWired()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_ratiostore_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={dbPath}").Options;
            int id1;
            int id2;
            using (var context = new PaperbunkrDbContext(options))
            {
                context.Database.EnsureCreated();
                var s = new Series { Name = "S" };
                context.Series.Add(s);
                context.SaveChanges();
                var a = new Issue { SeriesId = s.Id, Number = "1" };
                var b = new Issue { SeriesId = s.Id, Number = "2" };
                context.Issues.AddRange(a, b);
                context.SaveChanges();
                id1 = a.Id;
                id2 = b.Id;
            }

            CoverAspectRatioStore.ContextFactory = () => new PaperbunkrDbContext(options);
            CoverAspectRatioStore.ReportRatio(id1, 1.85);
            CoverAspectRatioStore.ReportRatio(id2, 0.68);
            CoverAspectRatioStore.FlushNowForTests();

            using (var context = new PaperbunkrDbContext(options))
            {
                Assert.Equal(1.85, context.Issues.Find(id1)!.CoverAspectRatio!.Value, precision: 2);
                Assert.Equal(0.68, context.Issues.Find(id2)!.CoverAspectRatio!.Value, precision: 2);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch (IOException) { }
        }
    }

    [Fact]
    public void Flush_IsInert_WhenNoContextFactoryIsWired()
    {
        // The under-test default: a stray Report from a decode-path test can never reach a real DB.
        CoverAspectRatioStore.ReportRatio(7, 1.3);
        CoverAspectRatioStore.FlushNowForTests(); // must not throw
        Assert.Equal(1.3, CoverAspectRatioStore.Get(7)!.Value, precision: 2);
    }
}
