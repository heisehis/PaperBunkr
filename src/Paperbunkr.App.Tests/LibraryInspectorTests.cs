using System;
using System.IO;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Library inspector's series state (docs/superpowers/specs/2026-10-04-library-redesign-design.md, Slice 3): which issue
/// Continue resumes, the issue strip's chips, and where the synopsis comes from. View-model level, same temp-database isolation
/// as <see cref="LibraryPreviewPanelViewModelTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryInspectorTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public LibraryInspectorTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_library_inspector_test_{Guid.NewGuid():N}.db");
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

    private static int AddSeries(string name, string? summary, params Action<Issue>[] issues)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, Summary = summary };
        context.Series.Add(series);
        context.SaveChanges();

        for (int i = 0; i < issues.Length; i++)
        {
            var issue = new Issue { SeriesId = series.Id, Number = (i + 1).ToString(), PageCount = 20, AddedTime = DateTime.UtcNow };
            issues[i](issue);
            context.Issues.Add(issue);
        }

        context.SaveChanges();
        return series.Id;
    }

    private static readonly Action<Issue> Unread = _ => { };
    private static readonly Action<Issue> Read = i => i.LastPageRead = 19;

    private static Action<Issue> MidRead(int lastPage, DateTime opened) => i => { i.LastPageRead = lastPage; i.OpenedTime = opened; };

    private static (LibraryScreenViewModel Vm, SeriesCardSample Card) Preview(int? reader = null, Action<int>? onRead = null)
    {
        var vm = new LibraryScreenViewModel(
            goDetail: _ => { },
            goReaderForIssue: id => onRead?.Invoke(id),
            goToNewIssueProperties: (_, _, _) => { });
        vm.SetGranularityCommand.Execute(LibraryContentGranularity.Series);
        var card = vm.Covers.Single();
        vm.PreviewSeries = card;
        return (vm, card);
    }

    [Fact]
    public void Continue_ResumesTheMostRecentlyOpenedInProgressIssue_NotTheFirstUnread()
    {
        var now = DateTime.UtcNow;
        AddSeries("Saga", null, Read, MidRead(4, now.AddDays(-3)), Unread, MidRead(8, now.AddHours(-1)), Unread);
        int? opened = null;

        var (vm, card) = Preview(onRead: id => opened = id);

        Assert.Equal("4", card.ContinueReadingNumber);
        Assert.Equal(9, card.ContinueReadingPage);
        Assert.Equal("▶ Continue #4 · p. 9", vm.PreviewSeriesPrimaryLabel);

        vm.PreviewSeriesPrimaryCommand.Execute(null);
        Assert.Equal(card.ContinueReadingIssueId, opened);
    }

    [Fact]
    public void Continue_FallsBackToTheFirstUnreadIssue_WhenNothingIsMidRead()
    {
        AddSeries("Saga", null, Read, Read, Unread, Unread);

        var (vm, card) = Preview();

        Assert.Equal("3", card.ContinueReadingNumber);
        Assert.Null(card.ContinueReadingPage);
        Assert.Equal("▶ Continue #3", vm.PreviewSeriesPrimaryLabel);
    }

    [Fact]
    public void PrimaryLabel_IsPlainRead_ForAnUntouchedSeries_AndForAFinishedOne()
    {
        AddSeries("Fresh", null, Unread, Unread);
        Assert.Equal("▶ Read", Preview().Vm.PreviewSeriesPrimaryLabel);
    }

    [Fact]
    public void IssueChips_ShowReadNextAndMissing_InNumberOrder()
    {
        AddSeries("Saga", null, Read, MidRead(8, DateTime.UtcNow), Unread, i => i.FileIsMissing = true);

        var (vm, _) = Preview();
        var chips = vm.PreviewSeriesIssueChips;

        Assert.Equal(new[] { "1", "2", "3", "4" }, chips.Select(c => c.Label));
        Assert.True(chips[0].IsRead);
        Assert.Equal("Read", chips[0].StateLabel);
        Assert.True(chips[1].IsNext);
        Assert.Equal("Next to read, in progress", chips[1].StateLabel);
        Assert.Equal("Unread", chips[2].StateLabel);
        Assert.True(chips[3].IsMissing);
        Assert.Equal("File missing", chips[3].StateLabel);
        Assert.Equal("Issue 4, File missing", chips[3].AccessibleName);
        Assert.Single(chips, c => c.IsNext);
        Assert.Equal("1 file missing", vm.PreviewIssueChipsCaption);
        Assert.True(vm.HasPreviewIssueChipsCaption);
    }

    [Fact]
    public void IssueChips_CoverTheWholeSeries_EvenUnderALensThatHidesSomeIssues()
    {
        AddSeries("Saga", null, Read, Unread, Unread);

        var (vm, _) = Preview();
        vm.SetLensCommand.Execute(LibraryLens.Reading);
        vm.PreviewSeries = vm.Covers.Single();

        Assert.Equal(3, vm.PreviewSeriesIssueChips.Count);
        Assert.False(vm.HasPreviewIssueChipsCaption);
    }

    [Fact]
    public void IssueChips_StopAtTheLimit_AndSaySo()
    {
        AddSeries("Long", null, Enumerable.Repeat(Unread, LibraryScreenViewModel.PreviewIssueChipLimit + 5).ToArray());

        var (vm, _) = Preview();

        Assert.Equal(LibraryScreenViewModel.PreviewIssueChipLimit, vm.PreviewSeriesIssueChips.Count);
        Assert.Equal($"first {LibraryScreenViewModel.PreviewIssueChipLimit} of {LibraryScreenViewModel.PreviewIssueChipLimit + 5}", vm.PreviewIssueChipsCaption);
    }

    [Fact]
    public void IssueChip_Click_DrillsIntoThatIssue()
    {
        AddSeries("Saga", null, Read, Unread);

        var (vm, _) = Preview();
        vm.DrillIntoIssueCommand.Execute(vm.PreviewSeriesIssueChips[1].Row);

        Assert.True(vm.ShowIssuePreview);
        Assert.Equal("2", vm.ActivePreviewIssue!.Number);
    }

    [Fact]
    public void Synopsis_PrefersTheSeriesSummary_AndFallsBackToTheCoverIssues()
    {
        AddSeries("Saga", "Two soldiers from warring worlds.", i => i.Summary = "Issue one summary.");
        Assert.Equal("Two soldiers from warring worlds.", Preview().Card.SynopsisExcerpt);
    }

    [Fact]
    public void Synopsis_UsesTheCoverIssuesSummary_WhenTheSeriesHasNone()
    {
        AddSeries("Saga", null, i => i.Summary = "Issue one summary.");

        var card = Preview().Card;

        Assert.True(card.HasSynopsis);
        Assert.Equal("Issue one summary.", card.SynopsisExcerpt);
    }
}
