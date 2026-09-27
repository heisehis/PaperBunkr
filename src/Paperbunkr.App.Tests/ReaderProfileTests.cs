using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Reader profiles: state JSON, the overlay, selection, per-field precedence, built-ins and deletion (docs/superpowers/specs/2026-09-25-comic-reader-profiles-design.md).</summary>
public class ReaderProfileTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_profile_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ReaderProfileTests()
    {
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
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

    private WorkspaceService CreateService() => new(() => new PaperbunkrDbContext(_dbOptions));

    // --- State JSON ---

    [Fact]
    public void StateJson_RoundTrips_AndOmitsUnsetFields()
    {
        var state = new ReaderProfileState(FitMode: ImageFitMode.FitWidth, Brightness: -8, PagedTapZoneLayout: TapZoneLayout.Kindlish, BackgroundColor: "#101010");

        string json = ReaderProfileStateJson.Serialize(state);

        Assert.Equal(state, ReaderProfileStateJson.Deserialize(json));
        Assert.Contains("FitWidth", json);
        Assert.DoesNotContain("AutoRotate", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"FitMode\":\"NoSuchMode\"}")]
    public void StateJson_CorruptOrUnknown_SetsNothing(string? json) =>
        Assert.Equal(new ReaderProfileState(), ReaderProfileStateJson.Deserialize(json));

    [Fact]
    public void StateJson_IgnoresUnknownKeys_AndKeepsTheKnownOnes() =>
        Assert.Equal(new ReaderProfileState(Gamma: 5), ReaderProfileStateJson.Deserialize("{\"Gamma\":5,\"FromTheFuture\":true}"));

    // --- Overlay ---

    [Fact]
    public void Overlay_NoProfile_ReturnsTheSettingsThemselves()
    {
        var settings = new AppSettings();

        Assert.Same(settings, ReaderProfileOverlay.Apply(settings, null));
    }

    [Fact]
    public void Overlay_AppliesOnlyTheNonNullFields_AndNeverMutatesTheInput()
    {
        var settings = new AppSettings { DefaultPageFitMode = ImageFitMode.Original, DefaultBrightness = 3, BackgroundColor = "WhiteSmoke", MouseWheelSpeed = 4.5 };
        var profile = new ReaderProfileState(FitMode: ImageFitMode.FitHeight, Brightness: -8, ReaderChromeHoverMode: ReaderChromeHoverMode.Ambient);

        var effective = ReaderProfileOverlay.Apply(settings, profile);

        Assert.NotSame(settings, effective);
        Assert.Equal(ImageFitMode.FitHeight, effective.DefaultPageFitMode);
        Assert.Equal(-8, effective.DefaultBrightness);
        Assert.Equal(ReaderChromeHoverMode.Ambient, effective.ReaderChromeHoverMode);
        Assert.Equal("WhiteSmoke", effective.BackgroundColor);        // not set by the profile
        Assert.Equal(4.5, effective.MouseWheelSpeed);                   // copied through
        Assert.Equal(ImageFitMode.Original, settings.DefaultPageFitMode); // the input is untouched
        Assert.Equal(3, settings.DefaultBrightness);
    }

    [Fact]
    public void Overlay_MapsEveryProfileFieldOntoItsSetting()
    {
        var profile = new ReaderProfileState(
            ImageFitMode.BestFit, true, PageLayoutMode.Double, PageTransitionStyle.Crossfade, 400, 1, 2, 3, 4,
            ImageBackgroundMode.Texture, "#123456", "linen", true, 0.1, true, ReaderChromeHoverMode.Ambient,
            TapZoneLayout.Edge, TapZoneInvert.Both, TapZoneLayout.LShaped, TapZoneInvert.Vertical);

        var e = ReaderProfileOverlay.Apply(new AppSettings(), profile);

        Assert.Equal(ImageFitMode.BestFit, e.DefaultPageFitMode);
        Assert.True(e.DefaultAutoRotate);
        Assert.Equal(PageLayoutMode.Double, e.DefaultPageLayoutMode);
        Assert.Equal(PageTransitionStyle.Crossfade, e.PageTransitionStyle);
        Assert.Equal(400, e.PageTransitionDurationMs);
        Assert.Equal((1d, 2d, 3d, 4d), (e.DefaultBrightness, e.DefaultContrast, e.DefaultSaturation, e.DefaultGamma));
        Assert.Equal(ImageBackgroundMode.Texture, e.ImageBackgroundMode);
        Assert.Equal("#123456", e.BackgroundColor);
        Assert.Equal("linen", e.BackgroundTexture);
        Assert.True(e.PageMarginEnabled);
        Assert.Equal(0.1, e.PageMarginPercentWidth);
        Assert.True(e.ReaderAutoHideChrome);
        Assert.Equal(ReaderChromeHoverMode.Ambient, e.ReaderChromeHoverMode);
        Assert.Equal(TapZoneLayout.Edge, e.PagedTapZoneLayout);
        Assert.Equal(TapZoneInvert.Both, e.PagedTapZoneInvert);
        Assert.Equal(TapZoneLayout.LShaped, e.ContinuousTapZoneLayout);
        Assert.Equal(TapZoneInvert.Vertical, e.ContinuousTapZoneInvert);
    }

    // --- Selection ---

    private static Workspace Row(int id, string name = "P") => new() { Id = id, Screen = WorkspaceScreen.Reader, Name = name };

    [Fact]
    public void Select_PrefersSession_ThenSeries_ThenDefault()
    {
        var rows = new[] { Row(1), Row(2), Row(3) };

        Assert.Equal((1, true), Pick(rows, session: 1, series: 2, def: 3));
        Assert.Equal((2, false), Pick(rows, session: null, series: 2, def: 3));
        Assert.Equal((3, false), Pick(rows, session: null, series: null, def: 3));
        Assert.Equal((null, false), Pick(rows, null, null, null));
    }

    [Fact]
    public void Select_StandardSession_MeansNoProfileAtAll()
    {
        var rows = new[] { Row(2), Row(3) };

        Assert.Equal((null, false), Pick(rows, session: ReaderProfileSelector.StandardSessionId, series: 2, def: 3));
    }

    [Fact]
    public void Select_MissingRows_FallThroughToTheNextCandidate()
    {
        var rows = new[] { Row(3) };

        Assert.Equal((3, false), Pick(rows, session: null, series: 99, def: 3));
        Assert.Equal((3, false), Pick(rows, session: 98, series: 99, def: 3));
        Assert.Equal((null, false), Pick(rows, session: 98, series: 99, def: 97));
    }

    [Fact]
    public void Select_IgnoresRowsOfOtherScreens()
    {
        var rows = new[] { new Workspace { Id = 5, Screen = WorkspaceScreen.Library, Name = "Not a reader profile" } };

        Assert.Equal((null, false), Pick(rows, session: 5, series: 5, def: 5));
    }

    private static (int? Id, bool IsSession) Pick(IReadOnlyList<Workspace> rows, int? session, int? series, int? def)
    {
        var selection = ReaderProfileSelector.Select(rows, session, series, def);
        return (selection.Row?.Id, selection.IsSession);
    }

    [Fact]
    public void Resolve_ReadsTheRowsFromTheDatabase_AndOverlaysThem()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var service = CreateService();
        var created = service.Create(WorkspaceScreen.Reader, "Mine", ReaderProfileStateJson.Serialize(new ReaderProfileState(FitMode: ImageFitMode.FitHeight)));
        var series = new Series { Name = "S", ReaderProfileId = created.Id };
        var settings = context.GetOrCreateAppSettings();

        var resolved = ReaderProfileSelector.Resolve(context, series, settings, sessionId: null);

        Assert.Equal("Mine", resolved.Name);
        Assert.False(resolved.IsSession);
        Assert.Null(resolved.SessionState);
        Assert.Equal(ImageFitMode.FitHeight, resolved.Effective.DefaultPageFitMode);
        Assert.NotEqual(ImageFitMode.FitHeight, settings.DefaultPageFitMode);   // the tracked entity is untouched
    }

    [Fact]
    public void Resolve_ASessionProfile_ExposesItsStateForPrecedence()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var created = CreateService().Create(WorkspaceScreen.Reader, "Mine", ReaderProfileStateJson.Serialize(new ReaderProfileState(Brightness: 12)));

        var resolved = ReaderProfileSelector.Resolve(context, null, context.GetOrCreateAppSettings(), created.Id);

        Assert.True(resolved.IsSession);
        Assert.Equal(12, resolved.SessionState?.Brightness);
    }

    [Fact]
    public void Resolve_NoCandidates_IsPlainSettings()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var settings = context.GetOrCreateAppSettings();

        var resolved = ReaderProfileSelector.Resolve(context, new Series { Name = "S" }, settings, sessionId: null);

        Assert.Null(resolved.Row);
        Assert.Same(settings, resolved.Effective);
    }

    // --- Precedence ---

    [Fact]
    public void FitMode_SessionBeatsIssueAndSeries_ThenTheChainAsBefore()
    {
        var settings = new AppSettings { DefaultPageFitMode = ImageFitMode.Original };
        var series = new Series { Name = "S", PageFitModeOverride = ImageFitMode.FitWidth };
        var issue = new Issue { PageFitModeOverride = ImageFitMode.FitHeight };
        var session = new ReaderProfileState(FitMode: ImageFitMode.BestFit);

        Assert.Equal(ImageFitMode.BestFit, ReaderProfileResolution.FitMode(session, issue, series, settings));
        Assert.Equal(ImageFitMode.FitHeight, ReaderProfileResolution.FitMode(null, issue, series, settings));
        Assert.Equal(ImageFitMode.FitWidth, ReaderProfileResolution.FitMode(null, new Issue(), series, settings));
        Assert.Equal(ImageFitMode.Original, ReaderProfileResolution.FitMode(null, new Issue(), new Series { Name = "S" }, settings));
        // A session profile that does not set fit mode leaves the chain alone.
        Assert.Equal(ImageFitMode.FitHeight, ReaderProfileResolution.FitMode(new ReaderProfileState(Brightness: 1), issue, series, settings));
    }

    [Fact]
    public void AutoRotate_And_LayoutMode_FollowTheSamePrecedence()
    {
        var settings = new AppSettings { DefaultAutoRotate = false, DefaultPageLayoutMode = PageLayoutMode.Single };
        var series = new Series { Name = "S", AutoRotateOverride = true, PageLayoutMode = PageLayoutMode.Double };
        var issue = new Issue { AutoRotateOverride = false, PageLayoutModeOverride = PageLayoutMode.Single };
        var session = new ReaderProfileState(AutoRotate: true, PageLayoutMode: PageLayoutMode.Double);

        Assert.True(ReaderProfileResolution.AutoRotate(session, issue, series, settings));
        Assert.False(ReaderProfileResolution.AutoRotate(null, issue, series, settings));
        Assert.Equal(PageLayoutMode.Double, ReaderProfileResolution.LayoutMode(session, issue, series, settings));
        Assert.Equal(PageLayoutMode.Single, ReaderProfileResolution.LayoutMode(null, issue, series, settings));
        Assert.Equal(PageLayoutMode.Double, ReaderProfileResolution.LayoutMode(null, new Issue(), series, settings));
        Assert.Equal(PageLayoutMode.Single, ReaderProfileResolution.LayoutMode(null, new Issue(), new Series { Name = "S" }, settings));
    }

    [Fact]
    public void Adjustment_SessionIsAbsolute_OtherwiseDefaultPlusTheIssueDelta()
    {
        Assert.Equal(new ReaderProfileResolution.Adjustment(-8, -8), ReaderProfileResolution.Adjust(-8, 5f, 20));
        Assert.Equal(new ReaderProfileResolution.Adjustment(20, 25), ReaderProfileResolution.Adjust(null, 5f, 20));
        Assert.Equal(new ReaderProfileResolution.Adjustment(20, 20), ReaderProfileResolution.Adjust(null, null, 20));
    }

    // --- Built-ins and storage ---

    [Fact]
    public void EnsureBuiltInsSeeded_SeedsTheThreeReaderProfiles_Once()
    {
        var service = CreateService();

        service.EnsureBuiltInsSeeded();
        service.EnsureBuiltInsSeeded();

        var rows = service.List(WorkspaceScreen.Reader);
        Assert.Equal(["Manga night", "Webtoon", "Tablet"], rows.Select(r => r.Name));
        Assert.All(rows, r => Assert.True(r.IsBuiltIn));
        var manga = ReaderProfileStateJson.Deserialize(rows[0].StateJson);
        Assert.Equal(ImageBackgroundMode.Color, manga.ImageBackgroundMode);
        Assert.Equal(-8, manga.Brightness);
        Assert.Null(manga.FitMode);
        Assert.Equal(ImageFitMode.FitWidth, ReaderProfileStateJson.Deserialize(rows[1].StateJson).FitMode);
        Assert.Equal(TapZoneLayout.Kindlish, ReaderProfileStateJson.Deserialize(rows[2].StateJson).PagedTapZoneLayout);
    }

    [Fact]
    public void ReaderProfiles_DoNotAppearInTheOtherScreensLists()
    {
        var service = CreateService();
        service.EnsureBuiltInsSeeded();

        Assert.DoesNotContain(service.List(WorkspaceScreen.Library), w => w.Name == "Manga night");
        Assert.DoesNotContain(service.List(WorkspaceScreen.Books), w => w.Name == "Webtoon");
    }

    [Fact]
    public void BuiltInProfiles_CannotBeRenamedUpdatedOrDeleted()
    {
        var service = CreateService();
        service.EnsureBuiltInsSeeded();
        var builtIn = service.List(WorkspaceScreen.Reader)[0];

        service.Rename(builtIn.Id, "Hacked");
        service.UpdateState(builtIn.Id, "{}");
        service.Delete(builtIn.Id);

        var after = service.List(WorkspaceScreen.Reader)[0];
        Assert.Equal("Manga night", after.Name);
        Assert.NotEqual("{}", after.StateJson);
    }

    [Fact]
    public void DeletingTheDefaultProfile_ClearsTheDefaultPointer()
    {
        var service = CreateService();
        var created = service.Create(WorkspaceScreen.Reader, "Mine", "{}");
        var other = service.Create(WorkspaceScreen.Reader, "Other", "{}");
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().DefaultReaderProfileId = created.Id;
            context.SaveChanges();
        }

        service.Delete(other.Id);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            Assert.Equal(created.Id, context.GetOrCreateAppSettings().DefaultReaderProfileId);
        }

        service.Delete(created.Id);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            Assert.Null(context.GetOrCreateAppSettings().DefaultReaderProfileId);
        }
    }

    [Fact]
    public void ProfilePointers_PersistOnSeriesAndSettings()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "S", ReaderProfileId = 7 };
            context.Series.Add(series);
            context.GetOrCreateAppSettings().DefaultReaderProfileId = 9;
            context.SaveChanges();
        }

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            Assert.Equal(7, context.Series.Single().ReaderProfileId);
            Assert.Equal(9, context.GetOrCreateAppSettings().DefaultReaderProfileId);
        }
    }
}
