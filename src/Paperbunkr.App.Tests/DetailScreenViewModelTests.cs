using System.Linq;
using FluentIcons.Common;
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

    private static string[] Writers(DetailScreenViewModel vm) =>
        vm.Band.Groups.FirstOrDefault(g => g.IsCreditsGroup)?.Writers?.Select(p => p.Value).ToArray() ?? System.Array.Empty<string>();

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
        Assert.Empty(Writers(vm));

        var bulkVm = new BulkIssuePropertiesScreenViewModel(() => { });
        bulkVm.Load(new[] { _issueId });
        bulkVm.ArtistFields.Single(f => f.Label == "Writer").Value = "New Writer Name";
        bulkVm.SaveCommand.Execute(null);

        vm.ReloadCurrentSeries();

        Assert.Equal(new[] { "New Writer Name" }, Writers(vm));
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

        Assert.Equal(new[] { "Solo Writer" }, Writers(vm));
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
        Assert.Equal(new[] { "Writer One" }, Writers(vm));

        vm.Tabs.ToggleIssueSelection(card2, isShiftHeld: false);

        Assert.Equal(new[] { "Writer One", "Writer Two" }, Writers(vm));
        Assert.Equal("Edit 2 Issues", vm.EditButtonLabel);
    }

    [Fact]
    public void HeaderTitle_TracksLoadedSeries_AndRaisesPropertyChanged()
    {
        int otherSeriesId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var other = new Series { Name = "A Completely Different Series" };
            context.Series.Add(other);
            context.SaveChanges();
            otherSeriesId = other.Id;
        }

        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);
        Assert.Equal("Test Series", ((IDetailHeaderSource)vm).HeaderTitle);

        var raised = new System.Collections.Generic.List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.LoadSeries(otherSeriesId);

        Assert.Equal("A Completely Different Series", ((IDetailHeaderSource)vm).HeaderTitle);
        Assert.Contains(nameof(IDetailHeaderSource.HeaderTitle), raised);
    }

    [Fact]
    public void FocusingOneIssue_PrimaryHeroAction_BecomesReadThatIssue()
    {
        int? readerTarget = null;
        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: id => readerTarget = id, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);

        // No focus yet: primary action is the series-level Continue button.
        Assert.Equal("Continue — Issue #1", vm.Actions[0].Label);

        vm.Tabs.ToggleIssueSelection(vm.Tabs.Issues.Single(), isShiftHeld: false);

        Assert.Equal("Read — Issue #1", vm.Actions[0].Label);
        Assert.True(vm.Actions[0].IsPrimary);
        Assert.Equal(Symbol.Play, vm.Actions[0].Icon);
        Assert.Equal(Symbol.Edit, vm.Actions[1].Icon);
        Assert.Equal(Symbol.Image, vm.Actions[2].Icon);
        vm.Actions[0].Command.Execute(null);
        Assert.Equal(_issueId, readerTarget);

        // Deselecting reverts to the series-level button.
        vm.Tabs.ToggleIssueSelection(vm.Tabs.Issues.Single(), isShiftHeld: false);
        Assert.Equal("Continue — Issue #1", vm.Actions[0].Label);
    }

    [Fact]
    public void ReadingStatus_HeaderProperty_NullForUnknown_EnumNameOtherwise()
    {
        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);
        Assert.Null(((IDetailHeaderSource)vm).ReadingStatus);
        Assert.False(vm.Band.HasReadingStatus);

        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            context.Series.Single(s => s.Id == _seriesId).ReadingStatus = ReadingStatus.Completed;
            context.SaveChanges();
        }

        vm.LoadSeries(_seriesId);
        Assert.Equal("Completed", ((IDetailHeaderSource)vm).ReadingStatus);
        Assert.True(vm.Band.HasReadingStatus);
        Assert.Equal("Completed", vm.Band.ReadingStatusValue);
    }

    [Fact]
    public void MetaBadges_AggregateAcrossIssues_ThenFocusedIssueOverrides()
    {
        int annualId;
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            var s = context.Series.Single(x => x.Id == _seriesId);
            s.Status = SeriesStatus.Completed;
            // series.Publisher stays blank - the aggregate must pick it up from the issues.
            context.Issues.Single(i => i.Id == _issueId).Format = "Single Issue"; // #1
            context.Issues.AddRange(
                new Issue { SeriesId = _seriesId, Number = "2", Publisher = "DC Comics", Format = "Single Issue", AgeRating = "Teen" },
                new Issue { SeriesId = _seriesId, Number = "3", Publisher = "DC Comics", Format = "Single Issue", AgeRating = "Teen" });
            var annual = new Issue { SeriesId = _seriesId, Number = "1", Format = "Annual", AgeRating = "Mature", LanguageISO = "en" };
            context.Issues.Add(annual);
            context.SaveChanges();
            annualId = annual.Id;
        }

        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);

        var badges = ((IDetailHeaderSource)vm).MetaBadges;
        Assert.Contains(badges, b => b.Mark == Paperbunkr.App.Controls.MarkFamily.Publisher && b.MarkValue == "DC Comics"); // from issues, not series
        Assert.Contains(badges, b => b.Text == "Complete");
        Assert.Contains(badges, b => b.Mark == Paperbunkr.App.Controls.MarkFamily.Format && b.MarkValue == "Single Issue"); // most common
        Assert.Contains(badges, b => b.Mark == Paperbunkr.App.Controls.MarkFamily.AgeRating && b.MarkValue == "Teen");
        // issue-count / unread: a separate plain-text line (Part 4 revision, user direction), not a
        // badge - all 4 seeded issues are unread (no LastPageRead set).
        Assert.Equal("4 issues  ·  4 unread", ((IDetailHeaderSource)vm).IssueSummaryLine);

        // focus the annual -> its own format/rating take over
        vm.Tabs.ToggleIssueSelection(vm.Tabs.Specials.First(i => i.Id == annualId), isShiftHeld: false);
        var focused = ((IDetailHeaderSource)vm).MetaBadges;
        Assert.Contains(focused, b => b.Mark == Paperbunkr.App.Controls.MarkFamily.Format && b.MarkValue == "Annual");
        Assert.Contains(focused, b => b.Mark == Paperbunkr.App.Controls.MarkFamily.AgeRating && b.MarkValue == "Mature");
        Assert.Contains(focused, b => b.Mark == Paperbunkr.App.Controls.MarkFamily.Language && b.MarkValue == "en");

        // deselect -> back to the aggregate
        vm.Tabs.ToggleIssueSelection(vm.Tabs.Specials.First(i => i.Id == annualId), isShiftHeld: false);
        Assert.Contains(((IDetailHeaderSource)vm).MetaBadges, b => b.Mark == Paperbunkr.App.Controls.MarkFamily.Format && b.MarkValue == "Single Issue");
    }

    /// <summary>Bug report 2026-09-04: "the unread doesn't update when i finish reading a comic" -
    /// marking an issue read from a Detail tile must move the hero's <c>IssueSummaryLine</c> unread
    /// count immediately, not just on the next full <see cref="DetailScreenViewModel.LoadSeries"/>.</summary>
    [Fact]
    public void MarkingAnIssueRead_FromTheTile_UpdatesTheHeroUnreadCountImmediately()
    {
        int issue2Id;
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            context.Issues.Single(i => i.Id == _issueId).PageCount = 10;
            var issue2 = new Issue { SeriesId = _seriesId, Number = "2", PageCount = 10 };
            context.Issues.Add(issue2);
            context.SaveChanges();
            issue2Id = issue2.Id;
        }

        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);
        Assert.Equal("2 issues  ·  2 unread", ((IDetailHeaderSource)vm).IssueSummaryLine);

        // deselected (series-aggregate) tile mark-as-read
        vm.Tabs.MarkIssueReadCommand.Execute(vm.Tabs.Issues.First(i => i.Id == _issueId));
        Assert.Equal("2 issues  ·  1 unread", ((IDetailHeaderSource)vm).IssueSummaryLine);

        // focus the other issue, then mark it read too - the count must still update while focused
        vm.Tabs.ToggleIssueSelection(vm.Tabs.Issues.First(i => i.Id == issue2Id), isShiftHeld: false);
        vm.Tabs.MarkIssueReadCommand.Execute(vm.Tabs.Issues.First(i => i.Id == issue2Id));
        Assert.Equal("2 issues", ((IDetailHeaderSource)vm).IssueSummaryLine);
    }

    [Fact]
    public void ReadingStatusPicker_IsBuilt_AndSettingItRoundTripsToHeroAndBand()
    {
        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
        vm.LoadSeries(_seriesId);

        var picker = ((IDetailHeaderSource)vm).ReadingStatusPicker;
        Assert.NotNull(picker);
        Assert.Same(picker, vm.Band.ReadingStatusPicker);
        Assert.True(vm.Band.ShowReadingStatusPicker);

        picker!.SetCommand.Execute(ReadingStatus.Reading);

        Assert.Equal("Reading", ((IDetailHeaderSource)vm).ReadingStatus);
        Assert.Equal("Reading", vm.Band.ReadingStatusValue);
        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        Assert.Equal(ReadingStatus.Reading, context.Series.Single(s => s.Id == _seriesId).ReadingStatus);
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
