using Paperbunkr.App.ViewModels.EntityPages;

namespace Paperbunkr.App.Tests.EntityPages;

/// <summary>docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md §1 bugs: "1 appearances", "2013 · · 1 issue", "CoverArtist".</summary>
public class EntityPageTextTests
{
    [Theory]
    [InlineData(1, "1 appearance")]
    [InlineData(0, "0 appearances")]
    [InlineData(1234, "1,234 appearances")]
    public void Count_Pluralises(int count, string expected) =>
        Assert.Equal(expected, EntityPageText.Count(count, "appearance", "appearances"));

    [Fact]
    public void JoinDetail_DropsEmptyParts() =>
        Assert.Equal("2013 · 1 issue", EntityPageText.JoinDetail("2013", "", " ", null, "1 issue"));

    [Theory]
    [InlineData("CoverArtist", "Cover artist")]
    [InlineData("Penciller", "Penciller")]
    [InlineData("writer", "Writer")]
    [InlineData("Cover Artist", "Cover artist")]
    [InlineData(null, "Other")]
    public void RoleName_IsReadable(string? raw, string expected) => Assert.Equal(expected, EntityPageText.RoleName(raw));

    [Fact]
    public void YearSpan_CollapsesOneYear()
    {
        Assert.Equal("2009–2026", EntityPageText.YearSpan(2009, 2026));
        Assert.Equal("2009", EntityPageText.YearSpan(2009, 2009));
        Assert.Equal(string.Empty, EntityPageText.YearSpan(null, null));
    }
}
