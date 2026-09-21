using Avalonia.Controls;
using FluentIcons.Common;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #10 - one colour + icon language for series status.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SeriesStatusStyleTests
{
    [Theory]
    [InlineData("Ongoing", Symbol.PlayCircle, "PbSuccessBrush")]
    [InlineData("Completed", Symbol.CheckmarkCircle, "PbAccentTextBrush")]
    [InlineData("Hiatus", Symbol.PauseCircle, "PbBadgeBrush")]
    [InlineData("Cancelled", Symbol.DismissCircle, "PbDangerBrush")]
    public void Each_KnownStatus_HasItsOwnIconAndThemeBrush(string status, Symbol icon, string brushKey)
    {
        var style = SeriesStatusStyle.For(status);

        Assert.NotNull(style);
        Assert.Equal(status, style!.Label);
        Assert.Equal(icon, style.Icon);
        Assert.Equal(brushKey, style.ForegroundKey);
        Assert.EndsWith("SoftBrush", style.BackgroundKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("SomethingElse")]
    public void Unknown_Or_Missing_HasNoStyle(string? status) => Assert.Null(SeriesStatusStyle.For(status));

    [Fact]
    public void FourStatuses_AreVisuallyDistinct()
    {
        var styles = new[] { "Ongoing", "Completed", "Hiatus", "Cancelled" }.Select(SeriesStatusStyle.For).ToList();

        Assert.Equal(4, styles.Select(s => s!.Icon).Distinct().Count());
        Assert.Equal(4, styles.Select(s => s!.ForegroundKey).Distinct().Count());
    }

    [Fact]
    public void Chip_ShowsOnlyForARealStatus_AndCarriesTheLabel()
    {
        var chip = new SeriesStatusChip();
        Assert.False(chip.IsVisible, "a new chip with no status starts hidden");

        chip.Status = "Hiatus";
        Assert.True(chip.IsVisible, "a real status (Hiatus) makes the chip visible");
        var panel = Assert.IsType<StackPanel>(chip.Child);
        var text = Assert.Single(panel.Children.OfType<TextBlock>());
        Assert.Equal("Hiatus", text.Text);

        chip.Status = "Unknown";
        Assert.False(chip.IsVisible, "Unknown hides the chip again");
    }

    [Fact]
    public void DetailBadge_UsesTheStatusChip_OnlyForARealStatus()
    {
        var real = DetailMetaBadge.Build(null, "Ongoing", false, null, null, null, null, statusKind: "Ongoing");
        var badge = Assert.Single(real);
        Assert.True(badge.IsStatus);
        Assert.False(badge.IsPlain);

        // A comic series whose status was never set keeps the generic chip it always had.
        var fallback = DetailMetaBadge.Build(null, "Ongoing", false, null, null, null, null, statusKind: "Unknown");
        var plain = Assert.Single(fallback);
        Assert.False(plain.IsStatus);
        Assert.True(plain.IsPlain);
    }
}
