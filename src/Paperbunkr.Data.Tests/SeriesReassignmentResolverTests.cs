using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="SeriesReassignmentResolver.Apply"/> (docs/superpowers/specs/2026-08-17-
/// metadata-model-phase2b-series-reassignment-design.md) against a real SQLite database - this
/// method mutates real rows (find-or-create a target series, reassign a FK, conditionally delete a
/// source series), so pure in-memory objects (this project's usual resolver-test style) wouldn't
/// exercise the real behavior. Every test here populates <see cref="Series.Issues"/> via
/// <c>series.Issues.Add(issue)</c> before calling <see cref="SeriesReassignmentResolver.Apply"/>,
/// matching <c>LibraryFolderScanner</c>'s exact object-graph shape - a real bug (the reassignment
/// silently no-op'ing) only reproduced with that collection navigation populated, not with a
/// simpler hand-built graph.
/// </summary>
public class SeriesReassignmentResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public SeriesReassignmentResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_seriesreassign_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

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

    private (int issueId, MetadataProposal proposal) SeedIssueWithProposal(PaperbunkrDbContext context, string sourceSeriesName, string proposedSeriesName, params string[] otherIssueNumbersOnSource)
    {
        var series = new Series { Name = sourceSeriesName };
        context.Series.Add(series);

        var issue = new Issue { Series = series, Number = "1" };
        series.Issues.Add(issue);
        context.Issues.Add(issue);

        foreach (string otherNumber in otherIssueNumbersOnSource)
        {
            var otherIssue = new Issue { Series = series, Number = otherNumber };
            series.Issues.Add(otherIssue);
            context.Issues.Add(otherIssue);
        }

        var proposal = new MetadataProposal
        {
            Issue = issue,
            Field = MetadataProposalField.Series,
            CurrentValue = sourceSeriesName,
            ProposedValue = proposedSeriesName,
            Source = MetadataProposalSource.FilenameParser,
            Confidence = 0.6m,
            Status = MetadataProposalStatus.Accepted,
            CreatedAt = DateTime.UtcNow,
            ResolvedAt = DateTime.UtcNow,
        };
        issue.MetadataProposals.Add(proposal);
        context.MetadataProposals.Add(proposal);

        context.SaveChanges();
        return (issue.Id, proposal);
    }

    [Fact]
    public void Apply_TargetSeriesDoesNotExist_CreatesIt_MovesIssue()
    {
        int issueId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            (issueId, var proposal) = SeedIssueWithProposal(context, "Embedded Name", "Filename Name");
            SeriesReassignmentResolver.Apply(context, proposal);
        }

        using var verify = new PaperbunkrDbContext(_dbOptions);
        var issue = verify.Issues.Include(i => i.Series).Single(i => i.Id == issueId);
        Assert.Equal("Filename Name", issue.Series!.Name);
    }

    [Fact]
    public void Apply_TargetSeriesAlreadyExists_ReusesIt_DoesNotDuplicate()
    {
        int issueId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Series.Add(new Series { Name = "Filename Name" }); // pre-existing target
            context.SaveChanges();

            (issueId, var proposal) = SeedIssueWithProposal(context, "Embedded Name", "Filename Name");
            SeriesReassignmentResolver.Apply(context, proposal);
        }

        using var verify = new PaperbunkrDbContext(_dbOptions);
        var targetSeries = Assert.Single(verify.Series.Where(s => s.Name == "Filename Name"));
        var issue = verify.Issues.Single(i => i.Id == issueId);
        Assert.Equal(targetSeries.Id, issue.SeriesId);
    }

    [Fact]
    public void Apply_SourceSeriesBecomesEmpty_IsDeleted()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            (_, var proposal) = SeedIssueWithProposal(context, "Embedded Name", "Filename Name");
            SeriesReassignmentResolver.Apply(context, proposal);
        }

        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.DoesNotContain(verify.Series, s => s.Name == "Embedded Name");
        Assert.Single(verify.Series); // only "Filename Name" remains
    }

    [Fact]
    public void Apply_SourceSeriesHasOtherIssues_Survives()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            (_, var proposal) = SeedIssueWithProposal(context, "Embedded Name", "Filename Name", "2", "3");
            SeriesReassignmentResolver.Apply(context, proposal);
        }

        using var verify = new PaperbunkrDbContext(_dbOptions);
        var sourceSeries = Assert.Single(verify.Series.Where(s => s.Name == "Embedded Name"));
        Assert.Equal(2, verify.Issues.Count(i => i.SeriesId == sourceSeries.Id)); // #2 and #3 stayed
    }

    [Fact]
    public void Apply_MissingIssue_NoOp_DoesNotThrow()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var proposal = new MetadataProposal { IssueId = 999, Field = MetadataProposalField.Series, ProposedValue = "Anything" };

        SeriesReassignmentResolver.Apply(context, proposal); // should not throw
    }
}
