using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>First test coverage for <see cref="DetailMetaBadgeGroup"/> (docs/superpowers/specs/
/// 2026-09-13-detail-screens-redesign-design.md §1) - caps the hero band's badge row at 3 with a
/// "+N more" expander.</summary>
public class DetailMetaBadgeGroupTests
{
    private static DetailMetaBadge Badge(string text) => new(text);

    [Fact]
    public void EightBadges_ShowsThreeWithOverflowOfFive()
    {
        var all = Enumerable.Range(1, 8).Select(i => Badge($"B{i}")).ToList();
        var group = new DetailMetaBadgeGroup(all);

        Assert.Equal(3, group.Visible.Count);
        Assert.Equal(5, group.OverflowCount);
        Assert.True(group.HasOverflow);
        Assert.Equal(8, group.TotalCount);
    }

    [Fact]
    public void TwoBadges_NoOverflow_AllVisible()
    {
        var group = new DetailMetaBadgeGroup(new[] { Badge("A"), Badge("B") });

        Assert.Equal(2, group.Visible.Count);
        Assert.False(group.HasOverflow);
        Assert.Equal(0, group.OverflowCount);
    }

    [Fact]
    public void ToggleExpand_ShowsAllBadges_AndFlipsLabel()
    {
        var all = Enumerable.Range(1, 8).Select(i => Badge($"B{i}")).ToList();
        var group = new DetailMetaBadgeGroup(all);

        Assert.Equal("+5 more", group.MoreLabel);

        group.ToggleExpandCommand.Execute(null);

        Assert.True(group.IsExpanded);
        Assert.Equal(8, group.Visible.Count);
        Assert.Equal("show less", group.MoreLabel);

        group.ToggleExpandCommand.Execute(null);

        Assert.False(group.IsExpanded);
        Assert.Equal(3, group.Visible.Count);
    }

    [Fact]
    public void EmptyGroup_NoOverflow_NoVisible()
    {
        var group = new DetailMetaBadgeGroup(Array.Empty<DetailMetaBadge>());

        Assert.Empty(group.Visible);
        Assert.False(group.HasOverflow);
        Assert.Equal(0, group.TotalCount);
    }
}
