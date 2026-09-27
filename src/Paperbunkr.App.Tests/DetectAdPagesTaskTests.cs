using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.AdDetection;
using Paperbunkr.App.Services.Scheduling;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using SkiaSharp;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The "Detect advertisement pages" scheduled task (docs/superpowers/specs/2026-09-21-comic-reader-page-
/// intelligence-design.md §5). Runs against a temp database via <see cref="PaperbunkrDbContext.DatabasePathOverride"/>,
/// since a catalog task body reaches the database through <c>PaperbunkrDb.CreateContext()</c>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DetectAdPagesTaskTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_adtask_test_{Guid.NewGuid():N}");

    public DetectAdPagesTaskTests()
    {
        Directory.CreateDirectory(_root);
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = Path.Combine(_root, "test.db");
        using var context = PaperbunkrDb.CreateContext();
        context.Database.Migrate();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static ScheduledTaskDescriptor Descriptor() => Assert.Single(ScheduledTaskCatalog.All, t => t.Id == ScheduledTaskCatalog.DetectAdPages);

    private static async Task<string> RunAsync()
    {
        var activity = new ActivityService(a => a(), _ => { });
        using var job = activity.StartJob(ActivityJobKind.Other, "Detect advertisement pages");
        return await Descriptor().RunAsync(job, CancellationToken.None);
    }

    [Fact]
    public void Task_IsRegistered_OffByDefault_AndDiskBound()
    {
        var task = Descriptor();

        Assert.False(task.DefaultEnabled);
        Assert.Equal(SchedulerResourceClass.DiskCpu, task.Resource);
        Assert.Equal(ScheduleMode.Interval, task.DefaultMode);
        Assert.Equal(ScheduledTaskCatalog.All.Count, ScheduledTaskCatalog.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal(ScheduledTaskCatalog.All.Count, ScheduledTaskCatalog.All.Select(t => t.Priority).Distinct().Count());
    }

    [Fact]
    public async Task Run_WithNoAdsYet_TellsTheUserToTagOne()
    {
        string summary = await RunAsync();

        Assert.Contains("tag an advertisement page", summary);
    }

    [Fact]
    public async Task Run_FindingAMatch_ReportsHowManyToReview()
    {
        long hash;
        using (var bitmap = PageHasherTests.Pattern(500))
        {
            hash = PageHasher.Compute(bitmap);
        }

        string path = Path.Combine(_root, "issue.cbz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            for (int i = 0; i < 6; i++)
            {
                using var page = PageHasherTests.Pattern(i == 4 ? 500 : 600 + i);
                using var stream = zip.CreateEntry($"p{i}.jpg").Open();
                byte[] bytes = PageHasherTests.Encode(page, SKEncodedImageFormat.Jpeg, quality: 70);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "S" };
            context.Series.Add(series);
            context.SaveChanges();
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", FilePath = path });
            context.AdPageHashes.Add(new AdPageHash { Hash = hash, CreatedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        string summary = await RunAsync();

        Assert.Equal("Found 1 possible ad page - review them in Library Health", summary);
    }
}
