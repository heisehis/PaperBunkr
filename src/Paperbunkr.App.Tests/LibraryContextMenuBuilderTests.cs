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
            "Open", "Edit Properties…", "My Rating", "Mark as", "Add to Reading List", "Add to Collection", "Show in List",
            "Go to Series", "Series", "Scrape…", "Organize…", "Copy Data", "Paste Data…", "Clear Data…", "Refresh",
            "Show in Explorer", "Copy file path", "Select All", "Invert Selection", "Clear Selection", "Delete…",
        }, headers);
    }

    [Fact]
    public void IssueMenu_EveryActionLeaf_HasACommand()
    {
        Seed("Alpha");
        var vm = NewVm();
        var row = Assert.Single(vm.IssueList.Rows);

        // Disabled placeholders ("(Not in any list)") are the one leaf kind with nothing to run.
        foreach (var leaf in Flatten(Menu(vm, row)).Where(e => !e.IsSeparator && e.Children is null && e.IsEnabled))
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
        Assert.Contains("Add 2 to Collection", headers);
    }

    [Fact]
    public void AddToCollection_IssueMenu_ListsExistingCollections_PlusNewCollection()
    {
        Seed("Alpha");
        var vm = NewVm();
        using (var context = PaperbunkrDb.CreateContext())
        {
            Paperbunkr.Data.Collections.CollectionService.Create(context, "Favorites");
        }
        vm.LoadFromDatabase();
        var row = Assert.Single(vm.IssueList.Rows);

        var addToCollection = Find(Menu(vm, row), "Add to Collection");
        var childHeaders = addToCollection.Children!.Select(c => c.Header).ToList();

        Assert.Equal(new[] { "Favorites", null, "New collection…" }, childHeaders);
        Assert.All(addToCollection.Children!.Where(c => !c.IsSeparator), c => Assert.NotNull(c.Command));
    }

    [Fact]
    public void AddToCollection_SeriesCardMenu_ListsExistingCollections_PlusNewCollection()
    {
        Seed("Alpha");
        var vm = NewVm();
        using (var context = PaperbunkrDb.CreateContext())
        {
            Paperbunkr.Data.Collections.CollectionService.Create(context, "Favorites");
        }
        vm.LoadFromDatabase();
        var card = Assert.Single(vm.Covers);

        var addToCollection = Find(Menu(vm, card), "Add to Collection");
        var childHeaders = addToCollection.Children!.Select(c => c.Header).ToList();

        Assert.Equal(new[] { "Favorites", null, "New collection…" }, childHeaders);
    }

    /// <summary>Right-clicking a tile outside the selection acts on the selection ∪ that tile (UnionForAction) - the label now says so
    /// (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §1). It used to read "Mark as" while marking both.</summary>
    [Fact]
    public void Labels_CountTheUnion_WhenTargetOutsideSelection()
    {
        Seed("One");
        Seed("Two");
        var vm = NewVm();
        // Select only "Two"; right-click "One".
        var two = vm.IssueList.Rows.Single(r => r.SeriesName == "Two");
        vm.ToggleIssueSelection(two, isShiftHeld: false);
        var one = vm.IssueList.Rows.Single(r => r.SeriesName == "One");

        var headers = Flatten(Menu(vm, one)).Select(e => e.Header).ToList();

        Assert.Contains("Mark 2 as", headers);
        Assert.Contains("Delete 2 comics…", headers);
    }

    [Fact]
    public void SingularLabels_WhenNothingElseSelected()
    {
        Seed("One");
        Seed("Two");
        var vm = NewVm();
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
            "Open Series", "Bulk Edit…", "Mark as", "Add to Reading List", "Add to Collection",
            "Content Type", "Publication Status", "Reading Status",
            "Scrape…", "Organize…", "Refresh", "Show in Explorer", "Copy file paths",
            "Select All", "Invert Selection", "Clear Selection", "Delete Series…",
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

    [Fact]
    public void WriteMetadataToFiles_AbsentWhenSettingOff_PresentWhenOn()
    {
        Seed("Alpha", filePath: @"C:\comics\alpha1.cbz");
        var vm = NewVm();
        var row = Assert.Single(vm.IssueList.Rows);

        // Default: master toggle off.
        Assert.DoesNotContain(Flatten(Menu(vm, row)), e => e.Header == "Write metadata to file");
        Assert.DoesNotContain(Flatten(Menu(vm, Assert.Single(vm.Covers))), e => e.Header == "Write metadata to files");

        using (var context = PaperbunkrDb.CreateContext())
        {
            context.GetOrCreateAppSettings().WriteMetadataToFiles = true;
            context.SaveChanges();
        }
        vm.LoadFromDatabase();

        Assert.Contains(Flatten(Menu(vm, vm.IssueList.Rows.Single())), e => e.Header == "Write metadata to file");
        Assert.Contains(Flatten(Menu(vm, Assert.Single(vm.Covers))), e => e.Header == "Write metadata to files");
    }
}
