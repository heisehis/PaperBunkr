using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The reader's info panel (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #28): what it shows, the collapsed summary, its key and its callbacks.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderInfoPanelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_info_panel_test_{Guid.NewGuid():N}.db");
    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_info_panel_test_{Guid.NewGuid():N}.cbz");
    private readonly int _issueId;
    private readonly int _seriesId;

    public ReaderInfoPanelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        CbzFixture.Create(_cbzPath, pageCount: 3);
        var series = new Series { Name = "Absolute Universe" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue
        {
            SeriesId = series.Id,
            Number = "3",
            Count = 12,
            Title = "Part Three",
            Summary = "The hero dies in the last panel.",
            Writer = "Ann Author; Bo Writer",
            Penciller = "Cy Artist",
            Characters = "Nova, Rook, Nova",
            Teams = "The Guard",
            Locations = "",
            StoryArc = "Fall",
            StoryArcNumber = "3",
            Publisher = "Big Two",
            Year = 2024,
            Rating = 4.5f,
            AgeRating = "Teen",
            FilePath = _cbzPath,
        };
        context.Issues.Add(issue);
        context.SaveChanges();
        context.IssueTags.Add(new IssueTag { IssueId = issue.Id, Field = IssueTagField.Genre, Value = "Superhero" });
        context.IssueTags.Add(new IssueTag { IssueId = issue.Id, Field = IssueTagField.Tags, Value = "event" });
        context.SaveChanges();
        _issueId = issue.Id;
        _seriesId = series.Id;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string path in new[] { _dbPath, _cbzPath })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    private static Issue Sample(Action<Issue>? tweak = null)
    {
        var issue = new Issue { Number = "3", Count = 12, Title = " Part Three ", Writer = "Ann Author; Bo Writer, ann author", Characters = "Nova, Rook, Nova" };
        tweak?.Invoke(issue);
        return issue;
    }

    // ===== The builder =====

    [Fact]
    public void Builder_MakesTheSeriesLine_CreditsAndDistinctNames()
    {
        var model = ReaderInfoBuilder.Build(Sample(), "Series", null);

        Assert.Equal("Series #3 of 12", model.SeriesLine);
        Assert.Equal("Part Three", model.Title);
        var writer = Assert.Single(model.Credits);
        Assert.Equal("Writer", writer.Role);
        Assert.Equal("Ann Author, Bo Writer", writer.Names);       // split, trimmed, the repeated name dropped
        Assert.Equal(new[] { "Nova", "Rook" }, model.Characters);
        Assert.True(model.HasCredits);
        Assert.False(model.HasTeams);
    }

    [Fact]
    public void Builder_LeavesOutEverythingThatIsAbsent()
    {
        var model = ReaderInfoBuilder.Build(new Issue(), "Series", null);

        Assert.Equal("Series", model.SeriesLine);
        Assert.Null(model.Title);
        Assert.Null(model.Summary);
        Assert.Empty(model.Credits);
        Assert.Null(model.StoryArc);
        Assert.Null(model.ContextLine);
        Assert.Null(model.RatingLine);
        Assert.Null(model.PublisherLine);
        Assert.False(model.HasSummary);
        Assert.False(model.HasPeopleAndPlaces);
    }

    [Fact]
    public void Builder_ArcContextRatingAndPublisherLines()
    {
        var issue = Sample(i => { i.StoryArc = "Fall"; i.StoryArcNumber = "3"; i.Rating = 4.5f; i.AgeRating = "Teen"; i.Publisher = "Big Two"; i.Imprint = "Vertigo"; i.Year = 2024; });
        var context = new ReadingContext(ReadingContextKind.ReadingList, "Absolute Universe", 3, 12, 10, 12);

        var model = ReaderInfoBuilder.Build(issue, "Series", context);

        Assert.Equal("Fall (part 3)", model.StoryArc);
        Assert.Equal("Absolute Universe · 3 of 12", model.ContextLine);
        Assert.Equal("★ 4.5 · Teen", model.RatingLine);
        Assert.Equal("Big Two · Vertigo · 2024", model.PublisherLine);
    }

    // ===== The view model =====

    [Fact]
    public void Load_FillsThePanel_ClosedAndWithTheSummaryCollapsed()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issueId);

        var info = vm.Info;
        Assert.False(info.IsOpen);
        Assert.NotNull(info.Model);
        Assert.Equal("Absolute Universe #3 of 12", info.Model!.SeriesLine);
        Assert.Equal(new[] { "Superhero" }, info.Model.Genres);
        Assert.Equal(new[] { "event" }, info.Model.Tags);
        Assert.True(info.Model.HasSummary);
        Assert.False(info.IsSummaryExpanded);                      // a summary can spoil
        Assert.True(info.IsSummaryCollapsed);

        info.ShowSummaryCommand.Execute(null);
        Assert.True(info.IsSummaryExpanded);
        Assert.False(info.IsSummaryCollapsed);
    }

    [Fact]
    public void ThePreferencesDefault_ShowsTheSummaryStraightAway()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.GetOrCreateAppSettings().InfoPanelShowSummary = true;
            context.SaveChanges();
        }

        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issueId);

        Assert.True(vm.Info.IsSummaryExpanded);
    }

    [Fact]
    public void ToggleInfoPanel_OpensAndCloses_AndAnotherIssueOpensWithItClosed()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issueId);

        vm.ToggleInfoPanelCommand.Execute(null);
        Assert.True(vm.Info.IsOpen);
        vm.ToggleInfoPanelCommand.Execute(null);
        Assert.False(vm.Info.IsOpen);

        vm.ToggleInfoPanelCommand.Execute(null);
        vm.LoadIssue(_issueId);
        Assert.False(vm.Info.IsOpen);
    }

    [Fact]
    public void OpenDetailsAndEditProperties_RaiseTheirEvents_AndCloseThePanel()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issueId);
        int? series = null, issue = null;
        vm.OpenDetailsRequested += id => series = id;
        vm.EditPropertiesRequested += id => issue = id;

        vm.ToggleInfoPanelCommand.Execute(null);
        vm.OpenInfoDetailsCommand.Execute(null);
        Assert.Equal(_seriesId, series);
        Assert.False(vm.Info.IsOpen);

        vm.EditInfoPropertiesCommand.Execute(null);
        Assert.Equal(_issueId, issue);
    }

    [Fact]
    public void ThePaletteAndTheKeyRegistry_KnowThePanel()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issueId);

        Assert.Contains(vm.BuildPaletteEntries(), e => e.Title.StartsWith("Info panel"));
        Assert.Contains(vm.ExtraKeyBindings, b => b.Gestures.Any(g => g.Key == Key.I));
    }

    // ===== On screen =====

    [Fact]
    public void PressingI_TogglesThePanel_OnTheRealScreen()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var (window, _, canvas) = ReaderScreenTestHost.Open(vm, _issueId);
        try
        {
            ReaderScreenTestHost.Press(window, Key.I);
            TestDispatcher.Drain();
            Assert.True(vm.Info.IsOpen);

            ReaderScreenTestHost.Press(window, Key.I);
            TestDispatcher.Drain();
            Assert.False(vm.Info.IsOpen);
        }
        finally
        {
            window.Close();
        }
    }
}
