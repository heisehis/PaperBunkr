using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PublisherCoverage"/> and the Library Health <see cref="PublisherGapsViewModel"/>
/// (docs/superpowers/specs/2026-10-04-publisher-icons-user-folder-and-gaps-design.md).
/// </summary>
public class PublisherCoverageTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;

    public PublisherCoverageTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pubcov_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static int SeedSeries(string name, string? seriesPublisher, params string?[] issuePublishers)
    {
        using var ctx = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, Publisher = seriesPublisher };
        ctx.Series.Add(series);
        ctx.SaveChanges();
        foreach (string? publisher in issuePublishers)
        {
            ctx.Issues.Add(new Issue { SeriesId = series.Id, Publisher = publisher });
        }

        ctx.SaveChanges();
        return series.Id;
    }

    private static bool OnlyDcHasALogo(string name) => name.Equals("DC Comics", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Scan_CountsPublishers_AndListsTheOnesWithoutALogo_MostIssuesFirst()
    {
        SeedSeries("Batman", null, "DC Comics", "DC Comics");
        SeedSeries("Cthulhu", null, "Obscure Press", "Obscure Press", "Obscure Press");
        SeedSeries("Tiny", null, "Small House");
        using var ctx = PaperbunkrDb.CreateContext();

        var report = PublisherCoverage.Scan(ctx, OnlyDcHasALogo);

        Assert.Equal(3, report.PublishersTotal);
        Assert.Equal(1, report.PublishersWithLogo);
        Assert.Equal(new[] { "Obscure Press", "Small House" }, report.WithoutLogo.Select(p => p.Name));
        Assert.Equal(3, report.WithoutLogo[0].Issues);
    }

    [Fact]
    public void Scan_TreatsADifferentlyCasedPublisherAsOne_AndBlankAsMissing()
    {
        SeedSeries("A", null, "DC Comics", "dc comics", "   ", null);
        using var ctx = PaperbunkrDb.CreateContext();

        var report = PublisherCoverage.Scan(ctx, OnlyDcHasALogo);

        Assert.Equal(1, report.PublishersTotal);
        Assert.Equal(2, report.IssuesWithoutPublisher);
    }

    [Fact]
    public void Scan_FallsBackToTheSeriesPublisher_SoThoseIssuesAreNotMissing()
    {
        SeedSeries("Migrated", "Image Comics", null, null);
        using var ctx = PaperbunkrDb.CreateContext();

        var report = PublisherCoverage.Scan(ctx, _ => true);

        Assert.Equal(0, report.IssuesWithoutPublisher);
        Assert.Equal(1, report.PublishersTotal);
    }

    [Fact]
    public void Scan_SuggestsTheMostCommonPublisherInTheSeries_AndNothingWhenTied()
    {
        int majority = SeedSeries("Mostly DC", null, "DC Comics", "DC Comics", "Vertigo", null, null);
        int tied = SeedSeries("Split", null, "DC Comics", "Marvel", null);
        int none = SeedSeries("Web scan", null, null, null, null);
        using var ctx = PaperbunkrDb.CreateContext();

        var report = PublisherCoverage.Scan(ctx, _ => true);

        Assert.Equal("DC Comics", report.SeriesMissing.Single(s => s.SeriesId == majority).Suggested);
        Assert.Equal(2, report.SeriesMissing.Single(s => s.SeriesId == majority).IssuesMissing);
        Assert.Null(report.SeriesMissing.Single(s => s.SeriesId == tied).Suggested);
        Assert.Null(report.SeriesMissing.Single(s => s.SeriesId == none).Suggested);
        Assert.Equal(2, report.FillableIssues); // only the unambiguous series counts
        Assert.Equal(6, report.IssuesWithoutPublisher);
        Assert.Equal(majority, report.SeriesMissing[0].SeriesId); // fillable series first
    }

    [Fact]
    public void FillFromSiblings_SetsOnlyTheBlanksOfUnambiguousSeries_AndLeavesTheRestAlone()
    {
        int majority = SeedSeries("Mostly DC", null, "DC Comics", "DC Comics", null, "  ");
        int tied = SeedSeries("Split", null, "DC Comics", "Marvel", null);
        int none = SeedSeries("Web scan", null, null, null);

        using var ctx = PaperbunkrDb.CreateContext();
        int updated = PublisherCoverage.FillFromSiblings(ctx);

        Assert.Equal(2, updated);
        using var check = PaperbunkrDb.CreateContext();
        Assert.All(check.Issues.Where(i => i.SeriesId == majority).ToList(), i => Assert.Equal("DC Comics", i.Publisher));
        Assert.Equal(1, check.Issues.Count(i => i.SeriesId == tied && i.Publisher == null)); // ambiguous: untouched
        Assert.Equal(2, check.Issues.Count(i => i.SeriesId == none && i.Publisher == null)); // nothing to copy from: untouched
        Assert.Equal(1, check.Issues.Count(i => i.SeriesId == tied && i.Publisher == "Marvel")); // never overwritten
    }

    [Fact]
    public void FillFromSiblings_PointsTheFilledIssuesAtThePublisherRow_AndIsRepeatable()
    {
        int series = SeedSeries("Mostly DC", null, "DC Comics", null);
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            Assert.Equal(1, PublisherCoverage.FillFromSiblings(ctx));
        }

        using var check = PaperbunkrDb.CreateContext();
        var filled = check.Issues.Single(i => i.SeriesId == series && i.Publisher == "DC Comics" && i.PublisherEntityId != null);
        Assert.NotNull(filled.PublisherEntityId);
        Assert.Equal(0, PublisherCoverage.FillFromSiblings(check)); // nothing left to fill
    }

    [Fact]
    public async Task ViewModel_ScanFillsItsLists_AndFillIsGatedOnConfirmation()
    {
        SeedSeries("Mostly DC", null, "DC Comics", "DC Comics", null);
        SeedSeries("Obscure", null, "Obscure Press");
        var declined = new PublisherGapsViewModel(dialogs: new FakeDialogs(false), hasLogo: OnlyDcHasALogo);
        Assert.False(declined.IsScanned);
        Assert.Equal("· Not scanned yet", declined.HeaderDetail);

        declined.ScanCommand.Execute(null);

        Assert.True(declined.IsScanned);
        Assert.Equal("Obscure Press", declined.WithoutLogo.Single().Name);
        Assert.Equal(1, declined.IssuesWithoutPublisher);
        Assert.True(declined.HasFillable);

        await declined.FillFromSiblingsCommand.ExecuteAsync(null);
        Assert.Equal(1, declined.IssuesWithoutPublisher); // declined: nothing changed

        var accepted = new PublisherGapsViewModel(dialogs: new FakeDialogs(true), hasLogo: OnlyDcHasALogo);
        accepted.ScanCommand.Execute(null);
        await accepted.FillFromSiblingsCommand.ExecuteAsync(null);

        Assert.Equal(0, accepted.IssuesWithoutPublisher);
        Assert.False(accepted.HasFillable);
        Assert.Equal("Set the publisher on 1 issue.", accepted.StatusMessage);
    }

    [Fact]
    public void ViewModel_ReloadIcons_RereadsTheFolderAndRescans()
    {
        SeedSeries("Obscure", null, "Obscure Press");
        bool reloaded = false;
        bool logoNow = false;
        var vm = new PublisherGapsViewModel(
            hasLogo: name => logoNow && name == "Obscure Press",
            reloadIcons: () => { reloaded = true; logoNow = true; },
            userIconCount: () => logoNow ? 1 : 0,
            iconsFolder: () => @"C:\icons");
        vm.ScanCommand.Execute(null);
        Assert.Single(vm.WithoutLogo);
        Assert.Equal("No icons added yet", vm.UserIconsSummary);

        vm.ReloadIconsCommand.Execute(null);

        Assert.True(reloaded);
        Assert.Empty(vm.WithoutLogo); // the added logo is picked up on the rescan
        Assert.Equal("1 icon in your folder", vm.UserIconsSummary);
    }

    [Fact]
    public void ViewModel_OpenIconsFolder_OpensTheUserFolder()
    {
        string? opened = null;
        var vm = new PublisherGapsViewModel(openFolder: p => opened = p, iconsFolder: () => @"C:\icons");

        vm.OpenIconsFolderCommand.Execute(null);

        Assert.Equal(@"C:\icons", opened);
    }

    private sealed class FakeDialogs : IDialogService
    {
        private readonly bool _confirm;

        public FakeDialogs(bool confirm) => _confirm = confirm;

        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(_confirm ? 0 : 1);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(_confirm);
    }
}
