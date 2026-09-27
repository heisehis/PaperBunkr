using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="CreatorResolver"/> (docs/superpowers/specs/2026-09-23-metron-api-utilization-
/// design.md) against a real SQLite database.
/// </summary>
public class CreatorResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public CreatorResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_creator_test_{Guid.NewGuid():N}.db");
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

    private static int SeedIssue(PaperbunkrDbContext context, string seriesName, Action<Issue>? configure = null)
    {
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        configure?.Invoke(issue);
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void SyncFromIssue_OneCreatorTwoRoles_ProducesTwoCreditRows()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, "Test Series", i =>
        {
            i.Writer = "Todd McFarlane";
            i.Penciller = "Todd McFarlane";
        });

        CreatorResolver.SyncFromIssue(context, issueId);

        Assert.Single(context.Creators);
        var credits = context.CreatorCredits.Where(c => c.IssueId == issueId).ToList();
        Assert.Equal(2, credits.Count);
        Assert.Contains(credits, c => c.Role == "Writer");
        Assert.Contains(credits, c => c.Role == "Penciller");
    }

    [Fact]
    public void SyncFromIssue_MultipleCreatorsAcrossFields_MaterializesAll()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, "Test Series", i =>
        {
            i.Writer = "Alan Moore";
            i.Penciller = "Dave Gibbons";
            i.Colorist = "John Higgins";
        });

        CreatorResolver.SyncFromIssue(context, issueId);

        Assert.Equal(3, context.Creators.Count());
        Assert.Equal(3, context.CreatorCredits.Count(c => c.IssueId == issueId));
    }

    [Fact]
    public void SyncFromIssue_RemovedFromField_PrunesThatCreditAndOrphanCreator()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, "Test Series", i => i.Writer = "Alan Moore");
        CreatorResolver.SyncFromIssue(context, issueId);
        Assert.Single(context.Creators);

        var issue = context.Issues.Single(i => i.Id == issueId);
        issue.Writer = null;
        context.SaveChanges();
        CreatorResolver.SyncFromIssue(context, issueId);

        Assert.Empty(context.CreatorCredits.Where(c => c.IssueId == issueId));
        Assert.Empty(context.Creators);
    }

    [Fact]
    public void SyncFromIssue_TranslatorField_IsCaptured()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, "Test Series", i => i.Translator = "Jane Doe");

        CreatorResolver.SyncFromIssue(context, issueId);

        var credit = context.CreatorCredits.Include(c => c.Creator).Single(c => c.IssueId == issueId);
        Assert.Equal("Translator", credit.Role);
        Assert.Equal("Jane Doe", credit.Creator!.Name);
    }
}
