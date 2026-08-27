using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BulkIssuePropertiesScreenViewModel"/>'s mixed-value Load, staged-only
/// Save, and list-field diff/merge (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md
/// §4). Two seeded issues deliberately share some field values and disagree on others, and share
/// only a partial overlap on the Writer list field, so the tests can assert the exact mixed-value/
/// intersection/delta behavior the spec calls for.
/// </summary>
public class BulkIssuePropertiesScreenViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _issueAId;
    private readonly int _issueBId;
    private readonly int _issueCId;
    private readonly int _seriesOneId;
    private readonly int _seriesTwoId;

    public BulkIssuePropertiesScreenViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bulkissuepropsvm_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        // Second series (docs/superpowers/specs/2026-08-16-manga-content-type-classification-design.md
        // §1) for multi-series Content Type write-through tests below - issueA/issueB share `series`,
        // issueC belongs to `seriesTwo`.
        var seriesTwo = new Series { Name = "Second Series" };
        context.Series.Add(seriesTwo);
        context.SaveChanges();

        var issueA = new Issue
        {
            SeriesId = series.Id,
            Number = "1",
            Title = "Watchmen #1",
            Publisher = "DC Comics",
            Writer = "Alan Moore, Dave Gibbons",
        };
        var issueB = new Issue
        {
            SeriesId = series.Id,
            Number = "2",
            Title = "Watchmen #2",
            Publisher = "DC Comics",
            Writer = "Alan Moore, Brian Bolland",
        };
        var issueC = new Issue
        {
            SeriesId = seriesTwo.Id,
            Number = "1",
            Title = "Other Series #1",
        };
        context.Issues.AddRange(issueA, issueB, issueC);
        context.SaveChanges();
        _issueAId = issueA.Id;
        _issueBId = issueB.Id;
        _issueCId = issueC.Id;
        _seriesOneId = series.Id;
        _seriesTwoId = seriesTwo.Id;
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

    private BulkIssuePropertiesScreenViewModel CreateViewModel(Action? goBack = null) =>
        new(goBack ?? (() => { }), () => new PaperbunkrDbContext(_dbOptions));

    private Issue GetIssue(int id)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Issues.First(i => i.Id == id);
    }

    private Series GetSeries(int id)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Series.First(s => s.Id == id);
    }

    [Fact]
    public void Load_ScalarFields_ShowsAgreedValue_OrBlankWhenMixed()
    {
        var vm = CreateViewModel();

        vm.Load(new[] { _issueAId, _issueBId });

        Assert.Equal("DC Comics", vm.MainFields.Single(f => f.Label == "Publisher").Value);
        Assert.Equal(string.Empty, vm.MainFields.Single(f => f.Label == "Title").Value);
        Assert.False(vm.MainFields.Single(f => f.Label == "Publisher").IsStaged);
    }

    [Fact]
    public void Load_ListField_ShowsIntersectionOfAllSelectedIssues()
    {
        var vm = CreateViewModel();

        vm.Load(new[] { _issueAId, _issueBId });

        var writerField = vm.ArtistFields.Single(f => f.Label == "Writer");
        Assert.Equal("Alan Moore", writerField.Value);
        Assert.False(writerField.IsStaged);
    }

    [Fact]
    public void Save_UnstagedField_LeavesEveryIssueValueUntouched()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });

        vm.SaveCommand.Execute(null);

        Assert.Equal("Watchmen #1", GetIssue(_issueAId).Title);
        Assert.Equal("Watchmen #2", GetIssue(_issueBId).Title);
    }

    [Fact]
    public void Save_StagedScalarField_OverwritesIdenticallyOnEveryIssue()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });
        vm.MainFields.Single(f => f.Label == "Publisher").Value = "Vertigo";

        vm.SaveCommand.Execute(null);

        Assert.Equal("Vertigo", GetIssue(_issueAId).Publisher);
        Assert.Equal("Vertigo", GetIssue(_issueBId).Publisher);
    }

    [Fact]
    public void Save_StagedListField_AddingAToken_UnionsIntoEveryIssue_PreservingOwnMembers()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });
        vm.ArtistFields.Single(f => f.Label == "Writer").Value = "Alan Moore, Neil Gaiman";

        vm.SaveCommand.Execute(null);

        var tokensA = ListFieldTokens.Parse(GetIssue(_issueAId).Writer);
        var tokensB = ListFieldTokens.Parse(GetIssue(_issueBId).Writer);
        Assert.Equal(new[] { "Alan Moore", "Dave Gibbons", "Neil Gaiman" }.ToHashSet(StringComparer.OrdinalIgnoreCase), tokensA);
        Assert.Equal(new[] { "Alan Moore", "Brian Bolland", "Neil Gaiman" }.ToHashSet(StringComparer.OrdinalIgnoreCase), tokensB);
    }

    [Fact]
    public void Save_StagedListField_RemovingTheSharedToken_PreservesEachIssuesOwnUntouchedMembers()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });
        vm.ArtistFields.Single(f => f.Label == "Writer").Value = string.Empty; // removes "Alan Moore" (the whole shown intersection), adds nothing

        vm.SaveCommand.Execute(null);

        Assert.Equal(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dave Gibbons" }, ListFieldTokens.Parse(GetIssue(_issueAId).Writer));
        Assert.Equal(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Brian Bolland" }, ListFieldTokens.Parse(GetIssue(_issueBId).Writer));
    }

    [Fact]
    public void Cancel_NeverTouchesDatabase_AndGoesBack()
    {
        bool wentBack = false;
        var vm = CreateViewModel(() => wentBack = true);
        vm.Load(new[] { _issueAId, _issueBId });
        vm.MainFields.Single(f => f.Label == "Publisher").Value = "Never Saved";

        vm.CancelCommand.Execute(null);

        Assert.True(wentBack);
        Assert.Equal("DC Comics", GetIssue(_issueAId).Publisher);
    }

    /// <summary>
    /// P6 follow-up (docs/alpha-todo.md): <see cref="MainViewModel.TryLeaveCurrentEditor"/> queries
    /// this to decide whether to prompt before navigating away from an in-progress bulk edit.
    /// </summary>
    [Fact]
    public void HasUnsavedChanges_FalseImmediatelyAfterLoad()
    {
        var vm = CreateViewModel();

        vm.Load(new[] { _issueAId, _issueBId });

        Assert.False(vm.HasUnsavedChanges());
    }

    [Fact]
    public void HasUnsavedChanges_TrueAfterEditingAField()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });

        vm.MainFields.Single(f => f.Label == "Publisher").Value = "Vertigo";

        Assert.True(vm.HasUnsavedChanges());
    }

    [Fact]
    public void HasUnsavedChanges_FalseAfterSave()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });
        vm.MainFields.Single(f => f.Label == "Publisher").Value = "Vertigo";

        vm.SaveCommand.Execute(null);

        Assert.False(vm.HasUnsavedChanges());
    }

    // ===================== Content Type / Reading Direction (docs/superpowers/specs/
    // 2026-08-16-manga-content-type-classification-design.md) =====================

    [Fact]
    public void ContentType_Unstaged_NeverTouchesAnySeries()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });

        vm.SaveCommand.Execute(null);

        // Series.ContentType's own property initializer is Unknown, not the enum's raw default
        // (Comic, first-declared) - confirms an unstaged field genuinely never touches the row.
        Assert.Equal(ContentType.Unknown, GetSeries(_seriesOneId).ContentType);
    }

    [Fact]
    public void ContentType_Staged_SingleSeriesSelection_WritesThroughToTheOwningSeries()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });
        vm.MainFields.Single(f => f.Label == BulkFieldRegistry.ContentTypeLabel).Value = nameof(ContentType.Manga);

        vm.SaveCommand.Execute(null);

        Assert.Equal(ContentType.Manga, GetSeries(_seriesOneId).ContentType);
    }

    [Fact]
    public void ContentType_Staged_MultiSeriesSelection_WritesThroughToEveryDistinctSeries()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueCId }); // spans seriesOne and seriesTwo
        vm.MainFields.Single(f => f.Label == BulkFieldRegistry.ContentTypeLabel).Value = nameof(ContentType.Manhwa);

        vm.SaveCommand.Execute(null);

        Assert.Equal(ContentType.Manhwa, GetSeries(_seriesOneId).ContentType);
        Assert.Equal(ContentType.Manhwa, GetSeries(_seriesTwoId).ContentType);
    }

    [Theory]
    [InlineData(nameof(ContentType.Comic), false)]
    [InlineData(nameof(ContentType.Manga), true)]
    [InlineData(nameof(ContentType.Manhua), true)]
    [InlineData(nameof(ContentType.Manhwa), true)]
    public void ShowReadingModePicker_TracksContentTypeFieldsLiveValue(string contentType, bool expectedVisible)
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });

        vm.MainFields.Single(f => f.Label == BulkFieldRegistry.ContentTypeLabel).Value = contentType;

        Assert.Equal(expectedVisible, vm.ShowReadingModePicker);
    }

    [Fact]
    public void ReadingDirection_DefaultsToRightToLeft_OnFirstTransitionIntoMangaFamily()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });

        vm.MainFields.Single(f => f.Label == BulkFieldRegistry.ContentTypeLabel).Value = nameof(ContentType.Manga);

        Assert.Equal(nameof(ReadingMode.RightToLeft), vm.MainFields.Single(f => f.Label == "Reading Direction").Value);
    }

    [Fact]
    public void ReadingDirection_StagedAndShown_WritesThroughToTheOwningSeries()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });
        vm.MainFields.Single(f => f.Label == BulkFieldRegistry.ContentTypeLabel).Value = nameof(ContentType.Manga);
        vm.MainFields.Single(f => f.Label == "Reading Direction").Value = nameof(ReadingMode.LeftToRight);

        vm.SaveCommand.Execute(null);

        Assert.Equal(ReadingMode.LeftToRight, GetSeries(_seriesOneId).ReadingMode);
    }

    // ===================== Status (docs/superpowers/specs/2026-08-18-metadata-model-ui-gaps-status-and-bookmarks-design.md) =====================

    [Fact]
    public void Status_Unstaged_NeverTouchesAnySeries()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });

        vm.SaveCommand.Execute(null);

        Assert.Equal(SeriesStatus.Unknown, GetSeries(_seriesOneId).Status);
    }

    [Fact]
    public void Status_Staged_SingleSeriesSelection_WritesThroughToTheOwningSeries()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });
        vm.MainFields.Single(f => f.Label == "Status").Value = nameof(SeriesStatus.Completed);

        vm.SaveCommand.Execute(null);

        Assert.Equal(SeriesStatus.Completed, GetSeries(_seriesOneId).Status);
    }

    [Fact]
    public void Status_Staged_MultiSeriesSelection_WritesThroughToEveryDistinctSeries()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueCId }); // spans seriesOne and seriesTwo
        vm.MainFields.Single(f => f.Label == "Status").Value = nameof(SeriesStatus.Ongoing);

        vm.SaveCommand.Execute(null);

        Assert.Equal(SeriesStatus.Ongoing, GetSeries(_seriesOneId).Status);
        Assert.Equal(SeriesStatus.Ongoing, GetSeries(_seriesTwoId).Status);
    }

    [Fact]
    public void ReadingStatus_Staged_SingleSeriesSelection_WritesThroughToTheOwningSeries()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });
        vm.MainFields.Single(f => f.Label == "Reading Status").Value = nameof(ReadingStatus.Dropped);

        vm.SaveCommand.Execute(null);

        Assert.Equal(ReadingStatus.Dropped, GetSeries(_seriesOneId).ReadingStatus);
    }

    [Fact]
    public void ReadingStatus_Staged_MultiSeriesSelection_WritesThroughToEveryDistinctSeries()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueCId }); // spans seriesOne and seriesTwo
        vm.MainFields.Single(f => f.Label == "Reading Status").Value = nameof(ReadingStatus.Reading);

        vm.SaveCommand.Execute(null);

        Assert.Equal(ReadingStatus.Reading, GetSeries(_seriesOneId).ReadingStatus);
        Assert.Equal(ReadingStatus.Reading, GetSeries(_seriesTwoId).ReadingStatus);
    }

    [Fact]
    public void SeriesAffectedCount_ReflectsDistinctSeriesInSelection()
    {
        var vm = CreateViewModel();

        vm.Load(new[] { _issueAId, _issueCId });

        Assert.Equal(2, vm.SeriesAffectedCount);
    }

    [Fact]
    public void HasSeriesAffected_TracksContentTypeFieldsStagedState()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });
        Assert.False(vm.HasSeriesAffected);

        vm.MainFields.Single(f => f.Label == BulkFieldRegistry.ContentTypeLabel).Value = nameof(ContentType.Manga);

        Assert.True(vm.HasSeriesAffected);
    }
}
