using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="DetailScreenViewModel.ReloadCurrentSeries"/>, added so returning from the
/// Issue Properties editor (docs/superpowers/specs/2026-08-07-issue-properties-editor-design.md)
/// shows edited data instead of the stale pre-edit state - found by manual verification: Save
/// persisted correctly but the Detail screen's Issues tab tile still showed the old label until a
/// full reload. Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite
/// file, same approach as <see cref="ReaderScreenViewModelTests"/> since <see cref="DetailScreenViewModel"/>
/// has no injected context-factory seam of its own. Joins <see cref="AvaloniaTestCollection"/>
/// since that override is a shared static other test classes also mutate.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DetailScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly int _seriesId;
    private readonly int _issueId;

    public DetailScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_detailscreenvm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
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
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ReloadCurrentSeries_BeforeAnySeriesLoaded_NoOps()
    {
        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });

        vm.ReloadCurrentSeries(); // should not throw despite no LoadSeries call yet
    }

    [Fact]
    public void ReloadCurrentSeries_ReflectsExternalChangesToIssueNumber()
    {
        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);
        Assert.Equal("#1", vm.Tabs.Issues.Single().Title);

        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.First(i => i.Id == _issueId).Number = "7";
            context.SaveChanges();
        }

        vm.ReloadCurrentSeries();

        Assert.Equal("#7", vm.Tabs.Issues.Single().Title);
    }

    [Fact]
    public void ReloadCurrentSeries_AfterBulkEditSave_ReflectsUpdatedCreditsRow()
    {
        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);
        Assert.Empty(vm.Meta.Writer);

        var bulkVm = new BulkIssuePropertiesScreenViewModel(() => { });
        bulkVm.Load(new[] { _issueId });
        bulkVm.ArtistFields.Single(f => f.Label == "Writer").Value = "New Writer Name";
        bulkVm.SaveCommand.Execute(null);

        vm.ReloadCurrentSeries();

        Assert.Equal(new[] { "New Writer Name" }, vm.Meta.Writer.Select(p => p.Value));
    }

    [Fact]
    public void SelectingExactlyOneIssue_SwitchesCreditsRowToThatIssue()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.First(i => i.Id == _issueId).Writer = "Solo Writer";
            context.SaveChanges();
        }

        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);
        var issueCard = vm.Tabs.Issues.Single();

        vm.Tabs.ToggleIssueSelection(issueCard, isShiftHeld: false);

        Assert.Equal(new[] { "Solo Writer" }, vm.Meta.Writer.Select(p => p.Value));
        Assert.True(vm.CanEdit);
        Assert.Equal("Edit Issue", vm.EditButtonLabel);
    }

    [Fact]
    public void SelectingExactlyOneIssue_SwitchesSummaryToThatIssuesOwnSummary()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = context.Series.First(s => s.Id == _seriesId);
            series.Summary = "Series-level summary";
            context.Issues.First(i => i.Id == _issueId).Summary = "This issue's own summary";
            context.SaveChanges();
        }

        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);
        Assert.Equal("Series-level summary", vm.Summary);

        vm.Tabs.ToggleIssueSelection(vm.Tabs.Issues.Single(), isShiftHeld: false);
        Assert.Equal("This issue's own summary", vm.Summary);

        vm.Tabs.ToggleIssueSelection(vm.Tabs.Issues.Single(), isShiftHeld: false); // deselect
        Assert.Equal("Series-level summary", vm.Summary);
    }

    [Fact]
    public void SelectingTwoIssues_RevertsToSeriesAggregate()
    {
        int issue2Id;
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.First(i => i.Id == _issueId).Writer = "Writer One";
            var issue2 = new Issue { SeriesId = _seriesId, Number = "2", Writer = "Writer Two" };
            context.Issues.Add(issue2);
            context.SaveChanges();
            issue2Id = issue2.Id;
        }

        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);
        var card1 = vm.Tabs.Issues.First(i => i.Id == _issueId);
        var card2 = vm.Tabs.Issues.First(i => i.Id == issue2Id);

        vm.Tabs.ToggleIssueSelection(card1, isShiftHeld: false);
        Assert.Equal(new[] { "Writer One" }, vm.Meta.Writer.Select(p => p.Value));

        vm.Tabs.ToggleIssueSelection(card2, isShiftHeld: false);

        Assert.Equal(new[] { "Writer One", "Writer Two" }, vm.Meta.Writer.Select(p => p.Value));
        Assert.Equal("Edit 2 Issues", vm.EditButtonLabel);
    }

    [Fact]
    public void Edit_NoSelection_IsDisabled()
    {
        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);

        Assert.False(vm.CanEdit);
        Assert.Equal("Edit", vm.EditButtonLabel);
    }

    [Fact]
    public void Edit_OneSelected_InvokesGoToPropertiesWithThatIssueId()
    {
        int? captured = null;
        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: id => captured = id, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);
        vm.Tabs.ToggleIssueSelection(vm.Tabs.Issues.Single(), isShiftHeld: false);

        vm.EditCommand.Execute(null);

        Assert.Equal(_issueId, captured);
    }
}
