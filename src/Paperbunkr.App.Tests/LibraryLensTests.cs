using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Library's lens tabs end to end through <see cref="LibraryScreenViewModel"/> (docs/superpowers/specs/
/// 2026-10-04-library-redesign-design.md, Slice 1): what each lens shows in both granularities, that the counts add up
/// and follow the search and the chips, that the lens persists and travels with a Workspace, and that the toolbar's
/// tabs answer <see cref="TabStrip.Step"/>. Same temp-database isolation as <see cref="LibraryWorkspaceTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryLensTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public LibraryLensTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_library_lens_test_{Guid.NewGuid():N}.db");
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

    /// <summary>One issue per entry: null = never opened, otherwise the last page read of a 20-page issue (19 = finished).</summary>
    private static void Seed(string name, string publisher, params int?[] lastPagesRead)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, Publisher = publisher };
        context.Series.Add(series);
        context.SaveChanges();

        for (int i = 0; i < lastPagesRead.Length; i++)
        {
            context.Issues.Add(new Issue
            {
                SeriesId = series.Id,
                Number = (i + 1).ToString(),
                PageCount = 20,
                LastPageRead = lastPagesRead[i],
                Publisher = publisher,
                AddedTime = DateTime.UtcNow,
            });
        }

        context.SaveChanges();
    }

    /// <summary>"Fresh" is untouched, "Midway" has one finished, one half-read and one unopened issue, "Done" is finished.</summary>
    private static void SeedThreeSeries()
    {
        Seed("Fresh", "DC", null, null);
        Seed("Midway", "Marvel", 19, 8, null);
        Seed("Done", "DC", 19);
    }

    private static LibraryScreenViewModel CreateVm(LibraryContentGranularity granularity = LibraryContentGranularity.Series)
    {
        var vm = new LibraryScreenViewModel(
            goDetail: _ => { },
            goReaderForIssue: _ => { },
            goToNewIssueProperties: (_, _, _) => { },
            promptForName: (_, cb) => cb("Lens workspace"));
        vm.SetGranularityCommand.Execute(granularity);
        return vm;
    }

    private static string[] CardNames(LibraryScreenViewModel vm) => vm.Covers.Select(c => c.Name).OrderBy(n => n).ToArray();

    [Fact]
    public void SeriesCounts_AreMutuallyExclusive_AndAddUpToAll()
    {
        SeedThreeSeries();
        var vm = CreateVm();

        Assert.Equal(3, vm.LensAllCount);
        Assert.Equal(1, vm.LensReadingCount);
        Assert.Equal(1, vm.LensUnreadCount);
        Assert.Equal(1, vm.LensReadCount);
        Assert.Equal(vm.LensAllCount, vm.LensReadingCount + vm.LensUnreadCount + vm.LensReadCount);
        Assert.Equal("3 series", vm.ResultSummary);
    }

    [Theory]
    [InlineData(LibraryLens.Reading, "Midway")]
    [InlineData(LibraryLens.Unread, "Fresh")]
    [InlineData(LibraryLens.Read, "Done")]
    public void SeriesLens_ShowsOnlyItsSeries(LibraryLens lens, string expected)
    {
        SeedThreeSeries();
        var vm = CreateVm();

        vm.SetLensCommand.Execute(lens);

        Assert.Equal(new[] { expected }, CardNames(vm));
        Assert.Equal("1 series", vm.ResultSummary);
        // The other tabs keep their numbers: a count never depends on which lens is selected.
        Assert.Equal(3, vm.LensAllCount);
    }

    [Fact]
    public void IssueLens_AppliesPerIssue()
    {
        SeedThreeSeries();
        var vm = CreateVm(LibraryContentGranularity.Issue);

        // Fresh: 2 unread. Midway: 1 read, 1 in progress, 1 unread. Done: 1 read.
        Assert.Equal(6, vm.LensAllCount);
        Assert.Equal(1, vm.LensReadingCount);
        Assert.Equal(3, vm.LensUnreadCount);
        Assert.Equal(2, vm.LensReadCount);

        vm.SetLensCommand.Execute(LibraryLens.Reading);

        var row = Assert.Single(vm.IssueList.Rows);
        Assert.Equal("Midway", row.SeriesName);
        Assert.Equal("1 issue", vm.ResultSummary);
    }

    [Fact]
    public void Counts_FollowTheSearchAndTheChips()
    {
        SeedThreeSeries();
        var vm = CreateVm();

        vm.PublisherFilter = "DC";

        Assert.Equal(2, vm.LensAllCount);
        Assert.Equal(0, vm.LensReadingCount);
        Assert.True(vm.IsLensReadingEmpty);
        Assert.True(vm.ShowClearChip);

        vm.ClearChipsCommand.Execute(null);
        vm.SearchQuery = "midway";

        Assert.Equal(1, vm.LensAllCount);
        Assert.Equal(1, vm.LensReadingCount);
        Assert.Equal(0, vm.LensUnreadCount);
    }

    [Fact]
    public void HasUnreadChip_KeepsItsOldMeaning_AlongsideTheLens()
    {
        SeedThreeSeries();
        var vm = CreateVm();

        vm.FilterUnreadOnly = true;

        // "Has unread" is anything with an unopened issue: the untouched series and the half-read one.
        Assert.Equal(new[] { "Fresh", "Midway" }, CardNames(vm));
        Assert.Equal(LibraryLens.All, vm.ActiveLens);
    }

    [Fact]
    public void EmptyLens_SaysSo_AndOffersToShowAll()
    {
        Seed("Fresh", "DC", new int?[] { null });
        var vm = CreateVm();

        vm.SetLensCommand.Execute(LibraryLens.Read);

        Assert.True(vm.ShowEmptyState);
        Assert.Equal("Nothing read yet.", vm.EmptyStateMessage);
        Assert.Equal("Show all", vm.EmptyStateActionLabel);

        vm.EmptyStateActionCommand.Execute(null);

        Assert.Equal(LibraryLens.All, vm.ActiveLens);
        Assert.False(vm.ShowEmptyState);
    }

    [Fact]
    public void Lens_SurvivesARestart()
    {
        SeedThreeSeries();
        CreateVm().SetLensCommand.Execute(LibraryLens.Unread);

        var reopened = CreateVm();

        Assert.Equal(LibraryLens.Unread, reopened.ActiveLens);
        Assert.True(reopened.IsUnreadLens);
        Assert.Equal(new[] { "Fresh" }, CardNames(reopened));
    }

    [Fact]
    public void Workspace_SavesAndRestoresTheLens_AndChangingItDropsTheLabel()
    {
        SeedThreeSeries();
        var vm = CreateVm();
        vm.SetLensCommand.Execute(LibraryLens.Read);
        vm.SaveWorkspaceAsCommand.Execute(null);
        int savedId = vm.Workspaces.Single(w => !w.IsBuiltIn).Id;

        vm.SetLensCommand.Execute(LibraryLens.All);
        Assert.DoesNotContain(vm.Workspaces, w => w.IsActive);

        vm.ApplyWorkspaceCommand.Execute(savedId);

        Assert.Equal(LibraryLens.Read, vm.ActiveLens);
        Assert.Equal(new[] { "Done" }, CardNames(vm));
    }

    [Fact]
    public void CurrentlyReadingBuiltIn_IsTheReadingLens()
    {
        SeedThreeSeries();
        new WorkspaceService().EnsureBuiltInsSeeded();
        var vm = CreateVm();

        vm.ApplyWorkspaceCommand.Execute(vm.Workspaces.Single(w => w.Name == "Currently reading").Id);

        Assert.Equal(LibraryLens.Reading, vm.ActiveLens);
        Assert.False(vm.FilterUnreadOnly);
    }

    [Fact]
    public void ToolbarLensTabs_StepWithTheTabStrip_AndWrap()
    {
        SeedThreeSeries();
        var vm = CreateVm();
        var toolbar = new LibraryToolbar { DataContext = vm };
        var window = new Window { Width = 1300, Height = 400, Content = toolbar };
        window.Show();
        TestDispatcher.Drain();

        Assert.True(TabStrip.Step(toolbar, 1));
        Assert.Equal(LibraryLens.Reading, vm.ActiveLens);
        TestDispatcher.Drain();

        Assert.True(TabStrip.Step(toolbar, -1));
        TestDispatcher.Drain();
        Assert.True(TabStrip.Step(toolbar, -1));
        Assert.Equal(LibraryLens.Read, vm.ActiveLens);

        window.Close();
    }

    [Fact]
    public void NarrowToolbar_CollapsesTheEmptySearch_AndFocusSearchOpensIt()
    {
        SeedThreeSeries();
        var vm = CreateVm();
        var toolbar = new LibraryToolbar { DataContext = vm };
        var window = new Window { Width = 1300, Height = 400, Content = toolbar };
        window.Show();
        TestDispatcher.Drain();
        Assert.False(toolbar.IsSearchCollapsed);

        window.Width = 820;
        TestDispatcher.Drain();
        Assert.True(toolbar.IsSearchCollapsed);

        toolbar.FocusSearchBox();
        TestDispatcher.Drain();
        Assert.False(toolbar.IsSearchCollapsed);

        vm.SearchQuery = "spawn";
        window.Focus();
        TestDispatcher.Drain();
        Assert.False(toolbar.IsSearchCollapsed);

        window.Close();
    }

    /// <summary>Not an assertion: renders the real toolbar with the app's styles and logs each control's vertical box, when PB_RENDER_OUT is set.</summary>
    [Fact]
    public void RenderToolbar()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        SeedThreeSeries();
        var vm = CreateVm();
        Theming.FoundationStyleTests.WithFoundation("cool_technical", () =>
        {
            var toolbar = new LibraryToolbar { DataContext = vm };
            int width = int.TryParse(Environment.GetEnvironmentVariable("PB_RENDER_WIDTH"), out int w) ? w : 1070;
            var window = new Window { Width = width, Height = 140, Content = toolbar, Background = (Avalonia.Media.IBrush)Avalonia.Application.Current!.FindResource("PbBgBrush")! };
            window.Show();
            FocusTestHarness.RunLayout(window);
            var lines = new System.Collections.Generic.List<string>();
            foreach (var c in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(toolbar).OfType<Control>()
                         .Where(c => c is Button or Avalonia.Controls.Primitives.ToggleButton or TextBox || (c is Border b && b.Classes.Count > 0) || (c is TextBlock t && t.Name is null && t.Text?.Contains("series") == true)))
            {
                var p = Avalonia.VisualExtensions.TranslatePoint(c, default, toolbar);
                if (p is null || !c.IsEffectivelyVisible) continue;
                lines.Add($"{c.GetType().Name,-12} [{string.Join(' ', c.Classes)}] x={p.Value.X:0} top={p.Value.Y:0} h={c.Bounds.Height:0} mid={p.Value.Y + c.Bounds.Height / 2:0.0} w={c.Bounds.Width:0} va={c.VerticalAlignment} m={c.Margin} name={c.Name}");
            }
            File.WriteAllLines(Path.Combine(outDir, "toolbar-boxes.txt"), lines);
            // PB_RENDER_SCALE (1.25, 1.5) renders as Windows display scaling would (2026-10-20 UI polish SP5 Q4).
            double scale = double.TryParse(Environment.GetEnvironmentVariable("PB_RENDER_SCALE"), System.Globalization.CultureInfo.InvariantCulture, out double sc) ? sc : 1;
            var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)(width * scale), (int)(140 * scale)), new Avalonia.Vector(96 * scale, 96 * scale));
            rtb.Render(window);
            rtb.Save(Path.Combine(outDir, $"toolbar-{width}@{scale}.png"));
            window.Close();
        });
    }
}
