using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// ViewModel side of the 2026-09-28 Home pitch (docs/superpowers/specs/2026-09-28-home-improvements-design.md and
/// 2026-09-28-home-cosmetics-design.md): the section list, hidden sections, Needs Attention, "Not interested" + Undo, the
/// greeting/seasonal masthead, the rotation pause, the two crossfade layers, the collage and the Because-You-Read lead card.
/// Same temp-database pattern as <see cref="HomeScreenViewModelTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class HomeScreenPitchViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public HomeScreenPitchViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_home_pitch_vm_test_{Guid.NewGuid():N}.db");
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

    private sealed class FakeToastHost : IToastHost
    {
        public List<ToastRequest> Shown { get; } = new();

        public void Show(ToastRequest toast) => Shown.Add(toast);

        public void Close(ToastRequest toast)
        {
        }
    }

    private static int SeedSeries(string name, params (string? Number, int? Read, DateTime? Opened)[] issues)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        foreach (var (number, read, opened) in issues)
        {
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = number, LastPageRead = read, PageCount = 100, OpenedTime = opened, AddedTime = DateTime.UtcNow.AddDays(-60) });
        }

        context.SaveChanges();
        return series.Id;
    }

    private static void Settings(Action<AppSettings> edit)
    {
        using var context = PaperbunkrDb.CreateContext();
        edit(context.GetOrCreateAppSettings());
        context.SaveChanges();
    }

    private static HomeScreenViewModel Make(IToastHost? toasts = null, Func<DateTime>? now = null, Action<int>? reader = null,
        Action<string>? preferences = null)
        => new(_ => { }, reader ?? (_ => { }), _ => { }, (_, _) => { }, (_, _) => { }, toastHost: toasts, localNow: now,
            goPreferencesAnchor: preferences);

    private static void Relate(int sourceId, int targetId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var relation = new MediaRelation { SourceSeriesId = sourceId, TargetSeriesId = targetId, RelationType = RelationType.Sequel };
        relation.Evidence.Add(new RelationEvidence { MediaRelation = relation, Provider = RelationEvidenceProvider.User, Confidence = 1.0m });
        context.MediaRelations.Add(relation);
        context.SaveChanges();
    }

    // --- I1: sections ---

    [Fact]
    public void Sections_FollowTheDefaultOrder_LeavingOutAnEmptyNeedsAttention()
    {
        var vm = Make();

        Assert.Equal(HomeSectionKey.Default.Where(k => k != HomeSectionKey.NeedsAttention), vm.Sections.Select(s => s.Key));
        Assert.False(vm.AllSectionsHidden);
    }

    [Fact]
    public void Sections_FollowASavedOrder()
    {
        Settings(s => s.HomeSectionOrder = "readingList,recentlyAdded");

        var vm = Make();

        int reading = vm.Sections.Select(s => s.Key).ToList().IndexOf(HomeSectionKey.ReadingList);
        int recent = vm.Sections.Select(s => s.Key).ToList().IndexOf(HomeSectionKey.RecentlyAdded);
        Assert.True(reading < recent);
    }

    [Fact]
    public void HiddenSection_IsLeftOut_AndNeverQueried()
    {
        SeedSeries("Unread", ("1", null, null));
        Settings(s => s.HomeHiddenSections = "spotlight");

        var vm = Make();

        Assert.DoesNotContain(vm.Sections, s => s.Key == HomeSectionKey.Spotlight);
        Assert.Empty(vm.SpotlightItems); // skipped, not merely hidden - there IS an unread issue to pick
    }

    [Fact]
    public void AllSectionsHidden_ShowsTheEmptyLine_AndLinksToPreferences()
    {
        Settings(s => s.HomeHiddenSections = string.Join(",", HomeSectionKey.Default));
        string? anchor = null;

        var vm = Make(preferences: a => anchor = a);
        vm.OpenHomePreferencesCommand.Execute(null);

        Assert.True(vm.AllSectionsHidden);
        Assert.Empty(vm.Sections);
        Assert.Equal("appearance.home", anchor);
    }

    // --- I3: Needs Attention ---

    [Fact]
    public void NeedsAttention_ShowsUnderTheSpotlight_AndResumesTheNextIssue()
    {
        SeedSeries("Almost", ("1", 100, DateTime.UtcNow.AddDays(-2)), ("2", null, null));
        int? opened = null;

        var vm = Make(reader: id => opened = id);

        Assert.True(vm.HasNeedsAttention);
        Assert.Equal("1 issue left in Almost", vm.NeedsAttention!.Headline);
        Assert.Equal(HomeSectionKey.NeedsAttention, vm.Sections[1].Key);

        vm.OpenAttentionCommand.Execute(null);
        Assert.Equal(vm.NeedsAttention.Attention.ResumeIssueId, opened);
    }

    // --- I5: "Not interested" ---

    [Fact]
    public void NotInterested_RemovesTheCardEverywhere_PersistsIt_AndUndoBringsItBack()
    {
        int source = SeedSeries("Source", ("1", null, DateTime.UtcNow));
        int target = SeedSeries("Target", ("1", null, null));
        Relate(source, target);
        var toasts = new FakeToastHost();
        var vm = Make(toasts);
        var card = vm.BecauseYouRead.Single().Cards.Single();

        vm.NotInterestedCommand.Execute(card);
        TestDispatcher.Drain();

        Assert.Empty(vm.BecauseYouRead);
        vm.LoadFromDatabase();
        Assert.Empty(vm.BecauseYouRead); // persisted, not just removed from the current rows

        var toast = Assert.Single(toasts.Shown);
        var undo = Assert.Single(toast.Actions!);
        Assert.Equal("Undo", undo.Label);
        undo.Command.Execute(null);
        TestDispatcher.Drain();

        Assert.Equal("Target", vm.BecauseYouRead.Single().Cards.Single().Name);
    }

    [Fact]
    public void BecauseYouReadRow_CarriesTheSeedSeriesForItsLeadCard()
    {
        int source = SeedSeries("Source", ("1", null, DateTime.UtcNow));
        Relate(source, SeedSeries("Target", ("1", null, null)));

        var vm = Make();

        Assert.Equal("Source", vm.BecauseYouRead.Single().SeedSeries!.Name);
    }

    // --- C2 / C10: masthead ---

    [Theory]
    [InlineData(8, "Good morning")]
    [InlineData(14, "Good afternoon")]
    [InlineData(19, "Good evening")]
    [InlineData(23, "Late-night reading?")]
    public void Greeting_FollowsTheLocalTime(int hour, string expected)
    {
        var vm = Make(now: () => new DateTime(2026, 9, 28, hour, 0, 0));

        Assert.Equal(expected, vm.Greeting);
    }

    [Fact]
    public void SeasonalFlourish_OnlyWhenSwitchedOn_AndInSeason()
    {
        var halloween = new DateTime(2026, 10, 30, 12, 0, 0);
        Assert.False(Make(now: () => halloween).HasSeason); // default off

        Settings(s => s.HomeSeasonalFlourish = true);
        var vm = Make(now: () => halloween);
        Assert.True(vm.HasSeason);
        Assert.Equal("Halloween", vm.SeasonLabel);

        Assert.False(Make(now: () => new DateTime(2026, 8, 1)).HasSeason);
    }

    // --- C7: rotation and layers ---

    [Fact]
    public void Rotation_StopsOffScreen_AndAHoverPauseSurvivesComingBackOnScreen()
    {
        var vm = Make();
        Assert.True(vm.IsSpotlightRotating);

        vm.SetOnScreen(false);
        Assert.False(vm.IsSpotlightRotating);

        vm.SetOnScreen(true);
        Assert.True(vm.IsSpotlightRotating);

        vm.PauseSpotlightRotation();
        vm.SetOnScreen(false);
        vm.SetOnScreen(true);
        Assert.False(vm.IsSpotlightRotating); // still hovered

        vm.ResumeSpotlightRotation();
        Assert.True(vm.IsSpotlightRotating);
    }

    [Fact]
    public void Spotlight_ShowsEightPanels_WithTheOpenOneFollowingTheIndex()
    {
        for (int i = 0; i < 10; i++)
        {
            SeedSeries($"Series {i}", ("1", null, null));
        }

        var vm = Make();

        Assert.Equal(HomeScreenViewModel.SpotlightPickCount, vm.SpotlightPanels.Count);
        Assert.True(vm.SpotlightPanels[0].IsOpen);
        Assert.Single(vm.SpotlightPanels, p => p.IsOpen);

        vm.SpotlightIndex = 3;
        Assert.True(vm.SpotlightPanels[3].IsOpen);
        Assert.Single(vm.SpotlightPanels, p => p.IsOpen);
    }

    [Fact]
    public void ActivatingAClosedPanel_OpensIt_AndActivatingTheOpenOne_Reads()
    {
        for (int i = 0; i < 3; i++)
        {
            SeedSeries($"Series {i}", ("1", null, null));
        }

        int? read = null;
        var vm = Make(reader: id => read = id);

        vm.ActivateSpotlightPanelCommand.Execute(vm.SpotlightPanels[2]);
        Assert.Equal(2, vm.SpotlightIndex);
        Assert.Null(read);

        vm.ActivateSpotlightPanelCommand.Execute(vm.SpotlightPanels[2]);
        Assert.Equal(vm.SpotlightPanels[2].Sample.IssueId, read);

        vm.ReadSpotlightPanelCommand.Execute(vm.SpotlightPanels[0]);
        Assert.Equal(vm.SpotlightPanels[0].Sample.IssueId, read);
    }

    [Fact]
    public void NextAndPrevious_WrapAround()
    {
        for (int i = 0; i < 3; i++)
        {
            SeedSeries($"Series {i}", ("1", null, null));
        }

        var vm = Make();

        vm.PreviousSpotlightCommand.Execute(null);
        Assert.Equal(2, vm.SpotlightIndex);
        vm.NextSpotlightCommand.Execute(null);
        Assert.Equal(0, vm.SpotlightIndex);
        Assert.Equal(vm.SpotlightPanels[0].Sample, vm.CurrentSpotlight);
    }

    // --- C5 / C6 ---

    [Fact]
    public void RecentlyAdded_FlagsSeriesAddedThisWeek()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var fresh = new Series { Name = "Fresh" };
            var stale = new Series { Name = "Stale" };
            context.Series.AddRange(fresh, stale);
            context.SaveChanges();
            context.Issues.Add(new Issue { SeriesId = fresh.Id, AddedTime = DateTime.UtcNow.AddDays(-1) });
            context.Issues.Add(new Issue { SeriesId = stale.Id, AddedTime = DateTime.UtcNow.AddDays(-30) });
            context.SaveChanges();
        }

        var vm = Make();

        Assert.True(vm.RecentlyAdded.Single(c => c.Name == "Fresh").IsRecentlyAdded);
        Assert.False(vm.RecentlyAdded.Single(c => c.Name == "Stale").IsRecentlyAdded);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(3, true)]   // the reading-list rule: three covers fill the grid by repeating the first
    [InlineData(2, false)]
    public void CollectionCard_UsesACollage_WhenItHasEnoughCovers(int members, bool collage)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var ids = Enumerable.Range(0, members).Select(i => SeedSeries($"Member {i}", ("1", null, null))).ToArray();
            var collection = CollectionService.Create(context, "Shelf");
            CollectionService.AddItems(context, collection.Id, seriesIds: ids);
        }

        var vm = Make();

        var card = vm.Collections.Single();
        Assert.Equal(collage, card.HasMosaic);
        if (collage)
        {
            Assert.Equal(4, card.MosaicCovers!.Count);
        }
    }
}
