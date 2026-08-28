using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="IssuePropertiesScreenViewModel"/>'s edit-buffer pattern
/// (docs/superpowers/specs/2026-08-07-issue-properties-editor-design.md §3) - Load copies fields
/// and disposes its context immediately, Save re-fetches and writes, Cancel never touches the
/// database at all. Joins <see cref="AvaloniaTestCollection"/> since cover-art lookup
/// (<c>CoverImageCache</c>/<c>SeriesCardSample</c>) touches Avalonia media types.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class IssuePropertiesScreenViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _issueId;

    public IssuePropertiesScreenViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_issuepropsvm_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();

        var issue = new Issue
        {
            SeriesId = series.Id,
            Number = "1",
            Title = "Original Title",
            Writer = "Original Writer",
            Volume = "2",
            Year = 1999,
            Rating = 3,
            CommunityRating = 4,
            ColorMode = ColorMode.BlackAndWhite,
            Summary = "Original Summary",
        };
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

    private Issue GetIssue()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Issues.First(i => i.Id == _issueId);
    }

    [Fact]
    public void Load_PopulatesBufferFromDatabase()
    {
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));

        vm.Load(_issueId);

        Assert.Equal("Test Series #1", vm.HeaderLabel);
        Assert.Equal("Original Title", vm.Title);
        Assert.Equal("Original Writer", vm.Writer);
        Assert.Equal("2", vm.VolumeText);
        Assert.Equal("1999", vm.YearText);
        Assert.Equal(3, vm.MyRating);
        Assert.Equal(4, vm.CommunityRating);
        Assert.Equal(nameof(ColorMode.BlackAndWhite), vm.ColorModeText);
        Assert.Equal("Original Summary", vm.Summary);
    }

    [Fact]
    public void Save_WritesAllFieldsBack_AndGoesBack()
    {
        bool wentBack = false;
        var vm = new IssuePropertiesScreenViewModel(() => wentBack = true, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId);

        vm.Title = "New Title";
        vm.Writer = "New Writer";
        vm.VolumeText = "5";
        vm.YearText = "2020";
        vm.ColorModeText = nameof(ColorMode.Color);
        vm.Summary = "New Summary";

        vm.SaveCommand.Execute(null);

        Assert.True(wentBack);
        var issue = GetIssue();
        Assert.Equal("New Title", issue.Title);
        Assert.Equal("New Writer", issue.Writer);
        Assert.Equal("5", issue.Volume);
        Assert.Equal(2020, issue.Year);
        Assert.Equal(ColorMode.Color, issue.ColorMode);
        Assert.Equal("New Summary", issue.Summary);
    }

    [Fact]
    public void Save_RoundTripsBookAge_AndReDerivesCharacterIndex()
    {
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId);

        vm.BookAge = "Silver (1956-69)";
        vm.Characters = "Batman, Robin";

        vm.SaveCommand.Execute(null);

        var issue = GetIssue();
        Assert.Equal("Silver (1956-69)", issue.BookAge);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(2, context.CharacterAppearances.Count(a => a.IssueId == _issueId));

        // Editing the text down re-syncs (removes the stale appearance).
        var vm2 = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm2.Load(_issueId);
        Assert.Equal("Silver (1956-69)", vm2.BookAge);
        vm2.Characters = "Batman";
        vm2.SaveCommand.Execute(null);

        using var context2 = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context2.CharacterAppearances.Where(a => a.IssueId == _issueId));
    }

    [Fact]
    public void Cancel_NeverTouchesDatabase_RowUnchanged()
    {
        bool wentBack = false;
        var vm = new IssuePropertiesScreenViewModel(() => wentBack = true, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId);

        vm.Title = "Edited But Never Saved";
        vm.VolumeText = "999";

        vm.CancelCommand.Execute(null);

        Assert.True(wentBack);
        var issue = GetIssue();
        Assert.Equal("Original Title", issue.Title);
        Assert.Equal("2", issue.Volume);
    }

    /// <summary>docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §3 - the manual "add a physical book" flow's Cancel-deletes-if-unedited guard.</summary>
    [Fact]
    public void Cancel_WithDeleteIfUneditedAndNoChanges_DeletesTheIssue()
    {
        bool wentBack = false;
        var vm = new IssuePropertiesScreenViewModel(() => wentBack = true, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId, deleteIfUnedited: true);

        vm.CancelCommand.Execute(null);

        Assert.True(wentBack);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Null(context.Issues.FirstOrDefault(i => i.Id == _issueId));
    }

    [Fact]
    public void Cancel_WithDeleteIfUneditedButFieldChanged_KeepsTheIssue()
    {
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId, deleteIfUnedited: true);

        vm.Title = "Now I typed something";
        vm.CancelCommand.Execute(null);

        var issue = GetIssue();
        Assert.Equal(_issueId, issue.Id);
    }

    [Fact]
    public void Cancel_WithoutDeleteIfUnedited_NeverDeletes()
    {
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId); // deleteIfUnedited defaults false - every existing call site (e.g. Detail's "Edit Properties")

        vm.CancelCommand.Execute(null);

        var issue = GetIssue();
        Assert.Equal(_issueId, issue.Id);
    }

    [Fact]
    public void Save_EmptyOrInvalidNumericText_ParsesToNull()
    {
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId);

        vm.VolumeText = string.Empty;
        vm.YearText = "not-a-number";

        vm.SaveCommand.Execute(null);

        var issue = GetIssue();
        Assert.Null(issue.Volume);
        Assert.Null(issue.Year);
    }

    [Fact]
    public void MyRating_ClickingSameStar_TogglesToUnrated()
    {
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId);
        Assert.Equal(3, vm.MyRating);

        vm.SetMyRating3Command.Execute(null);
        Assert.Null(vm.MyRating);
        Assert.False(vm.MyRatingStar1);

        vm.SetMyRating4Command.Execute(null);
        Assert.Equal(4, vm.MyRating);
        Assert.True(vm.MyRatingStar4);
        Assert.False(vm.MyRatingStar5);
    }

    [Fact]
    public void CommunityRating_ClickingSameStar_TogglesToUnrated()
    {
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId);
        Assert.Equal(4, vm.CommunityRating);

        vm.SetCommunityRating4Command.Execute(null);
        Assert.Null(vm.CommunityRating);

        vm.SetCommunityRating2Command.Execute(null);
        Assert.Equal(2, vm.CommunityRating);
        Assert.True(vm.CommunityRatingStar2);
        Assert.False(vm.CommunityRatingStar3);
    }

    /// <summary>
    /// P6 follow-up (docs/alpha-todo.md): <see cref="MainViewModel.TryLeaveCurrentEditor"/> queries
    /// this to decide whether to prompt before navigating away from an in-progress edit.
    /// </summary>
    [Fact]
    public void HasUnsavedChanges_FalseImmediatelyAfterLoad()
    {
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));

        vm.Load(_issueId);

        Assert.False(vm.HasUnsavedChanges());
    }

    [Fact]
    public void HasUnsavedChanges_TrueAfterEditingAField()
    {
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId);

        vm.Title = "Edited Title";

        Assert.True(vm.HasUnsavedChanges());
    }

    [Fact]
    public void HasUnsavedChanges_FalseAfterSave()
    {
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId);
        vm.Title = "Edited Title";

        vm.SaveCommand.Execute(null);

        Assert.False(vm.HasUnsavedChanges());
    }

    [Fact]
    public void HasUnsavedChanges_SwitchingTabsAloneDoesNotCount()
    {
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId);

        vm.GoDetailsCommand.Execute(null);
        vm.GoPlotNotesCommand.Execute(null);

        Assert.False(vm.HasUnsavedChanges());
    }
}
