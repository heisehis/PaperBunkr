using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>A throwaway SQLite database for the manga series-metadata tests, plus a seeder for a manga series with chapters.</summary>
public sealed class MangaTestDb : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_manga_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public MangaTestDb()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public PaperbunkrDbContext CreateContext() => new(_options);

    public Func<PaperbunkrDbContext> Factory => CreateContext;

    /// <summary>A Manga series with <paramref name="chapters"/> chapters that all carry publisher "X", writer "A" and the genre "Action".</summary>
    public int SeedSeries(int chapters, Action<int, Issue>? perChapter = null, ContentType type = ContentType.Manga)
    {
        using var context = CreateContext();
        var series = new Series { Name = "Test Manga", ContentType = type };
        for (int n = 1; n <= chapters; n++)
        {
            var issue = new Issue
            {
                FilePath = $"chapter{n}.cbz",
                Number = n.ToString(),
                Title = $"Chapter {n}",
                Publisher = "X",
                Writer = "A",
                Translator = "Group",
                ScanInformation = "Scans",
                LastPageRead = n % 3,
            };
            issue.Tags.Add(new IssueTag { Field = IssueTagField.Genre, Value = "Action", Category = "Genre" });
            perChapter?.Invoke(n, issue);
            series.Issues.Add(issue);
        }

        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
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
}
