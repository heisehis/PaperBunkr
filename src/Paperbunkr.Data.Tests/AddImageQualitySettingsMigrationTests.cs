using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddImageQualitySettings</c> migration (docs/superpowers/specs/2026-09-26-comic-reader-image-quality-design.md): the auto-levels, sharpen and auto-crop defaults, the two per-issue overrides and
/// the <c>PageCropOverrides</c> table. <c>Down</c> is a deliberate no-op, so only the migrate-to-HEAD defaults and the round trip are asserted.
/// </summary>
public class AddImageQualitySettingsMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_image_quality_migration_test_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_AddsColumns_AllOffByDefault()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        var settings = context.GetOrCreateAppSettings();

        Assert.False(settings.DefaultAutoLevels);
        Assert.Equal(0, settings.DefaultSharpen);
        Assert.False(settings.AutoCropMargins);
    }

    [Fact]
    public void SettingsAndIssueOverrides_RoundTrip()
    {
        int issueId;
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var settings = context.GetOrCreateAppSettings();
            settings.DefaultAutoLevels = true;
            settings.DefaultSharpen = 2;
            settings.AutoCropMargins = true;
            var series = new Series { Name = "S" };
            context.Series.Add(series);
            context.SaveChanges();
            var issue = new Issue { SeriesId = series.Id, Number = "1", AutoLevelsOverride = false, SharpenOverride = 3 };
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;
        }

        using (var context = CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            Assert.True(settings.DefaultAutoLevels);
            Assert.Equal(2, settings.DefaultSharpen);
            Assert.True(settings.AutoCropMargins);

            var issue = context.Issues.Single(i => i.Id == issueId);
            Assert.False(issue.AutoLevelsOverride);
            Assert.Equal(3, issue.SharpenOverride);
        }
    }

    [Fact]
    public void PageCropOverride_IsUniquePerPage_AndDeletedWithItsIssue()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        var series = new Series { Name = "S" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();

        context.PageCropOverrides.Add(new PageCropOverride { IssueId = issue.Id, PageNumber = 4, Mode = PageCropMode.Never });
        context.SaveChanges();
        Assert.Equal(PageCropMode.Never, context.PageCropOverrides.Single().Mode);

        context.PageCropOverrides.Add(new PageCropOverride { IssueId = issue.Id, PageNumber = 4, Mode = PageCropMode.Always });
        Assert.ThrowsAny<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();

        context.Issues.Remove(context.Issues.Single(i => i.Id == issue.Id));
        context.SaveChanges();
        Assert.Empty(context.PageCropOverrides);
    }
}
