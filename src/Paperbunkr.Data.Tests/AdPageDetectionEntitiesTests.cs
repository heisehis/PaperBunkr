using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The <c>AddAdPageDetection</c> migration and its three entities (docs/superpowers/specs/2026-09-21-comic-reader-
/// page-intelligence-design.md §5) against a real migrated SQLite file, so the unique indexes, cascades and the
/// signed-<c>long</c> hash round-trip are the real ones. <c>Down</c> is a no-op, so only migrate-to-HEAD is asserted.
/// </summary>
public class AdPageDetectionEntitiesTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_adpage_test_{Guid.NewGuid():N}.db");

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
    public void Migration_CreatesTheThreeTables()
    {
        using var context = CreateContext();
        Assert.Empty(context.AdPageHashes.ToList());
        Assert.Empty(context.PageHashes.ToList());
        Assert.Empty(context.AdPageProposals.ToList());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    public void AdPageHash_RoundTripsAnyBitPattern(long hash)
    {
        using (var context = CreateContext())
        {
            context.AdPageHashes.Add(new AdPageHash { Hash = hash, CreatedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        using var read = CreateContext();
        Assert.Equal(hash, read.AdPageHashes.Single().Hash);
    }

    [Fact]
    public void PageHash_IsUniquePerIssueAndPage()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);
        context.PageHashes.Add(new PageHash { IssueId = issueId, PageNumber = 2, Hash = 1, ContentStamp = "a" });
        context.SaveChanges();
        context.PageHashes.Add(new PageHash { IssueId = issueId, PageNumber = 2, Hash = 2, ContentStamp = "b" });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void AdPageProposal_IsUniquePerIssueAndPage_EvenWhenRejected()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);
        context.AdPageProposals.Add(new AdPageProposal { IssueId = issueId, PageNumber = 5, Status = AdPageProposalStatus.Rejected, CreatedAt = DateTime.UtcNow });
        context.SaveChanges();
        context.AdPageProposals.Add(new AdPageProposal { IssueId = issueId, PageNumber = 5, CreatedAt = DateTime.UtcNow });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void DeletingAnIssue_CascadesToItsHashesAndProposals()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);
        context.PageHashes.Add(new PageHash { IssueId = issueId, PageNumber = 1, Hash = 7, ContentStamp = "a" });
        context.AdPageProposals.Add(new AdPageProposal { IssueId = issueId, PageNumber = 1, CreatedAt = DateTime.UtcNow });
        context.SaveChanges();

        context.Issues.Remove(context.Issues.Find(issueId)!);
        context.SaveChanges();

        Assert.Empty(context.PageHashes.ToList());
        Assert.Empty(context.AdPageProposals.ToList());
    }

    [Fact]
    public void DeletingAnAdHash_KeepsProposals_ButClearsTheirMatch()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);
        var ad = new AdPageHash { Hash = 9, CreatedAt = DateTime.UtcNow };
        context.AdPageHashes.Add(ad);
        context.SaveChanges();
        context.AdPageProposals.Add(new AdPageProposal { IssueId = issueId, PageNumber = 1, MatchedAdHashId = ad.Id, Status = AdPageProposalStatus.Rejected, CreatedAt = DateTime.UtcNow });
        context.SaveChanges();

        context.AdPageHashes.Remove(ad);
        context.SaveChanges();

        var proposal = context.AdPageProposals.AsNoTracking().Single();
        Assert.Null(proposal.MatchedAdHashId);
        Assert.Equal(AdPageProposalStatus.Rejected, proposal.Status);
    }

    [Fact]
    public void AdPageHash_SurvivesDeletionOfItsSourceIssue()
    {
        using var context = CreateContext();
        int issueId = SeedIssue(context);
        context.AdPageHashes.Add(new AdPageHash { Hash = 5, SourceIssueId = issueId, SourcePageNumber = 3, CreatedAt = DateTime.UtcNow });
        context.SaveChanges();

        context.Issues.Remove(context.Issues.Find(issueId)!);
        context.SaveChanges();

        Assert.Single(context.AdPageHashes.ToList());
    }
}
