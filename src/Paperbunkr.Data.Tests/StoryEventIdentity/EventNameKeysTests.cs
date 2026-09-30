using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>Covers <see cref="EventNameKeys"/> (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §2, Q10).</summary>
public class EventNameKeysTests
{
    [Theory]
    [InlineData("Hulk: Planet Hulk")]
    [InlineData("Hulk - Planet Hulk")]
    [InlineData("Hulk – Planet Hulk")]
    [InlineData("Incredible Hulk: Planet Hulk")]
    public void SeriesPrefix_IsStripped_WhenItNamesAMemberSeries(string name)
    {
        var keys = EventNameKeys.For(name, new[] { "Hulk", "Incredible Hulk (1999)" });

        Assert.Contains("planethulk", keys);
    }

    [Fact]
    public void ColonTitle_IsKept_WhenThePrefixIsNotAMemberSeries()
    {
        var keys = EventNameKeys.For("Cataclysm: The Ultimates' Last Stand", new[] { "Ultimate Spider-Man", "Ultimates" });

        Assert.Equal(new[] { "cataclysmultimateslaststand" }, keys);
    }

    [Fact]
    public void YearSuffix_IsDropped()
    {
        Assert.Contains("secretwars", EventNameKeys.For("Secret Wars (2015)", Array.Empty<string>()));
    }

    [Fact]
    public void AliasKeys_JoinTheSet()
    {
        var keys = EventNameKeys.For("Planet Hulk", Array.Empty<string>(), new[] { "hulkplanethulk" });

        Assert.True(keys.SetEquals(new[] { "planethulk", "hulkplanethulk" }));
    }

    [Fact]
    public void ComicVineAndMetronSpellings_Overlap_OnlyWithTheSeriesKnown()
    {
        var series = new[] { "Hulk" };

        Assert.True(EventNameKeys.For("Hulk: Planet Hulk", series).Overlaps(EventNameKeys.For("Planet Hulk", series)));
        Assert.False(EventNameKeys.For("Hulk: Planet Hulk", Array.Empty<string>()).Overlaps(EventNameKeys.For("Planet Hulk", Array.Empty<string>())));
    }
}
