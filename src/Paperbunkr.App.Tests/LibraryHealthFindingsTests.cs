using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views.Preferences;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Library Health's "Collection gaps" and "Metadata consistency" sections (docs/superpowers/specs/2026-10-06-smart-features-design.md
/// §3.3, §3.4): scan, dismiss, restore, and the row templates built for real from <c>LibrarySection.axaml</c>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryHealthFindingsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_health_findings_{Guid.NewGuid():N}.db");

    public LibraryHealthFindingsTests()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
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

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private int AddSeries(string name, params Issue[] issues)
    {
        using var context = CreateContext();
        var series = new Series { Name = name, Issues = issues.ToList() };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private static Issue Owned(int number, int? year = null, string? publisher = null) =>
        new() { Number = number.ToString(), Year = year, Publisher = publisher, FilePath = $"C:\\x\\{Guid.NewGuid():N}.cbz" };

    // Row removals are deferred a dispatcher tick in the app; the tests run them at once.
    private static void Now(Action action) => action();

    // ----- Collection gaps -----

    [Fact]
    public void Gaps_ScanOnFirstOpen_ListsSeriesWithHoles_AndSummarisesTheHeader()
    {
        AddSeries("Saga", Owned(1), Owned(2), Owned(4));
        AddSeries("Complete", Owned(1), Owned(2));
        var vm = new CollectionGapsViewModel(CreateContext, Now);
        Assert.Equal("· Not scanned yet", vm.HeaderDetail);

        vm.EnsureScanned();

        var row = Assert.Single(vm.Rows);
        Assert.Equal("Saga", row.SeriesName);
        Assert.Equal("#3", row.Missing);
        Assert.Equal("· 1 series", vm.HeaderDetail);
        Assert.True(vm.HasRows);
        Assert.False(vm.IsClean);
    }

    [Fact]
    public void Gaps_NotAGap_MovesTheRowToDismissed_AndItStaysGoneOnRescan_UntilRestored()
    {
        AddSeries("Saga", Owned(1), Owned(2), Owned(4));
        var vm = new CollectionGapsViewModel(CreateContext, Now);
        vm.EnsureScanned();

        vm.NotAGapCommand.Execute(vm.Rows[0]);

        Assert.Empty(vm.Rows);
        Assert.Equal("Saga - missing #3", Assert.Single(vm.Dismissed).Label);
        Assert.True(vm.IsClean);
        Assert.Equal("· No gaps", vm.HeaderDetail);

        vm.ScanCommand.Execute(null);
        Assert.Empty(vm.Rows);
        Assert.Single(vm.Dismissed);

        vm.RestoreCommand.Execute(vm.Dismissed[0]);
        Assert.Single(vm.Rows);
        Assert.Empty(vm.Dismissed);
    }

    [Fact]
    public void Gaps_ADismissedSeries_ComesBack_WhenANewHoleAppears()
    {
        int seriesId = AddSeries("Saga", Owned(1), Owned(2), Owned(4));
        var vm = new CollectionGapsViewModel(CreateContext, Now);
        vm.EnsureScanned();
        vm.NotAGapCommand.Execute(vm.Rows[0]);

        using (var context = CreateContext())
        {
            context.Issues.Add(new Issue { SeriesId = seriesId, Number = "7", FilePath = "C:\\x\\7.cbz" });
            context.SaveChanges();
        }

        vm.ScanCommand.Execute(null);

        Assert.Equal("#3, #5–6", Assert.Single(vm.Rows).Missing);
    }

    [Fact]
    public void Gaps_OpenSeries_GoesToThatSeries()
    {
        int seriesId = AddSeries("Saga", Owned(1), Owned(3));
        int? opened = null;
        var vm = new CollectionGapsViewModel(CreateContext, Now) { OpenSeries = id => opened = id };
        vm.EnsureScanned();

        vm.OpenCommand.Execute(vm.Rows[0]);

        Assert.Equal(seriesId, opened);
    }

    // ----- Metadata consistency -----

    [Fact]
    public void Consistency_Scan_ListsEachSeriesAndCheck_WithItsOutliersInWords()
    {
        AddSeries("Saga", Owned(1, 2012, "Image"), Owned(2, 2012, "Image"), Owned(3, 2013, "Image"), Owned(4, 2013, "Image"), Owned(5, 1960, "Marvel"));
        var vm = new MetadataConsistencyViewModel(CreateContext, Now);

        vm.EnsureScanned();

        Assert.Equal(["Year", "Publisher"], vm.Rows.Select(r => r.CheckLabel));
        Assert.Equal("Most issues are around 2012", vm.Rows[0].ExpectedLabel);
        Assert.Equal("#5 has 1960", Assert.Single(vm.Rows[0].Outliers).Label);
        Assert.Equal("Most issues have Image", vm.Rows[1].ExpectedLabel);
        Assert.Equal("· 2 to check", vm.HeaderDetail);
    }

    [Fact]
    public void Consistency_ThisIsIntended_DismissesOneSeriesAndCheck_AndRestoreBringsItBack()
    {
        AddSeries("Saga", Owned(1, 2012, "Image"), Owned(2, 2012, "Image"), Owned(3, 2013, "Image"), Owned(4, 2013, "Image"), Owned(5, 1960, "Marvel"));
        var vm = new MetadataConsistencyViewModel(CreateContext, Now);
        vm.EnsureScanned();

        vm.IntendedCommand.Execute(vm.Rows.Single(r => r.CheckLabel == "Year"));

        Assert.Equal("Publisher", Assert.Single(vm.Rows).CheckLabel);
        Assert.Equal("Saga - year", Assert.Single(vm.Dismissed).Label);

        vm.ScanCommand.Execute(null);
        Assert.Single(vm.Rows);

        vm.RestoreCommand.Execute(vm.Dismissed[0]);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Empty(vm.Dismissed);
    }

    [Fact]
    public void Consistency_AnOutlier_OpensThatIssueInTheEditor_AndChangesNothing()
    {
        AddSeries("Saga", Owned(1, 2012), Owned(2, 2012), Owned(3, 2013), Owned(4, 2013), Owned(5, 1960));
        int? edited = null;
        var vm = new MetadataConsistencyViewModel(CreateContext, Now) { OpenIssueEditor = id => edited = id };
        vm.EnsureScanned();
        var outlier = vm.Rows[0].Outliers[0];

        vm.EditIssueCommand.Execute(outlier);

        Assert.Equal(outlier.IssueId, edited);
        using var context = CreateContext();
        Assert.Equal(1960, context.Issues.Single(i => i.Id == outlier.IssueId).Year);
        Assert.Empty(context.MetadataProposals);     // report-only: no proposal is created
    }

    [Fact]
    public void Consistency_ACleanLibrary_SaysSo()
    {
        AddSeries("Saga", Owned(1, 2012), Owned(2, 2012), Owned(3, 2013), Owned(4, 2013));
        var vm = new MetadataConsistencyViewModel(CreateContext, Now);

        vm.EnsureScanned();

        Assert.True(vm.IsClean);
        Assert.Equal("· Nothing inconsistent", vm.HeaderDetail);
    }

    // ----- Row templates -----

    private static (Window Window, ContentControl Host) Show(string templateKey, object item)
    {
        var section = new LibrarySection();
        var template = (IDataTemplate)section.Resources[templateKey]!;
        var host = new ContentControl { Content = item, ContentTemplate = template };
        var window = new Window { Content = host, Width = 1100, Height = 400 };
        window.Show();
        RunLayout(window);
        return (window, host);
    }

    private static List<string?> Texts(Visual root) => root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

    [Fact]
    public void TheGapRow_ShowsSeriesPublisherOwnedAndMissing_AndBothActions()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var (window, host) = Show("CollectionGapRowTemplate", new CollectionGapRow(1, "Invincible", "Image", "1–6, 9, 11–14", "#7–8, #10", 3, "1:7-8,10"));

            var texts = Texts(host);
            Assert.Contains("Invincible", texts);
            Assert.Contains("Image", texts);
            Assert.Contains("1–6, 9, 11–14", texts);
            Assert.Contains("#7–8, #10", texts);
            var buttons = host.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).ToList();
            Assert.Contains("Open series", buttons);
            Assert.Contains("Not a gap", buttons);
            window.Close();
        });
    }

    [Fact]
    public void TheConsistencyRow_ShowsTheCheckSeriesAndNorm_AndOneButtonPerOutlier()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var finding = new ConsistencyFinding(1, "Saga", ConsistencyCheck.Year, "2012", [new ConsistencyOutlier(5, "5", "1960"), new ConsistencyOutlier(9, "9", "1971")]);
            var (window, host) = Show("ConsistencyFindingRowTemplate", new ConsistencyFindingRow(finding));

            var texts = Texts(host);
            Assert.Contains("Year", texts);
            Assert.Contains("Saga", texts);
            Assert.Contains("Most issues are around 2012", texts);
            Assert.Contains("#5 has 1960", texts);
            Assert.Contains("#9 has 1971", texts);
            var buttons = host.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string).ToList();
            Assert.Contains("Open series", buttons);
            Assert.Contains("This is intended", buttons);
            window.Close();
        });
    }
}
