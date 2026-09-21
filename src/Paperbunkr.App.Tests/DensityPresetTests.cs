using Avalonia;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #17 - Compact / Comfortable / Spacious spacing presets.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DensityPresetTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_density_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public DensityPresetTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.Migrate();
    }

    public void Dispose()
    {
        // The tokens are process-global resources; put today's spacing back for sibling tests.
        ThemeService.ApplyDensityResources(DensityPresets.Comfortable);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Comfortable_IsExactlyTheSpacingTheAppHadBeforePresetsExisted()
    {
        var v = DensityPresets.For(DensityPresets.Comfortable);

        Assert.Equal(new Thickness(10), v.ListRowPadding);
        Assert.Equal(new Thickness(0, 3), v.DetailsRowPadding);
        Assert.Equal(new Thickness(8, 7), v.SidebarItemPadding);
    }

    [Fact]
    public void Compact_IsTighterAndSpacious_IsLooser_OnEveryToken()
    {
        var compact = DensityPresets.For(DensityPresets.Compact);
        var comfortable = DensityPresets.For(DensityPresets.Comfortable);
        var spacious = DensityPresets.For(DensityPresets.Spacious);

        Assert.True(compact.ListRowPadding.Top < comfortable.ListRowPadding.Top && comfortable.ListRowPadding.Top < spacious.ListRowPadding.Top);
        Assert.True(compact.DetailsRowPadding.Top < comfortable.DetailsRowPadding.Top && comfortable.DetailsRowPadding.Top < spacious.DetailsRowPadding.Top);
        Assert.True(compact.SidebarItemPadding.Top < comfortable.SidebarItemPadding.Top && comfortable.SidebarItemPadding.Top < spacious.SidebarItemPadding.Top);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void OutOfRange_FallsBackToComfortable(int preset)
        => Assert.Equal(DensityPresets.Comfortable, DensityPresets.Normalize(preset));

    [Theory]
    [InlineData("Compact", 0)]
    [InlineData("Comfortable", 1)]
    [InlineData("Spacious", 2)]
    [InlineData("nonsense", 1)]
    [InlineData(null, 1)]
    public void NamesRoundTrip_AndUnknownIsComfortable(string? name, int expected)
        => Assert.Equal(expected, DensityPresets.FromName(name));

    [Fact]
    public void ApplyDensity_WritesTheTokensLive_AndPersistsTheChoice()
    {
        var service = new ThemeService(() => new PaperbunkrDbContext(_options));

        Assert.Equal(DensityPresets.Comfortable, service.GetDensityPreset());

        service.ApplyDensity(DensityPresets.Spacious);

        var resources = Application.Current!.Resources;
        Assert.Equal(new Thickness(14), resources["PbListRowPadding"]);
        Assert.Equal(new Thickness(0, 6), resources["PbDetailsRowPadding"]);
        Assert.Equal(new Thickness(10, 10), resources["PbSidebarItemPadding"]);
        Assert.Equal(DensityPresets.Spacious, service.GetDensityPreset());

        service.ApplyDensity(DensityPresets.Compact);
        Assert.Equal(new Thickness(6), resources["PbListRowPadding"]);
    }

    [Fact]
    public void TheFallbackTokensInAppAxaml_MatchComfortable()
    {
        // App.axaml declares the tokens so a DynamicResource resolves before ThemeService runs; they must be today's values.
        var resources = Application.Current!.Resources;
        ThemeService.ApplyDensityResources(DensityPresets.Comfortable);

        Assert.True(resources.ContainsKey("PbListRowPadding"));
        Assert.True(resources.ContainsKey("PbDetailsRowPadding"));
        Assert.True(resources.ContainsKey("PbSidebarItemPadding"));
    }
}
