using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

public class IssuePageTaggerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pagetagger_test_{Guid.NewGuid():N}.db");

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

    private (PaperbunkrDbContext Context, int IssueId) Create()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        var series = new Series { Name = "S" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();
        return (context, issue.Id);
    }

    [Fact]
    public void SetPageType_UntaggedPage_CreatesARow()
    {
        var (context, issueId) = Create();
        using (context)
        {
            var row = IssuePageTagger.SetPageType(context, issueId, 3, PageType.Deleted);

            Assert.NotNull(row);
            var stored = Assert.Single(context.IssuePages.ToList());
            Assert.Equal(3, stored.PageNumber);
            Assert.Equal(PageType.Deleted, stored.PageType);
        }
    }

    [Fact]
    public void SetPageType_ExistingRow_KeepsRotationAndSpreadPosition()
    {
        var (context, issueId) = Create();
        using (context)
        {
            context.IssuePages.Add(new IssuePage { IssueId = issueId, PageNumber = 3, RotationDegrees = 90, SpreadPosition = PageSpreadPosition.Near });
            context.SaveChanges();

            IssuePageTagger.SetPageType(context, issueId, 3, PageType.Advertisement);

            var stored = Assert.Single(context.IssuePages.ToList());
            Assert.Equal(PageType.Advertisement, stored.PageType);
            Assert.Equal(90, stored.RotationDegrees);
            Assert.Equal(PageSpreadPosition.Near, stored.SpreadPosition);
        }
    }

    [Fact]
    public void SetPageType_StoryWithNoOtherOverrides_RemovesTheRow()
    {
        var (context, issueId) = Create();
        using (context)
        {
            IssuePageTagger.SetPageType(context, issueId, 3, PageType.Deleted);

            var row = IssuePageTagger.SetPageType(context, issueId, 3, PageType.Story);

            Assert.Null(row);
            Assert.Empty(context.IssuePages.ToList());
        }
    }

    [Fact]
    public void SetPageType_StoryOnARotatedPage_KeepsTheRowForTheRotation()
    {
        var (context, issueId) = Create();
        using (context)
        {
            context.IssuePages.Add(new IssuePage { IssueId = issueId, PageNumber = 3, PageType = PageType.Deleted, RotationDegrees = 180 });
            context.SaveChanges();

            IssuePageTagger.SetPageType(context, issueId, 3, PageType.Story);

            var stored = Assert.Single(context.IssuePages.ToList());
            Assert.Equal(PageType.Story, stored.PageType);
            Assert.Equal(180, stored.RotationDegrees);
        }
    }

    [Fact]
    public void SetPageType_StoryOnAnUntaggedPage_DoesNothing()
    {
        var (context, issueId) = Create();
        using (context)
        {
            Assert.Null(IssuePageTagger.SetPageType(context, issueId, 3, PageType.Story));
            Assert.Empty(context.IssuePages.ToList());
        }
    }
}
