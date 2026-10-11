using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary><see cref="FirstPageAd"/> (docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §3) against a real temp SQLite database.</summary>
public class FirstPageAdTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pb_firstpagead_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;

    public FirstPageAdTests()
    {
        _context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private int Seed(int? pageCount)
    {
        var series = new Series { Name = "S" };
        var issue = new Issue { Series = series, Number = "1", PageCount = pageCount };
        _context.Issues.Add(issue);
        _context.SaveChanges();
        return issue.Id;
    }

    private PageType? TypeOf(int issueId, int page) => _context.IssuePages.AsNoTracking().FirstOrDefault(p => p.IssueId == issueId && p.PageNumber == page)?.PageType;

    [Fact]
    public void TheFirstPageBecomesAnAd_AndTheSecondTheCover()
    {
        int id = Seed(24);

        Assert.True(FirstPageAd.Apply(_context, id));

        Assert.Equal(PageType.Advertisement, TypeOf(id, 0));
        Assert.Equal(PageType.Cover, TypeOf(id, 1));
        Assert.Null(TypeOf(id, 2));
    }

    [Fact]
    public void ARotationAlreadyOnAPage_IsKept()
    {
        int id = Seed(24);
        _context.IssuePages.Add(new IssuePage { IssueId = id, PageNumber = 1, RotationDegrees = 90 });
        _context.SaveChanges();

        FirstPageAd.Apply(_context, id);

        var page = _context.IssuePages.AsNoTracking().Single(p => p.IssueId == id && p.PageNumber == 1);
        Assert.Equal(PageType.Cover, page.PageType);
        Assert.Equal(90, page.RotationDegrees);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    public void ABookWithFewerThanTwoKnownPages_IsSkipped(int? pages)
    {
        int id = Seed(pages);

        Assert.False(FirstPageAd.Apply(_context, id));

        Assert.Empty(_context.IssuePages);
    }

    [Fact]
    public void ApplyingTwice_ChangesNothingTheSecondTime()
    {
        int id = Seed(10);
        FirstPageAd.Apply(_context, id);

        FirstPageAd.Apply(_context, id);

        Assert.Equal(2, _context.IssuePages.Count(p => p.IssueId == id));
    }
}
