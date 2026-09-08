using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="InsightsChartTheme"/>'s categorical palette (docs/superpowers/specs/
/// 2026-09-08-stats-v2-mangabaka-design.md §8). Joins <see cref="AvaloniaTestCollection"/> since
/// color resolution reads <c>Application.Current.Resources</c>, same seam <see cref="SkinServiceTests"/>
/// already uses.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class InsightsChartThemeTests
{
    [Fact]
    public void CategoricalPalette_HasSixDistinctSlots()
    {
        var palette = InsightsChartTheme.CategoricalPalette;

        Assert.Equal(6, palette.Count);
        Assert.Equal(6, palette.Distinct().Count());
    }

    [Fact]
    public void CategoricalPalette_ReflectsTheAppliedSkinsChartColors()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_charttheme_test_{Guid.NewGuid():N}.db");
        var dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        using (var ctx = new PaperbunkrDbContext(dbOptions))
        {
            ctx.Database.EnsureCreated();
        }

        try
        {
            var service = new SkinService(() => new PaperbunkrDbContext(dbOptions));
            service.ApplySkin(SkinService.DefaultSkinKey);

            Assert.Equal(ScottPlot.Color.FromHex("#5B8DBE"), InsightsChartTheme.Blue);
            Assert.Equal(ScottPlot.Color.FromHex("#9B7EBD"), InsightsChartTheme.Violet);
            Assert.Contains(InsightsChartTheme.Blue, InsightsChartTheme.CategoricalPalette);
            Assert.Contains(InsightsChartTheme.Violet, InsightsChartTheme.CategoricalPalette);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
