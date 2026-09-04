using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="MetadataWriteBackQueue"/> - debounce/coalescing, serial drain, the
/// settings gate re-checked at flush time, and the aggregated toast
/// (docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md).
/// </summary>
public class MetadataWriteBackQueueTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _dir;

    public MetadataWriteBackQueueTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mwbq_db_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_mwbq_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void SetSettings(bool master, bool automatic, bool sidecar = false)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var s = context.GetOrCreateAppSettings();
        s.WriteMetadataToFiles = master;
        s.WriteMetadataAutomatically = automatic;
        s.WriteNativeSidecar = sidecar;
        context.SaveChanges();
    }

    private int SeedIssue(string cbzName, string summary = "seed")
    {
        string cbz = Path.Combine(_dir, cbzName);
        CbzFixture.Create(cbz, pageCount: 1);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Kilo Station" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = cbz, Summary = summary };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static string ReadSummary(string cbz)
    {
        using var provider = Providers.Readers.CreateSourceProvider(cbz);
        provider.Open(async: false);
        return ((IInfoStorage)provider).LoadInfo(InfoLoadingMethod.Complete)?.Summary ?? string.Empty;
    }

    private MetadataWriteBackQueue CreateQueue(List<(string, string)> toasts) => new(
        () => new PaperbunkrDbContext(_dbOptions),
        new MetadataFileWriteBackService(() => new PaperbunkrDbContext(_dbOptions)),
        (t, m) => toasts.Add((t, m)),
        TimeSpan.FromMilliseconds(30));

    [Fact]
    public async Task Drain_MasterOff_WritesNothing()
    {
        SetSettings(master: false, automatic: true);
        int id = SeedIssue("a.cbz", "would-be-written");
        var toasts = new List<(string, string)>();
        var queue = CreateQueue(toasts);

        queue.Enqueue(id);
        await queue.DrainNowAsync();

        // The bare fixture has no ComicInfo.xml; a skipped drain leaves it that way.
        using (var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(_dir, "a.cbz")))
        {
            Assert.Null(zip.GetEntry("ComicInfo.xml"));
        }
        Assert.Empty(toasts);
    }

    [Fact]
    public async Task Drain_AutomaticOff_NonManualSkipped_ManualWrites()
    {
        SetSettings(master: true, automatic: false);
        int id = SeedIssue("b.cbz", "db-value");
        var toasts = new List<(string, string)>();
        var queue = CreateQueue(toasts);

        queue.Enqueue(id, manual: false);
        await queue.DrainNowAsync();
        Assert.Equal(string.Empty, ReadSummary(Path.Combine(_dir, "b.cbz"))); // fixture had no ComicInfo

        queue.Enqueue(id, manual: true);
        await queue.DrainNowAsync();
        Assert.Equal("db-value", ReadSummary(Path.Combine(_dir, "b.cbz")));
    }

    [Fact]
    public async Task Drain_CoalescesByIssueId()
    {
        SetSettings(master: true, automatic: true);
        int id = SeedIssue("c.cbz", "coalesced");
        var toasts = new List<(string, string)>();
        var queue = CreateQueue(toasts);

        queue.Enqueue(id);
        queue.Enqueue(id);
        queue.Enqueue(id);
        await queue.DrainNowAsync();

        Assert.Equal("coalesced", ReadSummary(Path.Combine(_dir, "c.cbz")));
        var toast = Assert.Single(toasts);
        Assert.Contains("1 file updated", toast.Item2);
    }

    [Fact]
    public async Task Drain_MixedBatch_SummaryReportsWroteAndSkipped()
    {
        SetSettings(master: true, automatic: true);
        int good = SeedIssue("good.cbz", "written");

        string cbr = Path.Combine(_dir, "bad.cbr");
        File.WriteAllText(cbr, "nope");
        int bad;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var issue = new Issue { SeriesId = context.Series.First().Id, Number = "2", FilePath = cbr };
            context.Issues.Add(issue);
            context.SaveChanges();
            bad = issue.Id;
        }

        var toasts = new List<(string, string)>();
        var queue = CreateQueue(toasts);
        queue.Enqueue(good, manual: true);
        queue.Enqueue(bad, manual: true);
        await queue.DrainNowAsync();

        var toast = Assert.Single(toasts);
        Assert.Contains("1 file updated", toast.Item2);
        Assert.Contains("1 skipped", toast.Item2);
    }

    [Fact]
    public async Task Drain_SidecarSetting_ControlsSidecarEntry()
    {
        SetSettings(master: true, automatic: true, sidecar: false);
        int id = SeedIssue("d.cbz", "x");
        var queue = CreateQueue(new List<(string, string)>());
        queue.Enqueue(id, manual: true);
        await queue.DrainNowAsync();

        using (var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(_dir, "d.cbz")))
        {
            Assert.Null(zip.GetEntry("paperbunkr.json"));
        }

        SetSettings(master: true, automatic: true, sidecar: true);
        queue.Enqueue(id, manual: true);
        await queue.DrainNowAsync();

        using (var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(_dir, "d.cbz")))
        {
            Assert.NotNull(zip.GetEntry("paperbunkr.json"));
        }
    }
}
