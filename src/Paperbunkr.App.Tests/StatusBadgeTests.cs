using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="StatusBadge"/> - each <see cref="StatusBadgeVariant"/> resolves to exactly one of
/// the three computed flags the template switches on (docs/superpowers/specs/2026-09-06-feedback-
/// notification-system-design.md §4 / plan Step 5).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class StatusBadgeTests
{
    [Theory]
    [InlineData(StatusBadgeVariant.Read, true, false, false)]
    [InlineData(StatusBadgeVariant.InProgress, false, true, false)]
    [InlineData(StatusBadgeVariant.New, false, false, true)]
    public void Variant_SetsExactlyOneFlag(StatusBadgeVariant variant, bool expectedRead, bool expectedInProgress, bool expectedNew)
    {
        var badge = new StatusBadge { Variant = variant };

        Assert.Equal(expectedRead, badge.IsRead);
        Assert.Equal(expectedInProgress, badge.IsInProgress);
        Assert.Equal(expectedNew, badge.IsNew);
    }

    [Fact]
    public void DefaultVariant_IsReadWithoutExplicitlySettingIt()
    {
        // Rebuild() runs once from the constructor, not just on VariantProperty.Changed, so the
        // default (StatusBadgeVariant.Read == 0) resolves correctly even when a caller never sets
        // Variant explicitly - Avalonia's property system does not always raise Changed just
        // because a StyledProperty is later set to its own default value.
        var badge = new StatusBadge();

        Assert.True(badge.IsRead);
        Assert.False(badge.IsInProgress);
        Assert.False(badge.IsNew);
    }

    [Fact]
    public void ChangingVariant_UpdatesTheFlags()
    {
        var badge = new StatusBadge { Variant = StatusBadgeVariant.Read };
        Assert.True(badge.IsRead);

        badge.Variant = StatusBadgeVariant.New;

        Assert.False(badge.IsRead);
        Assert.True(badge.IsNew);
    }
}
