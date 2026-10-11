using System;
using System.IO;
using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Library's "Continue reading" strip (docs/superpowers/specs/2026-10-04-library-redesign-design.md, Slice 4): which series
/// it lists, in what order, and that it only heads the plain whole-library view. View-model level.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryContinueStripTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public LibraryContinueStripTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_library_strip_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
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

    /// <summary>A three-issue series. <paramref name="midReadHoursAgo"/> null leaves it untouched; otherwise issue 2 is half-read and was opened that long ago.</summary>
    private static void Seed(string name, double? midReadHoursAgo, ReadingStatus status = default, ContentType type = ContentType.Comic)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, ReadingStatus = status, ContentType = type };
        context.Series.Add(series);
        context.SaveChanges();
        for (int n = 1; n <= 3; n++)
        {
            var issue = new Issue { SeriesId = series.Id, Number = n.ToString(), PageCount = 20, AddedTime = DateTime.UtcNow };
            if (n == 2 && midReadHoursAgo is double hours)
            {
                issue.LastPageRead = 8;
                issue.OpenedTime = DateTime.UtcNow.AddHours(-hours);
            }

            context.Issues.Add(issue);
        }

        context.SaveChanges();
    }

    private static LibraryScreenViewModel CreateVm()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });
        vm.SetGranularityCommand.Execute(LibraryContentGranularity.Series);
        return vm;
    }

    [Fact]
    public void Strip_ListsSeriesWithAnIssueInProgress_MostRecentlyOpenedFirst()
    {
        Seed("Older", midReadHoursAgo: 48);
        Seed("Untouched", midReadHoursAgo: null);
        Seed("Newer", midReadHoursAgo: 1);

        var vm = CreateVm();

        Assert.Equal(new[] { "Newer", "Older" }, vm.ContinueStrip.Select(c => c.Name));
        Assert.True(vm.ShowContinueStrip);
        Assert.Equal("Issue 2 of 3 · p. 9", vm.ContinueStrip[0].ContinueLabel);
    }

    [Fact]
    public void Strip_LeavesOutDroppedSeries_AndStopsAtTheLimit()
    {
        Seed("Dropped", midReadHoursAgo: 1, status: ReadingStatus.Dropped);
        for (int n = 1; n <= LibraryScreenViewModel.ContinueStripLimit + 2; n++)
        {
            Seed($"Series {n:00}", midReadHoursAgo: n + 1);
        }

        var vm = CreateVm();

        Assert.Equal(LibraryScreenViewModel.ContinueStripLimit, vm.ContinueStrip.Count);
        Assert.DoesNotContain(vm.ContinueStrip, c => c.Name == "Dropped");
        Assert.Equal("Series 01", vm.ContinueStrip[0].Name);
    }

    [Fact]
    public void Strip_IsHidden_WhenNothingIsInProgress()
    {
        Seed("Untouched", midReadHoursAgo: null);

        var vm = CreateVm();

        Assert.Empty(vm.ContinueStrip);
        Assert.False(vm.ShowContinueStrip);
    }

    [Fact]
    public void Strip_OnlyHeadsThePlainWholeLibraryView()
    {
        Seed("Midway", midReadHoursAgo: 1);
        Seed("Manga One", midReadHoursAgo: null, type: ContentType.Manga);
        var vm = CreateVm();
        Assert.True(vm.ShowContinueStrip);

        vm.SetLensCommand.Execute(LibraryLens.Reading);
        Assert.False(vm.ShowContinueStrip);
        vm.SetLensCommand.Execute(LibraryLens.All);
        Assert.True(vm.ShowContinueStrip);

        vm.SearchQuery = "mid";
        Assert.False(vm.ShowContinueStrip);
        vm.SearchQuery = string.Empty;
        Assert.True(vm.ShowContinueStrip);

        vm.FilterMissingIssues = true;
        Assert.False(vm.ShowContinueStrip);
        vm.FilterMissingIssues = false;

        vm.PublisherFilter = "DC";
        Assert.False(vm.ShowContinueStrip);
        vm.PublisherFilter = null;

        vm.SetContentTypeFilterCommand.Execute(vm.ContentTypes.Single(c => c.ContentType == ContentType.Manga));
        Assert.False(vm.ShowContinueStrip);
        vm.ClearChipsCommand.Execute(null);
        Assert.True(vm.ShowContinueStrip);
    }

    [Fact]
    public void FocusingAStripCard_PointsTheInspectorAtThatSeries()
    {
        Seed("Other", midReadHoursAgo: null);
        Seed("Midway", midReadHoursAgo: 1);
        var vm = CreateVm();

        vm.PreviewContinueCard(vm.ContinueStrip.Single());

        Assert.Equal("Midway", vm.PreviewSeries!.Name);
        Assert.True(vm.ShowSeriesPreview);
        Assert.Equal("▶ Continue #2 · p. 9", vm.PreviewSeriesPrimaryLabel);
    }

    [Fact]
    public void ContinueCommand_OpensTheInProgressIssue()
    {
        Seed("Midway", midReadHoursAgo: 1);
        int? opened = null;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: id => opened = id, goToNewIssueProperties: (_, _, _) => { });
        var card = vm.ContinueStrip.Single();

        vm.ContinueReadingCommand.Execute(card);

        Assert.Equal(card.ContinueReadingIssueId, opened);
        Assert.Equal("2", card.ContinueReadingNumber);
    }
}
