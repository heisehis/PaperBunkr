using System.Linq;
using FluentIcons.Common;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="DetailMetaBadge.Build"/> - the ordered hero badge set
/// (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-design.md Part 4).
/// </summary>
public class DetailMetaBadgeTests
{
    [Fact]
    public void Build_FullInput_ProducesEveryBadgeInOrder()
    {
        var badges = DetailMetaBadge.Build(
            publisher: "Marvel", statusLabel: "Ongoing", isComplete: false,
            year: "2024", format: "Annual", ageRating: "Teen", languageIso: "en");

        Assert.Collection(badges,
            b => { Assert.Equal(MarkFamily.Publisher, b.Mark); Assert.Equal("Marvel", b.MarkValue); },
            b => { Assert.Null(b.Mark); Assert.Equal("Ongoing", b.Text); Assert.Equal(Symbol.Circle, b.Icon); },
            b => { Assert.Equal("2024", b.Text); Assert.Equal(Symbol.Calendar, b.Icon); },
            b => Assert.Equal(MarkFamily.Format, b.Mark),
            b => Assert.Equal(MarkFamily.AgeRating, b.Mark),
            b => { Assert.Equal(MarkFamily.Language, b.Mark); Assert.Equal("en", b.MarkValue); });
    }

    [Fact]
    public void Build_IssueCountAndUnread_SitBetweenStatusAndYear()
    {
        var badges = DetailMetaBadge.Build(
            publisher: null, statusLabel: "Ongoing", isComplete: false,
            year: "2024", format: null, ageRating: null, languageIso: null,
            issueCountLabel: "42 issues", unreadLabel: "12 unread");

        Assert.Collection(badges,
            b => Assert.Equal("Ongoing", b.Text),
            b => { Assert.Equal("42 issues", b.Text); Assert.Equal(Symbol.TextBulletList, b.Icon); },
            b => { Assert.Equal("12 unread", b.Text); Assert.Equal(Symbol.CircleHalfFill, b.Icon); },
            b => Assert.Equal("2024", b.Text));
    }

    [Fact]
    public void Build_NoUnread_OmitsTheUnreadBadge()
    {
        var badges = DetailMetaBadge.Build(null, null, false, null, null, null, null,
            issueCountLabel: "5 issues", unreadLabel: null);
        var badge = Assert.Single(badges);
        Assert.Equal("5 issues", badge.Text);
    }

    [Fact]
    public void Build_Complete_UsesTheCheckmarkGlyph()
    {
        var badges = DetailMetaBadge.Build("Image", "Complete", isComplete: true, null, null, null, null);
        var status = badges.Single(b => b.Text == "Complete");
        Assert.Equal(Symbol.CheckmarkCircle, status.Icon);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_BlankSources_AreOmitted(string? blank)
    {
        var badges = DetailMetaBadge.Build(blank, blank, false, blank, blank, blank, blank);
        Assert.Empty(badges);
    }

    [Fact]
    public void Build_PartialInput_KeepsOnlyWhatIsSet()
    {
        var badges = DetailMetaBadge.Build("DC", null, false, "1986", null, null, null);
        Assert.Equal(2, badges.Count);
        Assert.Contains(badges, b => b.Mark == MarkFamily.Publisher);
        Assert.Contains(badges, b => b.Text == "1986");
    }

    [Fact]
    public void IconGlyphAndMarkOrDefault_AreNeverNull()
    {
        var mark = new DetailMetaBadge(string.Empty, Mark: MarkFamily.Publisher, MarkValue: "X");
        Assert.True(mark.IsMark);
        Assert.Equal(MarkFamily.Publisher, mark.MarkOrDefault);
        Assert.Equal(Symbol.Circle, mark.IconGlyph); // default when Icon unset

        var glyph = new DetailMetaBadge("2024", Icon: Symbol.Calendar);
        Assert.False(glyph.IsMark);
        Assert.Equal(Symbol.Calendar, glyph.IconGlyph);
    }
}
