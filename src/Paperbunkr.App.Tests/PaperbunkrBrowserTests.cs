using System.IO;
using System.Linq;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Real adapter tests for <see cref="PaperbunkrBrowser.SelectComics"/> - the gap-closure addition
/// that replaced its documented no-op (docs/superpowers/specs/2026-08-30-plugin-api-automation-
/// gaps-design.md). Same fixture convention as <see cref="PaperbunkrApplicationTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class PaperbunkrBrowserTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public PaperbunkrBrowserTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pluginbrowser_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch (IOException) { }
    }

    private static (int SeriesId, int[] IssueIds) SeedThreeIssues()
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = "Select Comics Series" };
        context.Series.Add(series);
        context.SaveChanges();

        var issues = new[]
        {
            new Issue { SeriesId = series.Id, Number = "1" },
            new Issue { SeriesId = series.Id, Number = "2" },
            new Issue { SeriesId = series.Id, Number = "3" },
        };
        context.Issues.AddRange(issues);
        context.SaveChanges();
        return (series.Id, issues.Select(i => i.Id).ToArray());
    }

    [Fact]
    public void SelectComics_IssuesPresentInLoadedLibraryData_SelectsExactlyThose_AndNavigatesToLibrary()
    {
        var (_, issueIds) = SeedThreeIssues();
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null); // populates IssueList.Rows
        var browser = new PaperbunkrBrowser(vm);

        using var context = PaperbunkrDb.CreateContext();
        var toSelect = context.Issues.Where(i => i.Id == issueIds[0] || i.Id == issueIds[2]).ToList();

        browser.SelectComics(toSelect);

        Assert.Equal("library", vm.CurrentScreen);
        Assert.Equal(2, vm.Library.Selection.Count);
        Assert.True(vm.Library.Selection.IsSelected(issueIds[0]));
        Assert.True(vm.Library.Selection.IsSelected(issueIds[2]));
        Assert.False(vm.Library.Selection.IsSelected(issueIds[1]));
    }

    [Fact]
    public void SelectComics_IssueNotInLoadedData_IsSilentlySkipped_NotAnError()
    {
        var (seriesId, issueIds) = SeedThreeIssues();
        var vm = new MainViewModel();
        vm.GoLibraryCommand.Execute(null);
        var browser = new PaperbunkrBrowser(vm);

        var phantom = new Issue { Id = 999999, SeriesId = seriesId, Number = "99" };
        using var context = PaperbunkrDb.CreateContext();
        var real = context.Issues.First(i => i.Id == issueIds[0]);

        var exception = Record.Exception(() => browser.SelectComics(new[] { real, phantom }));

        Assert.Null(exception);
        Assert.Equal(1, vm.Library.Selection.Count);
        Assert.True(vm.Library.Selection.IsSelected(issueIds[0]));
    }
}
