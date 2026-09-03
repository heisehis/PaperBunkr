using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary><see cref="QuickOpenService.BuildIndex"/> projection coverage.</summary>
public class QuickOpenServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly QuickOpenService _service;

    public QuickOpenServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_quickopen_svc_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Database.EnsureCreated();
        }

        _service = new QuickOpenService(() => new PaperbunkrDbContext(_dbOptions));
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

    private void Seed()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Batman" };
        context.Series.Add(series);
        context.SaveChanges();

        context.SeriesTitles.Add(new SeriesTitle { SeriesId = series.Id, Value = "バットマン" });
        context.Issues.Add(new Issue
        {
            SeriesId = series.Id, Number = "404", Title = "Year One",
            OpenedTime = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        context.Books.Add(new Book
        {
            Title = "Dune", Author = "Frank Herbert", Format = BookFormat.Epub, FilePath = @"C:\b\dune.epub",
            LastOpenedTime = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
        });
        var now = DateTime.UtcNow;
        context.ReadingLists.Add(new ReadingList { Name = "Knightfall", CreatedAt = now, UpdatedAt = now });
        context.SmartLists.Add(new SmartList { Name = "Unread Manga" });
        context.Collections.Add(new Collection { Name = "DC" });
        context.StoryEvents.Add(new StoryEvent { Name = "Crisis", CreatedAt = now, UpdatedAt = now });
        context.Continuities.Add(new Continuity { Name = "Post-Crisis", CreatedAt = now, UpdatedAt = now });
        context.SaveChanges();
    }

    [Fact]
    public void BuildIndex_ProducesOneEntryPerEntity_PlusScreensAndActions()
    {
        Seed();
        var index = _service.BuildIndex();

        Assert.Single(index, e => e.Kind == QuickOpenKind.Series);
        Assert.Single(index, e => e.Kind == QuickOpenKind.Issue);
        Assert.Single(index, e => e.Kind == QuickOpenKind.Book);
        Assert.Single(index, e => e.Kind == QuickOpenKind.ReadingList && e.Primary == "Knightfall");
        Assert.Single(index, e => e.Kind == QuickOpenKind.SmartList && e.Primary == "Unread Manga");
        Assert.Single(index, e => e.Kind == QuickOpenKind.Collection && e.Primary == "DC");
        Assert.Single(index, e => e.Kind == QuickOpenKind.StoryEvent && e.Primary == "Crisis");
        Assert.Single(index, e => e.Kind == QuickOpenKind.Continuity && e.Primary == "Post-Crisis");

        Assert.Equal(QuickOpenService.Screens.Count, index.Count(e => e.Kind == QuickOpenKind.Screen));
        Assert.Equal(QuickOpenService.Actions.Count, index.Count(e => e.Kind == QuickOpenKind.Action));
    }

    [Fact]
    public void BuildIndex_IssueCarriesSeriesNameAndOpenedTime()
    {
        Seed();
        var issue = _service.BuildIndex().Single(e => e.Kind == QuickOpenKind.Issue);

        Assert.Equal("Batman", issue.Secondary);
        Assert.Contains("Batman #404", issue.Primary);
        Assert.Contains("Year One", issue.Primary);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), issue.RecencyUtc);
    }

    [Fact]
    public void BuildIndex_BookRecencyIsLastOpenedTime()
    {
        Seed();
        var book = _service.BuildIndex().Single(e => e.Kind == QuickOpenKind.Book);
        Assert.Equal(new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), book.RecencyUtc);
    }

    [Fact]
    public void BuildIndex_SeriesIsMatchableByAltTitle()
    {
        Seed();
        var series = _service.BuildIndex().Single(e => e.Kind == QuickOpenKind.Series);
        Assert.NotNull(QuickOpenMatcher.Score("バットマン", series.Primary));
    }

    [Fact]
    public void BuildIndex_EmptyLibrary_StillHasScreensAndActions()
    {
        var index = _service.BuildIndex();
        Assert.Equal(QuickOpenService.Screens.Count + QuickOpenService.Actions.Count, index.Count);
    }
}
