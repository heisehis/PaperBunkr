using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// First test coverage for <see cref="MangaDetailScreenViewModel"/> (docs/superpowers/specs/
/// 2026-08-23-manga-detail-screen-design.md) - the manga/manhua/manhwa-specific detail screen.
/// Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file, same
/// approach as <see cref="DetailScreenViewModelTests"/>, since this view model has no injected
/// context-factory seam of its own. Joins <see cref="AvaloniaTestCollection"/> since that override
/// is a shared static other test classes also mutate.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MangaDetailScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly int _seriesId;

    public MangaDetailScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mangadetailvm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Manga", ContentType = ContentType.Manga, Status = SeriesStatus.Ongoing };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        context.Issues.AddRange(
            new Issue { SeriesId = series.Id, Number = "1", LastPageRead = 10, PageCount = 10 },
            new Issue { SeriesId = series.Id, Number = "2", LastPageRead = 3, PageCount = 10 },
            new Issue { SeriesId = series.Id, Number = "3" });
        context.SaveChanges();
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

    private static MangaDetailScreenViewModel CreateViewModel(Action<int>? goToReader = null, Action<int>? goToProperties = null, Action<int>? goDetailForSeries = null) =>
        new(goBack: () => { }, goToReader: goToReader ?? (_ => { }), goToProperties: goToProperties ?? (_ => { }), goToBulkProperties: _ => { }, goDetailForSeries: goDetailForSeries);

    [Fact]
    public void LoadSeries_PopulatesHeaderFields()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(_seriesId);

        Assert.Equal("Test Manga", vm.SeriesTitle);
        Assert.Equal("Ongoing", vm.StatusLabel);
        Assert.Equal(ContentType.Manga, vm.SelectedContentType);
    }

    [Fact]
    public void LoadSeries_PopulatesChaptersInNumberOrder()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(_seriesId);

        Assert.Equal(3, vm.Chapters.Count);
        Assert.Equal(new[] { "#1", "#2", "#3" }, vm.Chapters.Select(c => c.DisplayNumber));
    }

    [Fact]
    public void LoadSeries_MarksFullyReadAndInProgressChaptersCorrectly()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(_seriesId);

        var chapter1 = vm.Chapters.Single(c => c.DisplayNumber == "#1");
        var chapter2 = vm.Chapters.Single(c => c.DisplayNumber == "#2");
        var chapter3 = vm.Chapters.Single(c => c.DisplayNumber == "#3");

        Assert.True(chapter1.IsRead);
        Assert.False(chapter1.IsInProgress);

        Assert.False(chapter2.IsRead);
        Assert.True(chapter2.IsInProgress);

        Assert.False(chapter3.IsRead);
        Assert.False(chapter3.IsInProgress);
    }

    [Fact]
    public void ChapterFilter_Unread_ExcludesFullyReadChapters()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);

        vm.SetChapterFilterCommand.Execute(ChapterListFilter.Unread);

        Assert.DoesNotContain(vm.Chapters, c => c.DisplayNumber == "#1");
        Assert.Equal(2, vm.Chapters.Count);
    }

    [Fact]
    public void OpenChapter_InvokesGoToReaderWithThatIssueId()
    {
        int? capturedIssueId = null;
        var vm = CreateViewModel(goToReader: id => capturedIssueId = id);
        vm.LoadSeries(_seriesId);
        var chapter2 = vm.Chapters.Single(c => c.DisplayNumber == "#2");

        vm.OpenChapterCommand.Execute(chapter2);

        Assert.Equal(chapter2.Id, capturedIssueId);
    }

    /// <summary>
    /// Continue picks the in-progress chapter over the never-opened one, same priority as
    /// <see cref="DetailScreenViewModel"/>'s own Continue logic.
    /// </summary>
    [Fact]
    public void Continue_PicksInProgressChapterOverNeverOpened()
    {
        int? capturedIssueId = null;
        var vm = CreateViewModel(goToReader: id => capturedIssueId = id);
        vm.LoadSeries(_seriesId);
        var chapter2 = vm.Chapters.Single(c => c.DisplayNumber == "#2");

        vm.ContinueCommand.Execute(null);

        Assert.Equal(chapter2.Id, capturedIssueId);
    }

    [Fact]
    public void SelectedContentType_ChangedToComic_ReroutesViaGoDetailForSeries()
    {
        int? reroutedSeriesId = null;
        var vm = CreateViewModel(goDetailForSeries: id => reroutedSeriesId = id);
        vm.LoadSeries(_seriesId);

        vm.SelectedContentType = ContentType.Comic;

        Assert.Equal(_seriesId, reroutedSeriesId);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        Assert.Equal(ContentType.Comic, context.Series.Find(_seriesId)!.ContentType);
    }

    // --- Apply-from-Provider header additions (docs/superpowers/specs/2026-08-23-apply-from-
    // provider-design.md) ---

    [Fact]
    public void LoadSeries_NoProviderSourcedFields_AttributionsAreNull()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(_seriesId);

        Assert.Null(vm.SummaryAttribution);
        Assert.Null(vm.StatusAttribution);
    }

    [Fact]
    public void LoadSeries_AcceptedProviderProposal_PopulatesAttributionCaption()
    {
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            context.MetadataProposals.Add(new MetadataProposal
            {
                SeriesId = _seriesId,
                Field = MetadataProposalField.Summary,
                ProposedValue = "A synopsis.",
                Source = MetadataProposalSource.MetadataProvider,
                ProviderKey = ExternalMetadataProvider.MangaBaka,
                Confidence = 1.0m,
                Status = MetadataProposalStatus.Accepted,
                ResolvedAt = DateTime.UtcNow,
            });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);

        Assert.Equal("via MangaBaka", vm.SummaryAttribution);
    }

    [Fact]
    public void LoadSeries_RejectedProviderProposal_LeavesAttributionNull()
    {
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            context.MetadataProposals.Add(new MetadataProposal
            {
                SeriesId = _seriesId,
                Field = MetadataProposalField.Summary,
                ProposedValue = "A synopsis.",
                Source = MetadataProposalSource.MetadataProvider,
                ProviderKey = ExternalMetadataProvider.MangaBaka,
                Confidence = 1.0m,
                Status = MetadataProposalStatus.Rejected,
                ResolvedAt = DateTime.UtcNow,
            });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);

        Assert.Null(vm.SummaryAttribution);
    }

    [Fact]
    public void LoadSeries_PopulatesExternalLinksChipRow()
    {
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = _seriesId, Provider = ExternalMetadataProvider.AniList, ExternalId = "30013", Url = "https://anilist.co/manga/30013" });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);

        Assert.True(vm.HasExternalLinks);
        var link = Assert.Single(vm.ExternalLinks);
        Assert.Equal("AniList", link.ProviderLabel);
        Assert.Equal("30013", link.ExternalId);
    }

    [Fact]
    public void LoadSeries_ComputesChapterAndVolumeCountBadges()
    {
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            foreach (var issue in context.Issues.Where(i => i.SeriesId == _seriesId))
            {
                issue.Volume = "1";
            }

            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);

        Assert.Equal("3 chapters", vm.ChapterCountBadge);
        Assert.Equal("1 volume", vm.VolumeCountBadge);
    }

    [Fact]
    public void LoadSeries_NoVolumeData_VolumeCountBadgeIsEmpty()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(_seriesId);

        Assert.Equal(string.Empty, vm.VolumeCountBadge);
    }

    // --- Mark as Read/Unread (docs/superpowers/specs/2026-08-23-mark-as-read-design.md) ---

    [Fact]
    public void MarkChapterRead_SetsLastPageReadToLastValidIndex_AndReflectsOnTheReloadedRow()
    {
        int issueId;
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            var issue = context.Issues.Single(i => i.Number == "3");
            issue.PageCount = 20;
            context.SaveChanges();
            issueId = issue.Id;
        }

        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);
        var chapter = vm.Chapters.Single(c => c.DisplayNumber == "#3");

        vm.MarkChapterReadCommand.Execute(chapter);

        using var verifyContext = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        Assert.Equal(19, verifyContext.Issues.Find(issueId)!.LastPageRead);
        Assert.True(vm.Chapters.Single(c => c.DisplayNumber == "#3").IsRead);
    }

    [Fact]
    public void MarkChapterUnread_ZeroesLastPageRead()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);
        var chapter = vm.Chapters.Single(c => c.DisplayNumber == "#1"); // seeded fully-read (LastPageRead 10 == PageCount 10)
        Assert.True(chapter.IsRead);

        vm.MarkChapterUnreadCommand.Execute(chapter);

        using var verifyContext = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var issueId = verifyContext.Issues.Single(i => i.Number == "1").Id;
        Assert.Equal(0, verifyContext.Issues.Find(issueId)!.LastPageRead);
        Assert.False(vm.Chapters.Single(c => c.DisplayNumber == "#1").IsRead);
    }

    [Fact]
    public void MarkChapterRead_UnknownPageCount_NoOps()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);
        var chapter = vm.Chapters.Single(c => c.DisplayNumber == "#3"); // seeded with no PageCount

        var exception = Record.Exception(() => vm.MarkChapterReadCommand.Execute(chapter));

        Assert.Null(exception);
        Assert.False(vm.Chapters.Single(c => c.DisplayNumber == "#3").IsRead);
    }

    // --- Streaming redesign (docs/superpowers/specs/2026-08-28-detail-screens-streaming-redesign-design.md) ---

    [Fact]
    public void LoadSeries_SecondaryTitle_UsesNativeOrRomanizedAltTitle_NotThePrimaryName()
    {
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            var series = context.Series.Include(s => s.Titles).Single(s => s.Id == _seriesId);
            series.Titles.Add(new SeriesTitle { Value = "テストマンガ", Type = SeriesTitleType.Native });
            series.Titles.Add(new SeriesTitle { Value = "Test Manga", Type = SeriesTitleType.Romanized }); // equals primary name - skipped
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);

        Assert.Equal("テストマンガ", ((IDetailHeaderSource)vm).SecondaryTitle);
    }

    [Fact]
    public void LoadSeries_NoAltTitles_SecondaryTitleIsNull()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);
        Assert.Null(((IDetailHeaderSource)vm).SecondaryTitle);
    }

    [Fact]
    public void LoadSeries_TrackerProgressIsNull()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);
        Assert.Null(((IDetailHeaderSource)vm).TrackerProgress);
    }

    [Fact]
    public void ChapterGroups_GroupsByVolume_NoVolumeBucketLast()
    {
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            context.Issues.Single(i => i.Number == "1").Volume = "1";
            context.Issues.Single(i => i.Number == "2").Volume = "2";
            // #3 keeps no volume
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);

        Assert.Equal(new[] { "Volume 1", "Volume 2", "No volume" }, vm.ChapterGroups.Select(g => g.VolumeLabel));
        Assert.Equal("#3", vm.ChapterGroups.Last().Chapters.Single().DisplayNumber);
    }

    [Fact]
    public void ChapterRow_IsNew_OnlyWhenUnreadAndReleasedWithinTwoWeeks()
    {
        using (var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            context.Issues.Single(i => i.Number == "3").ReleasedTime = DateTime.Now.AddDays(-3);   // unread + recent -> NEW
            context.Issues.Single(i => i.Number == "2").ReleasedTime = DateTime.Now.AddDays(-40);  // unread + old   -> not NEW
            context.Issues.Single(i => i.Number == "1").ReleasedTime = DateTime.Now.AddDays(-1);   // read + recent  -> not NEW
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(_seriesId);

        Assert.True(vm.Chapters.Single(c => c.DisplayNumber == "#3").IsNew);
        Assert.False(vm.Chapters.Single(c => c.DisplayNumber == "#2").IsNew);
        Assert.False(vm.Chapters.Single(c => c.DisplayNumber == "#1").IsNew);
    }
}
