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

    public BulkIssuePropertiesScreenViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bulkissuepropsvm_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
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
        context.Issues.AddRange(issueA, issueB);
        context.SaveChanges();
        _issueAId = issueA.Id;
        _issueBId = issueB.Id;
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
}
