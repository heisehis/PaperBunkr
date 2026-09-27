using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>Verifies the <c>AddPageNotesAndClips</c> migration (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #7 and #28). <c>Down</c> is a deliberate no-op.</summary>
public class AddPageNotesAndClipsMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_notes_clips_migration_test_{Guid.NewGuid():N}.db");

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

    private static Issue AddIssue(PaperbunkrDbContext context)
    {
        var series = new Series { Name = "S" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue;
    }

    [Fact]
    public void Migration_AddsTheSummarySetting_OffByDefault()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        Assert.False(context.GetOrCreateAppSettings().InfoPanelShowSummary);
    }

    [Fact]
    public void PageNote_IsUniquePerPage_AndRoundTrips()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        var issue = AddIssue(context);

        context.PageNotes.Add(new PageNote { IssueId = issue.Id, PageNumber = 3, Text = "the map", CreatedTime = DateTime.UtcNow, ModifiedTime = DateTime.UtcNow });
        context.SaveChanges();
        Assert.Equal("the map", context.PageNotes.Single().Text);

        context.PageNotes.Add(new PageNote { IssueId = issue.Id, PageNumber = 3, Text = "again" });
        Assert.ThrowsAny<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void PageClips_AllowSeveralPerPage_AndNotesAndClipsGoWithTheirIssue()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        var issue = AddIssue(context);
        context.PageClips.Add(new PageClip { IssueId = issue.Id, PageNumber = 1, RectX = 0.1, RectY = 0.2, RectWidth = 0.3, RectHeight = 0.4, ImagePath = "a.png", CreatedTime = DateTime.UtcNow });
        context.PageClips.Add(new PageClip { IssueId = issue.Id, PageNumber = 1, RectX = 0.5, RectY = 0.5, RectWidth = 0.2, RectHeight = 0.2, ImagePath = "b.png", Caption = "lineup", CreatedTime = DateTime.UtcNow });
        context.PageNotes.Add(new PageNote { IssueId = issue.Id, PageNumber = 1, Text = "n" });
        context.SaveChanges();
        Assert.Equal(2, context.PageClips.Count());

        context.Issues.Remove(context.Issues.Single(i => i.Id == issue.Id));
        context.SaveChanges();

        Assert.Empty(context.PageClips);
        Assert.Empty(context.PageNotes);
    }
}
