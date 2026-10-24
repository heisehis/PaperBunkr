using Paperbunkr.App.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>The Insights Today redesign's logic (docs/superpowers/specs/2026-10-20-insights-today-redesign-design.md): the spotlight source rules, the
/// short subtitle under a shelf cover, and the responsive tile columns.</summary>
public class InsightsTodayTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;

    public InsightsTodayTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_insights_today_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
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

    private sealed class FakeDialogService : IDialogService
    {
        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(0);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(true);
    }

    private static InsightsScreenViewModel NewVm(Action<int>? goReader = null, Action<int>? goDetail = null) =>
        new(goReader ?? (_ => { }), goDetail ?? (_ => { }), _ => { }, () => { }, new FakeDialogService(),
            nowUtc: () => new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData("own 100 issues · #1–100, never opened", "own 100 issues")]
    [InlineData("1 issue left", "1 issue left")]
    [InlineData("", "")]
    [InlineData(" · leading separator", " · leading separator")]
    public void ShortSubtitle_IsThePartBeforeTheFirstSeparator(string subtitle, string expected) =>
        Assert.Equal(expected, new AttentionRow("T", subtitle, null, 1, null).ShortSubtitle);

    [Fact]
    public void Spotlight_IsEmpty_WithNothingToContinueOrRecommend()
    {
        var vm = NewVm();

        Assert.False(vm.HasSpotlight);
        Assert.False(vm.SpotlightIsContinue);
        Assert.Equal(string.Empty, vm.SpotlightTitle);
    }

    [Fact]
    public void Spotlight_ShowsTheSeriesBeingContinued_AndOpensItsResumeIssue()
    {
        int? opened = null;
        var vm = NewVm(goReader: id => opened = id);
        vm.ContinueRows.Add(new AttentionRow("Convergence", "0 of 9 read", 42, 7, "key"));

        // The property change comes from a refresh in the app; reading the properties directly is what the view's bindings do after it.
        Assert.True(vm.HasSpotlight);
        Assert.True(vm.SpotlightIsContinue);
        Assert.Equal("CONTINUE READING", vm.SpotlightHeading);
        Assert.Equal("Convergence", vm.SpotlightTitle);
        Assert.Equal("0 of 9 read", vm.SpotlightSubtitle);
        Assert.Equal("key", vm.SpotlightCoverKey);
        Assert.Equal("Continue reading", vm.SpotlightActionLabel);

        vm.OpenSpotlightCommand.Execute(null);
        Assert.Equal(42, opened);
    }

    [Theory]
    [InlineData(1240, 3, 3)]    // plenty of room and three tiles: three across
    [InlineData(1240, 2, 2)]    // two visible tiles share the row instead of leaving a gap
    [InlineData(1240, 1, 1)]
    [InlineData(700, 3, 2)]     // only two 340 px columns fit in 700
    [InlineData(500, 3, 1)]
    [InlineData(2400, 8, 3)]    // never past MaxColumns
    public void Columns_FollowTheWidth_NeverMoreThanTheTilesOrTheMaximum(double width, int tiles, int expected) =>
        Assert.Equal(expected, ResponsiveColumnsPanel.ColumnsFor(width, 340, 12, 3, tiles));

    [Fact]
    public void Columns_AreAtLeastOne_EvenWithNoChildren() =>
        Assert.Equal(1, ResponsiveColumnsPanel.ColumnsFor(100, 340, 12, 3, 0));
}
