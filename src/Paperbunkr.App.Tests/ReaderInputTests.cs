using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Reading-order page commands, their extra default gestures, conflict contexts and the tap zone overlay geometry (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md).</summary>
public class ReaderInputTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_input_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ReaderInputTests()
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

    private KeyBindingService CreateService() => new(() => new PaperbunkrDbContext(_dbOptions));

    [Fact]
    public void NextAndPreviousPage_ShipClickerMediaAndSpaceDefaults()
    {
        var service = CreateService();

        Assert.Equal(
            [new KeyGesture(Key.PageDown), new KeyGesture(Key.Space), new KeyGesture(Key.MediaNextTrack)],
            service.GetKeys(KeyboardCommandRegistry.ReaderNextPage));
        Assert.Equal(
            [new KeyGesture(Key.PageUp), new KeyGesture(Key.Space, KeyModifiers.Shift), new KeyGesture(Key.MediaPreviousTrack)],
            service.GetKeys(KeyboardCommandRegistry.ReaderPreviousPage));
    }

    [Fact]
    public void StoredRows_ReplaceTheAdditionalDefaults()
    {
        var service = CreateService();

        service.AddKey(KeyboardCommandRegistry.ReaderNextPage, new KeyGesture(Key.D));

        Assert.Equal([new KeyGesture(Key.D)], service.GetKeys(KeyboardCommandRegistry.ReaderNextPage));
    }

    [Fact]
    public void ResetToDefaults_RestoresTheAdditionalDefaults()
    {
        var service = CreateService();
        service.ReplaceKeys(KeyboardCommandRegistry.ReaderNextPage, [new KeyGesture(Key.D)]);

        service.ResetToDefaults();

        Assert.Equal(3, service.GetKeys(KeyboardCommandRegistry.ReaderNextPage).Count);
    }

    [Fact]
    public void ReadingOrderCommands_AreBoundToPagedModeOnly()
    {
        Assert.All(
            KeyboardCommandRegistry.Commands.Where(c => c.Id is KeyboardCommandRegistry.ReaderNextPage or KeyboardCommandRegistry.ReaderPreviousPage),
            c => Assert.Equal(ConflictContext.Paged, c.Context));
    }

    [Theory]
    [InlineData(ConflictContext.Always, ConflictContext.Continuous, true)]
    [InlineData(ConflictContext.Paged, ConflictContext.PagedUnzoomed, true)]
    [InlineData(ConflictContext.PagedZoomed, ConflictContext.Paged, true)]
    [InlineData(ConflictContext.Paged, ConflictContext.Paged, true)]
    [InlineData(ConflictContext.Paged, ConflictContext.Continuous, false)]
    [InlineData(ConflictContext.PagedUnzoomed, ConflictContext.PagedZoomed, false)]
    [InlineData(ConflictContext.Continuous, ConflictContext.Continuous, true)]
    public void MayOverlap_MatchesRuntimeExclusivity(ConflictContext a, ConflictContext b, bool expected) =>
        Assert.Equal(expected, ConflictContexts.MayOverlap(a, b));

    [Fact]
    public void NoTwoCommandsThatCanBeActiveTogetherShareADefaultGesture()
    {
        var all = KeyboardCommandRegistry.Commands;
        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                if (!ConflictContexts.MayOverlap(all[i].Context, all[j].Context))
                {
                    continue;
                }

                var shared = all[i].AllDefaults.Intersect(all[j].AllDefaults).ToList();
                Assert.True(shared.Count == 0, $"{all[i].Id} and {all[j].Id} both default to {string.Join(", ", shared)}");
            }
        }
    }

    [Fact]
    public void KeyOptions_ContainEveryShippedDefault()
    {
        var offered = KeyOptions.All.Select(o => o.Gesture).ToHashSet();
        foreach (var command in KeyboardCommandRegistry.Commands.Where(c => c.AdditionalDefaults is { Count: > 0 }))
        {
            Assert.All(command.AllDefaults, g => Assert.Contains(g, offered));
        }
    }

    // --- Overlay geometry (sampled from the resolver, so it shows exactly what a tap does) ---

    [Fact]
    public void Overlay_DisabledLayout_HasNoRegions() =>
        Assert.Empty(TapZoneOverlay.BuildRegions(TapZoneLayout.Disabled, TapZoneInvert.None, false, false));

    [Fact]
    public void Overlay_ContinuousDefault_HasNoRegions() =>
        Assert.Empty(TapZoneOverlay.BuildRegions(TapZoneLayout.Default, TapZoneInvert.None, false, true));

    [Fact]
    public void Overlay_DefaultPaged_IsThreeColumns()
    {
        var regions = TapZoneOverlay.BuildRegions(TapZoneLayout.Default, TapZoneInvert.None, false, false);

        Assert.Equal([TapAction.Left, TapAction.Menu, TapAction.Right], regions.Select(r => r.Action));
        Assert.All(regions, r => Assert.Equal(1.0, r.Rect.Height, 6));
    }

    [Fact]
    public void Overlay_LShaped_CoversTheWholeFrameWithoutOverlap()
    {
        var regions = TapZoneOverlay.BuildRegions(TapZoneLayout.LShaped, TapZoneInvert.None, false, false);

        Assert.Equal(1.0, regions.Sum(r => r.Rect.Width * r.Rect.Height), 6);
        Assert.Contains(regions, r => r.Action == TapAction.Menu);
        Assert.Contains(regions, r => r.Action == TapAction.Previous);
        Assert.Contains(regions, r => r.Action == TapAction.Next);
    }

    [Fact]
    public void Overlay_RightToLeft_SwapsPreviousAndNextSides()
    {
        var ltr = TapZoneOverlay.BuildRegions(TapZoneLayout.Kindlish, TapZoneInvert.None, false, false);
        var rtl = TapZoneOverlay.BuildRegions(TapZoneLayout.Kindlish, TapZoneInvert.None, true, false);

        Assert.Equal(TapAction.Previous, ltr.First(r => r.Rect.X < 0.1 && r.Rect.Y > 0.3).Action);
        Assert.Equal(TapAction.Previous, rtl.First(r => r.Rect.Right > 0.9 && r.Rect.Y > 0.3).Action);
    }

    [Theory]
    [InlineData(TapAction.Previous, "Previous")]
    [InlineData(TapAction.Next, "Next")]
    [InlineData(TapAction.Menu, "Menu")]
    [InlineData(TapAction.Left, "Left")]
    [InlineData(TapAction.Right, "Right")]
    [InlineData(TapAction.None, "")]
    public void Overlay_LabelFor(TapAction action, string expected) => Assert.Equal(expected, TapZoneOverlay.LabelFor(action));
}
