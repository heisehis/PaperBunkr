using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="TeamResolver"/> (docs/superpowers/specs/2026-09-23-metron-api-utilization-
/// design.md) against a real SQLite database.
/// </summary>
public class TeamResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public TeamResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_team_test_{Guid.NewGuid():N}.db");
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

    private static int SeedIssue(PaperbunkrDbContext context, string seriesName, string? teams)
    {
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", Teams = teams };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void SyncFromIssue_MaterializesAppearances()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, "Justice League", "Justice League, Suicide Squad");

        TeamResolver.SyncFromIssue(context, issueId);

        Assert.Equal(2, context.TeamAppearances.Count(a => a.IssueId == issueId));
        Assert.Equal(2, context.Teams.Count());
    }

    [Fact]
    public void SyncFromIssue_RemovesStale_AndPrunesOrphanTeams()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, "Justice League", "Justice League, Suicide Squad");
        TeamResolver.SyncFromIssue(context, issueId);

        var issue = context.Issues.Single(i => i.Id == issueId);
        issue.Teams = "Justice League";
        context.SaveChanges();
        TeamResolver.SyncFromIssue(context, issueId);

        Assert.Single(context.TeamAppearances.Where(a => a.IssueId == issueId));
        Assert.Single(context.Teams);
        Assert.Equal("Justice League", context.Teams.Single().Name);
    }

    [Fact]
    public void GetOrCreate_IsCaseInsensitive()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var first = TeamResolver.GetOrCreate(context, "Avengers");
        var second = TeamResolver.GetOrCreate(context, "AVENGERS");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(context.Teams);
    }
}
