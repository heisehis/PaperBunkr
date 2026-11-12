using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The badge store and the tile collection source on the card samples (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CollectionBadgeSourceTests : IDisposable
{
    public CollectionBadgeSourceTests() => CollectionBadgeStore.Clear();

    public void Dispose() => CollectionBadgeStore.Clear();

    private static CollectionMemberships Memberships(Dictionary<int, IReadOnlyList<int>> bySeries, Dictionary<int, IReadOnlyList<int>>? byIssue = null) =>
        new(bySeries, byIssue ?? new Dictionary<int, IReadOnlyList<int>>());

    [Fact]
    public void Tooltip_ListsNames_AndIsNullWhenEmpty()
    {
        Assert.Null(CollectionBadgeText.Tooltip(Array.Empty<CollectionBadge>()));
        var badges = new[] { new CollectionBadge(Color.FromRgb(1, 2, 3), "Events"), new CollectionBadge(null, "Favourites") };
        Assert.Equal("In: Events, Favourites", CollectionBadgeText.Tooltip(badges));
    }

    [Fact]
    public void Store_IsEmptyByDefault_AndAfterClear()
    {
        Assert.Empty(CollectionBadgeStore.ForSeries(1));
        Assert.Empty(CollectionBadgeStore.ForIssue(1, 1));

        CollectionBadgeStore.Set(Memberships(new() { [1] = new[] { 10 } }), new[] { new Collection { Id = 10, Name = "A" } });
        Assert.Single(CollectionBadgeStore.ForSeries(1));
        CollectionBadgeStore.Clear();
        Assert.Empty(CollectionBadgeStore.ForSeries(1));
    }

    [Fact]
    public void Store_ResolvesAccentsAndNames_InTheGivenOrder()
    {
        CollectionBadgeStore.Set(
            Memberships(new() { [1] = new[] { 20, 10 } }),
            new[] { new Collection { Id = 10, Name = "Plain" }, new Collection { Id = 20, Name = "Pink", AccentColor = "#C97C9E" } });

        var badges = CollectionBadgeStore.ForSeries(1);

        Assert.Equal(new[] { "Pink", "Plain" }, badges.Select(b => b.Name));
        Assert.Equal(Color.Parse("#C97C9E"), badges[0].Accent);
        Assert.Null(badges[1].Accent);
    }

    [Fact]
    public void AnIssue_ListsItsOwnThenItsSeries_WithoutDuplicates()
    {
        CollectionBadgeStore.Set(
            Memberships(new() { [1] = new[] { 10, 20 } }, new() { [100] = new[] { 20, 30 } }),
            new[] { new Collection { Id = 10, Name = "A" }, new Collection { Id = 20, Name = "B" }, new Collection { Id = 30, Name = "C" } });

        Assert.Equal(new[] { "B", "C", "A" }, CollectionBadgeStore.ForIssue(100, 1).Select(b => b.Name));
        Assert.Equal(new[] { "A", "B" }, CollectionBadgeStore.ForIssue(101, 1).Select(b => b.Name)); // via its series only
        Assert.Equal(new[] { "B", "C" }, CollectionBadgeStore.ForIssue(100, 2).Select(b => b.Name)); // its own only
    }

    [Fact]
    public void TheSeriesCardSample_ReadsTheStore()
    {
        var series = new Series { Id = 7, Name = "S" };
        ITileCollectionSource sample = SeriesCardSample.FromSeries(series);
        Assert.Empty(sample.CollectionBadges);
        Assert.Null(sample.CollectionBadgeTooltip);

        CollectionBadgeStore.Set(Memberships(new() { [7] = new[] { 10 } }), new[] { new Collection { Id = 10, Name = "A" } });

        Assert.Equal("In: A", sample.CollectionBadgeTooltip);
    }
}
