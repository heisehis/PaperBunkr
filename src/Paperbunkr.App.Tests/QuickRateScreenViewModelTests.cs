using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>First test coverage for <see cref="QuickRateScreenViewModel"/> - previously had none.
/// Exercises the docs/superpowers/specs/2026-09-13-activity-tab-event-log-expansion-design.md
/// RatingChanged activity logging added to <see cref="QuickRateScreenViewModel.Save"/>.</summary>
public class QuickRateScreenViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _seriesId;
    private readonly int _issueId;

    public QuickRateScreenViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_quickrate_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        context.Issues.Add(issue);
        context.SaveChanges();
        _issueId = issue.Id;
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

    private QuickRateScreenViewModel CreateViewModel(Action? close = null) =>
        new(close ?? (() => { }), () => new PaperbunkrDbContext(_dbOptions));

    [Fact]
    public void Save_RatingChanged_LogsActivityEvent()
    {
        var vm = CreateViewModel();
        vm.Load(_issueId);
        vm.Rating = 4;

        vm.SaveCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var activity = Assert.Single(context.SeriesActivityEvents);
        Assert.Equal(SeriesActivityEventKind.RatingChanged, activity.Kind);
        Assert.Equal(_seriesId, activity.SeriesId);
        Assert.Equal(_issueId, activity.IssueId);
        Assert.Equal("Rating set to 4", activity.Detail);
    }

    [Fact]
    public void Save_RatingUnchanged_LogsNoActivityEvent()
    {
        using (var seed = new PaperbunkrDbContext(_dbOptions))
        {
            seed.Issues.First(i => i.Id == _issueId).Rating = 3;
            seed.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.Load(_issueId);
        // vm.Rating is already 3 from Load - saving without changing it must not log anything.

        vm.SaveCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.SeriesActivityEvents);
    }

    [Fact]
    public void Save_RatingCleared_LogsClearedDetail()
    {
        using (var seed = new PaperbunkrDbContext(_dbOptions))
        {
            seed.Issues.First(i => i.Id == _issueId).Rating = 3;
            seed.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.Load(_issueId);
        vm.Rating = null;

        vm.SaveCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var activity = Assert.Single(context.SeriesActivityEvents);
        Assert.Equal("Rating cleared", activity.Detail);
    }
}
