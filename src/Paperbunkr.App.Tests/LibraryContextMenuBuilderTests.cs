using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Covers <see cref="LibraryContextMenuBuilder"/> - the Library right-click menu as plain data
/// (docs/superpowers/specs/2026-08-29-context-menu-rebuild-design.md). Same temp-SQLite harness as
/// <see cref="LibraryScreenViewModelTests"/>; runs under <see cref="AvaloniaTestCollection"/> since
/// constructing the view model materializes cover brushes.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryContextMenuBuilderTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public LibraryContextMenuBuilderTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_ctxmenu_test_{Guid.NewGuid():N}.db");
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

    private static void Seed(string name, ContentType contentType = ContentType.Comic,
        SeriesStatus status = SeriesStatus.Unknown, ReadingStatus readingStatus = ReadingStatus.Unknown,
        ReadingMode readingMode = ReadingMode.LeftToRight, string? filePath = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series
        {
            Name = name,
            ContentType = contentType,
            Status = status,
            ReadingStatus = readingStatus,
            ReadingMode = readingMode,
        };
        context.Series.Add(series);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", FilePath = filePath });
        context.SaveChanges();
    }

    private static LibraryScreenViewModel NewVm() =>
        new(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

    private static IReadOnlyList<ContextMenuEntry> Menu(LibraryScreenViewModel vm, object? target) =>
        ((IContextMenuProvider)vm).BuildContextMenu(target) ?? Array.Empty<ContextMenuEntry>();

    private static IEnumerable<ContextMenuEntry> Flatten(IEnumerable<ContextMenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            if (entry.Children is { } children)
            {
                foreach (var descendant in Flatten(children))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static ContextMenuEntry Find(IEnumerable<ContextMenuEntry> entries, string header) =>
        Flatten(entries).First(e => e.Header == header);

    [Fact]
    public void IssueMenu_HasCoreEntries_InOrder()
    {
        Seed("Alpha");
        var vm = NewVm();
        var row = Assert.Single(vm.IssueList.Rows);

        var headers = Menu(vm, row).Where(e => !e.IsSeparator).Select(e => e.Header).ToList();

        Assert.Equal(new[]
        {
            "Open", "Edit Properties…", "Quick Rate…", "Mark as", "Add to Reading List",
            "Go to Series", "Series", "Show in Explorer", "Select All", "Clear Selection", "Delete…",
        }, headers);
    }

    [Fact]
    public void IssueMenu_EveryActionLeaf_HasACommand()
    {
        Seed("Alpha");
        var vm = NewVm();
        var row = Assert.Single(vm.IssueList.Rows);

        foreach (var leaf in Flatten(Menu(vm, row)).Where(e => !e.IsSeparator && e.Children is null))
        {
            Assert.True(leaf.Command is not null, $"'{leaf.Header}' has no command");
        }
    }

    [Fact]
    public void ShowInExplorer_TracksHasFile()
    {
        Seed("NoFile");
        Seed("WithFile", filePath: "C:/x/withfile.cbz");
        var vm = NewVm();

        var noFile = vm.IssueList.Rows.Single(r => r.SeriesName == "NoFile");
        var withFile = vm.IssueList.Rows.Single(r => r.SeriesName == "WithFile");

        Assert.False(Find(Menu(vm, noFile), "Show in Explorer").IsEnabled);
        Assert.True(Find(Menu(vm, withFile), "Show in Explorer").IsEnabled);
    }

    [Fact]
    public void ReadingDirection_OnlyForMangaFamily()
    {
        Seed("AComic", ContentType.Comic);
        Seed("AManga", ContentType.Manga);
        var vm = NewVm();

        var comic = vm.IssueList.Rows.Single(r => r.SeriesName == "AComic");
        var manga = vm.IssueList.Rows.Single(r => r.SeriesName == "AManga");

        Assert.DoesNotContain(Flatten(Menu(vm, comic)), e => e.Header == "Reading Direction");
        Assert.Contains(Flatten(Menu(vm, manga)), e => e.Header == "Reading Direction");
    }

    [Fact]
    public void FindDuplicates_HiddenWithoutPluginHost()
    {
        Seed("Alpha");
        var vm = NewVm();
        var row = Assert.Single(vm.IssueList.Rows);

        Assert.DoesNotContain(Flatten(Menu(vm, row)), e => e.Header == "Find Duplicates");
    }

    [Fact]
    public void CurrentContentType_IsChecked()
    {
        Seed("AManga", ContentType.Manga);
        var vm = NewVm();
        var row = Assert.Single(vm.IssueList.Rows);

        var contentType = Find(Menu(vm, row), "Content Type");
        Assert.True(contentType.Children!.Single(c => c.Header == "Manga").IsChecked);
        Assert.False(contentType.Children!.Single(c => c.Header == "Comic").IsChecked);
    }

    [Fact]
    public void CurrentReadingStatus_MapsReReadingEnumToDisplayLabel()
    {
        Seed("Alpha", readingStatus: ReadingStatus.ReReading);
        var vm = NewVm();
        var row = Assert.Single(vm.IssueList.Rows);

        var readingStatus = Find(Menu(vm, row), "Reading Status");
        Assert.True(readingStatus.Children!.Single(c => c.Header == "Re-reading").IsChecked);
    }

    [Fact]
    public void SelectionAwareLabels_WhenTargetIsInAMultiSelection()
    {
        Seed("One");
        Seed("Two");
        var vm = NewVm();
        vm.SelectAllVisibleIssuesCommand.Execute(null);
        var row = vm.IssueList.Rows.First();

        var headers = Flatten(Menu(vm, row)).Select(e => e.Header).ToList();

        Assert.Contains("Mark 2 as", headers);
        Assert.Contains("Delete 2 comics…", headers);
        Assert.Contains("Add 2 to Reading List", headers);
    }

    [Fact]
    public void SingularLabels_WhenTargetOutsideSelection()
    {
        Seed("One");
        Seed("Two");
        var vm = NewVm();
        // Select only "Two"; right-click "One".
        var two = vm.IssueList.Rows.Single(r => r.SeriesName == "Two");
        vm.ToggleIssueSelection(two, isShiftHeld: false);
        var one = vm.IssueList.Rows.Single(r => r.SeriesName == "One");

        var headers = Flatten(Menu(vm, one)).Select(e => e.Header).ToList();

        Assert.Contains("Mark as", headers);
        Assert.Contains("Delete…", headers);
    }

    [Fact]
    public void SeriesCardMenu_HasItsOwnShape()
    {
        Seed("Alpha");
        var vm = NewVm();
        var card = Assert.Single(vm.Covers);

        var headers = Menu(vm, card).Where(e => !e.IsSeparator).Select(e => e.Header).ToList();

        Assert.Equal(new[]
        {
            "Open Series", "Content Type", "Publication Status", "Reading Status",
            "Show in Explorer", "Delete Series…",
        }, headers);
    }

    [Fact]
    public void EmptySpace_YieldsSelectAllOnly()
    {
        Seed("Alpha");
        var vm = NewVm();

        var entry = Assert.Single(Menu(vm, null));
        Assert.Equal("Select All", entry.Header);
    }
}
