using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>Covers <see cref="EventChronology"/> (docs/superpowers/specs/2026-09-27-continuity-map-design.md §2).</summary>
public class EventChronologyTests
{
    private static EventSpan Span(int id, string name, int? start) => new(id, name, start, start);

    [Theory]
    [InlineData(RelationType.Prequel, 1, 2)]
    [InlineData(RelationType.Sequel, 2, 1)]
    [InlineData(RelationType.Continuation, 2, 1)]
    public void Direction_FollowsTheConventions(RelationType type, int earlier, int later)
    {
        Assert.Equal((earlier, later), EventChronology.Direction(1, 2, type));
    }

    [Theory]
    [InlineData(RelationType.Crossover)]
    [InlineData(RelationType.SameUniverse)]
    [InlineData(RelationType.Related)]
    public void UnorderedTypes_HaveNoDirection(RelationType type)
    {
        Assert.Null(EventChronology.Direction(1, 2, type));
    }

    [Fact]
    public void Relations_BeatDates()
    {
        var events = new[] { Span(1, "World War Hulk", 200601), Span(2, "Planet Hulk", 200704) };

        var order = EventChronology.Order(events, new[] { (2, 1, RelationType.Prequel) });

        Assert.Equal(new[] { 2, 1 }, order);
    }

    [Fact]
    public void WithoutRelations_DateThenName()
    {
        var events = new[] { Span(1, "Zeta", 200601), Span(2, "Alpha", 200601), Span(3, "Early", 199001), Span(4, "Undated", null) };

        Assert.Equal(new[] { 3, 2, 1, 4 }, EventChronology.Order(events, Array.Empty<(int, int, RelationType)>()));
    }

    [Fact]
    public void ALoop_IsBrokenByTheEarliestDate()
    {
        var events = new[] { Span(1, "A", 200601), Span(2, "B", 200501), Span(3, "C", 200701) };
        var loop = new[] { (1, 2, RelationType.Prequel), (2, 3, RelationType.Prequel), (3, 1, RelationType.Prequel) };

        var order = EventChronology.Order(events, loop);

        Assert.Equal(3, order.Count);
        Assert.Equal(2, order[0]);        // earliest-dated breaks the loop
        Assert.Equal(new[] { 2, 3, 1 }, order);
    }
}
