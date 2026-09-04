using Avalonia.Media;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="IssueCardSample.TileGlyph"/> - the detail-screen tile read-state badge
/// (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-design.md §4).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class IssueCardSampleTests
{
    private static IssueCardSample Card(bool isRead, double readFraction) => new()
    {
        Title = "#1",
        CoverBrush = Brushes.Transparent,
        IsRead = isRead,
        ReadFraction = readFraction,
    };

    [Fact]
    public void TileGlyph_FullyRead_IsRead()
    {
        Assert.Equal(IssueTileGlyph.Read, Card(isRead: true, readFraction: 1).TileGlyph);
        // Read wins even if a stale fraction says "in progress".
        Assert.Equal(IssueTileGlyph.Read, Card(isRead: true, readFraction: 0.4).TileGlyph);
    }

    [Fact]
    public void TileGlyph_PartlyRead_IsInProgress()
    {
        Assert.Equal(IssueTileGlyph.InProgress, Card(isRead: false, readFraction: 0.4).TileGlyph);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TileGlyph_UnstartedOrEdgeFraction_IsNone(double fraction)
    {
        Assert.Equal(IssueTileGlyph.None, Card(isRead: false, readFraction: fraction).TileGlyph);
    }
}
