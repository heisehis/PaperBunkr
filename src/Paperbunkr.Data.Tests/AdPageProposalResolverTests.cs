using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

public class AdPageProposalResolverTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_adresolver_test_{Guid.NewGuid():N}.db");

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
        context.Database.Migrate();
        var series = new Series { Name = "S" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();
        return (context, issue.Id);
    }

    private static int Propose(PaperbunkrDbContext context, int issueId, int page)
    {
        var proposal = new AdPageProposal { IssueId = issueId, PageNumber = page, CreatedAt = DateTime.UtcNow };
        context.AdPageProposals.Add(proposal);
        context.SaveChanges();
        return proposal.Id;
    }

    [Fact]
    public void Accept_TagsThePageAdvertisement_AndResolvesTheProposal()
    {
        var (context, issueId) = Create();
        using (context)
        {
            int id = Propose(context, issueId, 18);

            Assert.True(AdPageProposalResolver.Accept(context, id));

            var tag = Assert.Single(context.IssuePages.ToList());
            Assert.Equal(18, tag.PageNumber);
            Assert.Equal(PageType.Advertisement, tag.PageType);
            var proposal = context.AdPageProposals.Single();
            Assert.Equal(AdPageProposalStatus.Accepted, proposal.Status);
            Assert.NotNull(proposal.ResolvedAt);
        }
    }

    [Fact]
    public void Accept_KeepsAnExistingRotationOnThePage()
    {
        var (context, issueId) = Create();
        using (context)
        {
            context.IssuePages.Add(new IssuePage { IssueId = issueId, PageNumber = 18, RotationDegrees = 90 });
            context.SaveChanges();
            int id = Propose(context, issueId, 18);

            AdPageProposalResolver.Accept(context, id);

            var tag = Assert.Single(context.IssuePages.ToList());
            Assert.Equal(PageType.Advertisement, tag.PageType);
            Assert.Equal(90, tag.RotationDegrees);
        }
    }

    [Fact]
    public void Reject_MarksItRejected_AndWritesNoTag()
    {
        var (context, issueId) = Create();
        using (context)
        {
            int id = Propose(context, issueId, 18);

            Assert.True(AdPageProposalResolver.Reject(context, id));

            Assert.Equal(AdPageProposalStatus.Rejected, context.AdPageProposals.Single().Status);
            Assert.Empty(context.IssuePages.ToList());
        }
    }

    [Fact]
    public void AlreadyResolvedProposals_AreNotChangedAgain()
    {
        var (context, issueId) = Create();
        using (context)
        {
            int id = Propose(context, issueId, 18);
            AdPageProposalResolver.Reject(context, id);

            Assert.False(AdPageProposalResolver.Accept(context, id));
            Assert.False(AdPageProposalResolver.Reject(context, id));
            Assert.Equal(AdPageProposalStatus.Rejected, context.AdPageProposals.Single().Status);
            Assert.Empty(context.IssuePages.ToList());
        }
    }

    [Fact]
    public void UnknownProposal_ChangesNothing()
    {
        var (context, _) = Create();
        using (context)
        {
            Assert.False(AdPageProposalResolver.Accept(context, 999));
            Assert.False(AdPageProposalResolver.Reject(context, 999));
        }
    }

    [Fact]
    public void AcceptAll_AndRejectAll_ReturnHowManyChanged()
    {
        var (context, issueId) = Create();
        using (context)
        {
            var ids = new[] { Propose(context, issueId, 16), Propose(context, issueId, 17), Propose(context, issueId, 18) };
            AdPageProposalResolver.Reject(context, ids[0]);

            Assert.Equal(2, AdPageProposalResolver.AcceptAll(context, ids));
            Assert.Equal(0, AdPageProposalResolver.RejectAll(context, ids));
            Assert.Equal(new[] { 17, 18 }, context.IssuePages.OrderBy(p => p.PageNumber).Select(p => p.PageNumber).ToArray());
        }
    }
}
