using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #20 - one read-state rule behind every glyph.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReadStateGlyphTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true, false, IssueTileGlyph.Read)]
    [InlineData(true, true, IssueTileGlyph.Read)]        // Read beats InProgress
    [InlineData(false, true, IssueTileGlyph.InProgress)]
    [InlineData(false, false, IssueTileGlyph.Unread)]
    public void Resolve_PrecedenceWithoutAddedTime(bool isRead, bool inProgress, IssueTileGlyph expected)
        => Assert.Equal(expected, IssueTileGlyphs.Resolve(isRead, inProgress, null, Now));

    [Fact]
    public void Resolve_RecentlyAddedUnread_IsNew_ButOnlyWithinTheWindow()
    {
        Assert.Equal(IssueTileGlyph.New, IssueTileGlyphs.Resolve(false, false, Now.AddDays(-3), Now));
        Assert.Equal(IssueTileGlyph.New, IssueTileGlyphs.Resolve(false, false, Now.AddDays(-7), Now)); // boundary is inclusive
        Assert.Equal(IssueTileGlyph.Unread, IssueTileGlyphs.Resolve(false, false, Now.AddDays(-8), Now));
    }

    [Fact]
    public void Resolve_NewNeverOverridesReadOrInProgress()
    {
        Assert.Equal(IssueTileGlyph.Read, IssueTileGlyphs.Resolve(true, false, Now.AddDays(-1), Now));
        Assert.Equal(IssueTileGlyph.InProgress, IssueTileGlyphs.Resolve(false, true, Now.AddDays(-1), Now));
    }

    [Theory]
    [InlineData(StatusBadgeVariant.Read)]
    [InlineData(StatusBadgeVariant.InProgress)]
    [InlineData(StatusBadgeVariant.New)]
    [InlineData(StatusBadgeVariant.Unread)]
    public void StatusBadge_ExactlyOneVariantFlagIsSet(StatusBadgeVariant variant)
    {
        var badge = new StatusBadge { Variant = variant };

        var flags = new[] { badge.IsRead, badge.IsInProgress, badge.IsNew, badge.IsUnread };
        Assert.Single(flags, f => f);
        Assert.Equal(variant == StatusBadgeVariant.Unread, badge.IsUnread);
    }

    [Fact]
    public void DetailCard_TileGlyph_ShowsUnreadNotNothing()
    {
        var unread = new IssueCardSample { Title = "T", CoverBrush = Avalonia.Media.Brushes.Black };

        Assert.Equal(IssueTileGlyph.Unread, unread.TileGlyph);
    }
}
