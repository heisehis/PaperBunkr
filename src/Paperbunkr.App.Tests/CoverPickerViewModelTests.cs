using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="CoverPickerViewModel"/> (docs/superpowers/specs/2026-09-17-reader-save-page-
/// and-cover-picker-design.md) - candidate resolution for the "From Series"/"From Reading List"
/// tabs, and that selecting one calls <see cref="CoverThumbnailService.TrySetCustomCover"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CoverPickerViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _coverDir;
    private readonly string _customCoverDir;

    public CoverPickerViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_coverpicker_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();

        _coverDir = Path.Combine(Path.GetTempPath(), $"paperbunkr_coverpicker_thumbs_{Guid.NewGuid():N}");
        _customCoverDir = Path.Combine(Path.GetTempPath(), $"paperbunkr_coverpicker_custom_{Guid.NewGuid():N}");
        CoverThumbnailPaths.ThumbnailDirectory = _coverDir;
        Services.Covers.CustomCoverPaths.Directory = _customCoverDir;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = null;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_coverDir)) Directory.Delete(_coverDir, recursive: true);
            if (Directory.Exists(_customCoverDir)) Directory.Delete(_customCoverDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext CreateContext() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    /// <summary>Real System.Drawing-rendered JPEG - <see cref="CoverThumbnailService.TrySetCustomCover"/> opens the source as a real <c>Bitmap</c>, so a placeholder byte array would fail to decode.</summary>
    private static void WriteFakeCover(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new System.Drawing.Bitmap(16, 24);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.Clear(System.Drawing.Color.SteelBlue);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);
    }

    [Fact]
    public void Load_SeriesTab_ExcludesTargetIssue_AndCoverlessSiblings()
    {
        using var context = CreateContext();
        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        var target = new Issue { SeriesId = series.Id, Number = "1" };
        var siblingWithCover = new Issue { SeriesId = series.Id, Number = "2" };
        var siblingNoCover = new Issue { SeriesId = series.Id, Number = "3" };
        context.Issues.AddRange(target, siblingWithCover, siblingNoCover);
        context.SaveChanges();
        WriteFakeCover(CoverThumbnailPaths.GetCachePath(siblingWithCover.Id));

        var vm = new CoverPickerViewModel(target.Id, series.Id, () => { });

        var candidate = Assert.Single(vm.SeriesCandidates);
        Assert.Equal(siblingWithCover.Id, candidate.IssueId);
    }

    [Fact]
    public void Load_ReadingListTab_DedupesAgainstSeriesTab()
    {
        using var context = CreateContext();
        var seriesA = new Series { Name = "A" };
        var seriesB = new Series { Name = "B" };
        context.Series.AddRange(seriesA, seriesB);
        context.SaveChanges();
        var target = new Issue { SeriesId = seriesA.Id, Number = "1" };
        var seriesSibling = new Issue { SeriesId = seriesA.Id, Number = "2" }; // in series AND reading list
        var readingListOnly = new Issue { SeriesId = seriesB.Id, Number = "1" };
        context.Issues.AddRange(target, seriesSibling, readingListOnly);
        context.SaveChanges();
        WriteFakeCover(CoverThumbnailPaths.GetCachePath(seriesSibling.Id));
        WriteFakeCover(CoverThumbnailPaths.GetCachePath(readingListOnly.Id));

        var list = new ReadingList { Name = "My List" };
        context.ReadingLists.Add(list);
        context.SaveChanges();
        context.ReadingListItems.AddRange(
            new ReadingListItem { ReadingListId = list.Id, IssueId = target.Id },
            new ReadingListItem { ReadingListId = list.Id, IssueId = seriesSibling.Id },
            new ReadingListItem { ReadingListId = list.Id, IssueId = readingListOnly.Id });
        context.SaveChanges();

        var vm = new CoverPickerViewModel(target.Id, seriesA.Id, () => { });

        Assert.Single(vm.SeriesCandidates); // seriesSibling
        var readingListCandidate = Assert.Single(vm.ReadingListCandidates); // readingListOnly only - seriesSibling deduped out
        Assert.Equal(readingListOnly.Id, readingListCandidate.IssueId);
    }

    [Fact]
    public void SelectCandidate_CopiesCoverAndInvokesCallback()
    {
        using var context = CreateContext();
        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        var target = new Issue { SeriesId = series.Id, Number = "1" };
        var source = new Issue { SeriesId = series.Id, Number = "2" };
        context.Issues.AddRange(target, source);
        context.SaveChanges();
        WriteFakeCover(CoverThumbnailPaths.GetCachePath(source.Id));

        bool applied = false;
        var vm = new CoverPickerViewModel(target.Id, series.Id, () => applied = true);
        var candidate = Assert.Single(vm.SeriesCandidates);

        vm.SelectCandidateCommand.Execute(candidate);

        Assert.True(applied);
        Assert.True(Services.Covers.CustomCoverPaths.Exists(target.Id));
    }

    [Fact]
    public void TabToggle_UpdatesActiveFlags()
    {
        var vm = new CoverPickerViewModel(1, null, () => { });
        Assert.True(vm.IsBrowseFileTabActive);

        vm.ShowSeriesTabCommand.Execute(null);
        Assert.True(vm.IsSeriesTabActive);
        Assert.False(vm.IsBrowseFileTabActive);

        vm.ShowReadingListTabCommand.Execute(null);
        Assert.True(vm.IsReadingListTabActive);
        Assert.False(vm.IsSeriesTabActive);
    }

    // --- External Provider tab (docs/superpowers/specs/2026-09-18-external-metadata-full-
    // extraction-design.md §2) - tab-wiring only; the actual candidate fetch goes through
    // MangaBakaMetadataProvider.Shared's real HttpClient, so it isn't exercised here, matching
    // this codebase's "no live network calls in tests" policy (see e.g.
    // AniListMetadataProviderTests' own doc comment on the same point). ---

    [Fact]
    public void HasExternalProviderTab_FalseWithoutContext()
    {
        var vm = new CoverPickerViewModel(1, null, () => { });

        Assert.False(vm.HasExternalProviderTab);
    }

    [Fact]
    public void HasExternalProviderTab_TrueWithContext_AndOpensDirectlyToIt()
    {
        var vm = new CoverPickerViewModel(1, null, () => { }, (ExternalMetadataProvider.MangaBaka, "708"));

        Assert.True(vm.HasExternalProviderTab);
        Assert.True(vm.IsExternalProviderTabActive);
    }

    [Fact]
    public void ShowExternalProviderTabCommand_ActivatesTheTab()
    {
        var vm = new CoverPickerViewModel(1, null, () => { });

        vm.ShowExternalProviderTabCommand.Execute(null);

        Assert.True(vm.IsExternalProviderTabActive);
        Assert.False(vm.IsBrowseFileTabActive);
    }
}
