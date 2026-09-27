using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <see cref="PageReportService"/> and the <c>AddPageReports</c> migration (docs/superpowers/specs/2026-09-21-
/// comic-reader-page-intelligence-design.md §4). Runs against a real migrated SQLite file, so the unique index and
/// the cascade delete are the real ones.
/// </summary>
public class PageReportServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pagereport_test_{Guid.NewGuid():N}.db");

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
        var context = new PaperbunkrDbContext(options);
        context.Database.Migrate();
        return context;
    }

    private static int SeedIssue(PaperbunkrDbContext context)
    {
        var series = new Series { Name = "S" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void Migration_CreatesThePageReportsTable()
    {
        using var context = CreateContext();
        Assert.Empty(context.PageReports.ToList());
    }

    [Fact]
    public void Upsert_NewPage_CreatesTheReport()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);

        var undo = PageReportService.Upsert(context, issueId, 4, PageReportReason.Blank);

        var report = Assert.Single(context.PageReports.ToList());
        Assert.Equal(PageReportReason.Blank, report.Reason);
        Assert.Equal(4, report.PageNumber);
        Assert.False(report.Acknowledged);
        Assert.True(undo.WasNew);
    }

    [Fact]
    public void Upsert_SamePageTwice_UpdatesTheReasonInsteadOfAddingARow()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);

        PageReportService.Upsert(context, issueId, 4, PageReportReason.Blank);
        var second = PageReportService.Upsert(context, issueId, 4, PageReportReason.Corrupt);

        var report = Assert.Single(context.PageReports.ToList());
        Assert.Equal(PageReportReason.Corrupt, report.Reason);
        Assert.False(second.WasNew);
        Assert.Equal(PageReportReason.Blank, second.PreviousReason);
    }

    [Fact]
    public void Upsert_ADismissedReport_IsBroughtBack()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);
        PageReportService.Upsert(context, issueId, 4, PageReportReason.Blank);
        PageReportService.Acknowledge(context, context.PageReports.Single().Id);
        Assert.True(context.PageReports.Single().Acknowledged);

        PageReportService.Upsert(context, issueId, 4, PageReportReason.Blank);

        Assert.False(context.PageReports.Single().Acknowledged);
    }

    [Fact]
    public void Undo_OfANewReport_RemovesIt()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);
        var undo = PageReportService.Upsert(context, issueId, 4, PageReportReason.Blank);

        PageReportService.Undo(context, undo);

        Assert.Empty(context.PageReports.ToList());
    }

    [Fact]
    public void Undo_OfARepeatReport_RestoresThePreviousReasonAndDismissedState()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);
        PageReportService.Upsert(context, issueId, 4, PageReportReason.Blank);
        PageReportService.Acknowledge(context, context.PageReports.Single().Id);
        var undo = PageReportService.Upsert(context, issueId, 4, PageReportReason.LowRes);

        PageReportService.Undo(context, undo);

        var report = context.PageReports.Single();
        Assert.Equal(PageReportReason.Blank, report.Reason);
        Assert.True(report.Acknowledged);
    }

    [Fact]
    public void Remove_DeletesTheReport()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);
        PageReportService.Upsert(context, issueId, 4, PageReportReason.Other);

        PageReportService.Remove(context, context.PageReports.Single().Id);

        Assert.Empty(context.PageReports.ToList());
    }

    [Fact]
    public void UniqueIndex_RejectsADuplicateIssuePagePair()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);
        context.PageReports.Add(new PageReport { IssueId = issueId, PageNumber = 2, Reason = PageReportReason.Other, CreatedAt = DateTime.UtcNow });
        context.SaveChanges();
        context.PageReports.Add(new PageReport { IssueId = issueId, PageNumber = 2, Reason = PageReportReason.Blank, CreatedAt = DateTime.UtcNow });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void DeletingTheIssue_CascadesToItsReports()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);
        PageReportService.Upsert(context, issueId, 1, PageReportReason.Blank);
        PageReportService.Upsert(context, issueId, 2, PageReportReason.Corrupt);

        context.Issues.Remove(context.Issues.Find(issueId)!);
        context.SaveChanges();

        Assert.Empty(context.PageReports.ToList());
    }
}
