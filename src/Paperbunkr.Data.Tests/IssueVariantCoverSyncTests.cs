using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="IssueVariantCoverSync"/> (docs/superpowers/specs/2026-09-23-metron-api-
/// utilization-design.md) against a real SQLite database.
/// </summary>
public class IssueVariantCoverSyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public IssueVariantCoverSyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_variantcover_test_{Guid.NewGuid():N}.db");
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

    private static int SeedIssue(PaperbunkrDbContext context)
    {
        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void SyncFromIssueDetails_CreatesVariants()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context);

        IssueVariantCoverSync.SyncFromIssueDetails(context, issueId, new[] { new ComicVineVariantCover("2nd Print", "https://x/v1.jpg") });

        var variant = context.IssueVariantCovers.Single();
        Assert.Equal(issueId, variant.IssueId);
        Assert.Equal("2nd Print", variant.Name);
        Assert.Equal("https://x/v1.jpg", variant.ImageUrl);
    }

    [Fact]
    public void SyncFromIssueDetails_RemovesStaleVariants()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context);
        IssueVariantCoverSync.SyncFromIssueDetails(context, issueId, new[]
        {
            new ComicVineVariantCover("2nd Print", "https://x/v1.jpg"),
            new ComicVineVariantCover("3rd Print", "https://x/v2.jpg"),
        });

        IssueVariantCoverSync.SyncFromIssueDetails(context, issueId, new[] { new ComicVineVariantCover("2nd Print", "https://x/v1.jpg") });

        Assert.Single(context.IssueVariantCovers);
    }

    [Fact]
    public void SyncFromIssueDetails_RunTwice_IsIdempotent()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context);

        IssueVariantCoverSync.SyncFromIssueDetails(context, issueId, new[] { new ComicVineVariantCover("2nd Print", "https://x/v1.jpg") });
        IssueVariantCoverSync.SyncFromIssueDetails(context, issueId, new[] { new ComicVineVariantCover("2nd Print", "https://x/v1.jpg") });

        Assert.Single(context.IssueVariantCovers);
    }
}
