using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Sharing;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="ArchivePageSource"/>'s bounded-resource behavior (docs/superpowers/specs/
/// 2026-09-19-remote-library-sharing-design.md §5/§7.3): more concurrent issues than the open-archive
/// cap must evict and reopen cleanly, never read from a closed archive, and never exceed the cap.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class ArchivePageSourceTests : IDisposable
{
    private const int IssueCount = 9; // > the 4-archive cap, so eviction is constant

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_archsrc_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly List<int> _issueIds = new();

    public ArchivePageSourceTests()
    {
        Directory.CreateDirectory(_root);
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "t.db")}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
        var series = new Series { Name = "Stress" };
        context.Series.Add(series);
        context.SaveChanges();

        for (int i = 0; i < IssueCount; i++)
        {
            string path = CbzFixture.Create(Path.Combine(_root, $"issue{i}.cbz"), pageCount: 3);
            var issue = new Issue { SeriesId = series.Id, Number = i.ToString(), FilePath = path };
            context.Issues.Add(issue);
            context.SaveChanges();
            _issueIds.Add(issue.Id);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task ConcurrentRequestsAcrossMoreIssuesThanTheCap_AllSucceed_AndTheCapHolds()
    {
        using var source = new ArchivePageSource(() => new PaperbunkrDbContext(_options), effectiveCoverPath: _ => null);
        int failures = 0;
        int maxOpen = 0;

        await Parallel.ForEachAsync(Enumerable.Range(0, 240), new ParallelOptions { MaxDegreeOfParallelism = 12 }, async (n, ct) =>
        {
            int issueId = _issueIds[n % IssueCount];
            var content = await source.GetPageAsync(issueId, n % 3, maxWidth: null, ct);
            if (content is null)
            {
                Interlocked.Increment(ref failures);
                return;
            }

            await using (content.Content)
            {
                var buffer = new MemoryStream();
                await content.Content.CopyToAsync(buffer, ct);
                if (buffer.Length == 0)
                {
                    Interlocked.Increment(ref failures);
                }
            }

            int open = source.OpenArchiveCount;
            int seen;
            while (open > (seen = Volatile.Read(ref maxOpen)) && Interlocked.CompareExchange(ref maxOpen, open, seen) != seen)
            {
            }
        });

        Assert.Equal(0, failures);
        Assert.True(maxOpen <= 4, $"open archives peaked at {maxOpen}, above the cap of 4");
    }

    [Fact]
    public async Task UnknownIssue_ReturnsNull_WithoutOpeningAnything()
    {
        using var source = new ArchivePageSource(() => new PaperbunkrDbContext(_options), effectiveCoverPath: _ => null);

        Assert.Null(await source.GetPagesAsync(9999, default));
        Assert.Equal(0, source.OpenArchiveCount);
    }

    [Fact]
    public async Task IssueWhoseFileWentMissing_ReturnsNull_InsteadOfThrowing()
    {
        File.Delete(Path.Combine(_root, "issue0.cbz"));
        using var source = new ArchivePageSource(() => new PaperbunkrDbContext(_options), effectiveCoverPath: _ => null);

        Assert.Null(await source.GetPageAsync(_issueIds[0], 0, null, default));
    }

    [Fact]
    public async Task Dispose_ClosesEveryArchive_AndLaterRequestsReturnNull()
    {
        var source = new ArchivePageSource(() => new PaperbunkrDbContext(_options), effectiveCoverPath: _ => null);
        await source.GetPageAsync(_issueIds[0], 0, null, default);
        Assert.Equal(1, source.OpenArchiveCount);

        source.Dispose();

        Assert.Equal(0, source.OpenArchiveCount);
        Assert.Null(await source.GetPageAsync(_issueIds[0], 0, null, default));
    }
}
