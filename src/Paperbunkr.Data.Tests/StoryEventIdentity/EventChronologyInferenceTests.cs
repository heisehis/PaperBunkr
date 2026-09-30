using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>Covers <see cref="EventChronologyInference"/> (docs/superpowers/specs/2026-09-27-continuity-map-design.md §2, Q12).</summary>
public class EventChronologyInferenceTests : IdentityTestDb
{
    private IReadOnlyList<InferredEventRelation> Infer(int? focus = null)
    {
        using var context = NewContext();
        return EventChronologyInference.Infer(context, focus);
    }

    [Fact]
    public void DirectContinuation_WhenASharedSeriesPicksUpAtTheNextNumber()
    {
        var hulk = AddSeries("Hulk", ("104", 2007, 4), ("105", 2007, 5), ("106", 2007, 6), ("107", 2007, 7));
        int planet = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk.Take(2));
        int war = AddEvent("World War Hulk", StoryEventOrigin.Provider, hulk.Skip(2));

        var r = Assert.Single(Infer());

        Assert.Equal((war, planet, RelationType.Continuation), (r.SourceEventId, r.TargetEventId, r.Type));
        Assert.True(r.IsStrong);
        Assert.Equal("Continues straight on: Hulk #105 → #106", r.Reason);
    }

    [Fact]
    public void SharedSeriesOrder_WithAGap_IsASequel()
    {
        var hulk = AddSeries("Hulk", ("92", 2006, 4), ("95", 2006, 7), ("106", 2007, 6));
        int planet = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk.Take(2));
        int war = AddEvent("World War Hulk", StoryEventOrigin.Provider, hulk.Skip(2));

        var r = Assert.Single(Infer());

        Assert.Equal((war, planet, RelationType.Sequel), (r.SourceEventId, r.TargetEventId, r.Type));
        Assert.Equal(EventChronologyInference.SharedOrderConfidence, r.Confidence);
    }

    [Fact]
    public void InterleavedIssues_AreNotOrdered()
    {
        var hulk = AddSeries("Hulk", ("1", 2006, 1), ("2", 2006, 2), ("3", 2006, 3), ("4", 2006, 4));
        AddEvent("Alpha Saga", StoryEventOrigin.Provider, new[] { hulk[0], hulk[2] });
        AddEvent("Beta Arc", StoryEventOrigin.Provider, new[] { hulk[1], hulk[3] });

        Assert.Empty(Infer());
    }

    [Theory]
    [InlineData("Prelude to World War Hulk", RelationType.Prequel)]
    [InlineData("Road to World War Hulk", RelationType.Prequel)]
    [InlineData("World War Hulk: Prologue", RelationType.Prequel)]
    [InlineData("World War Hulk: Aftermath", RelationType.Sequel)]
    [InlineData("Aftermath of World War Hulk", RelationType.Sequel)]
    [InlineData("World War Hulk - Fallout", RelationType.Sequel)]
    public void NamePatterns_PointAtTheNamedEvent(string name, RelationType type)
    {
        int war = AddEvent("World War Hulk", StoryEventOrigin.Provider, AddSeries("World War Hulk", ("1", 2007, 6)));
        int other = AddEvent(name, StoryEventOrigin.Provider, AddSeries("Incredible Hulk", ("110", 2007, 9)));

        var r = Assert.Single(Infer(), x => x.IsStrong);

        Assert.Equal((other, war, type), (r.SourceEventId, r.TargetEventId, r.Type));
    }

    [Fact]
    public void NamePattern_NamingNothingInTheLibrary_InfersNothing()
    {
        AddEvent("Road to Nowhere", StoryEventOrigin.Provider, AddSeries("Hulk", ("1", 2006, 1)));
        AddEvent("Planet Hulk", StoryEventOrigin.Provider, AddSeries("Thor", ("1", 2006, 1)));

        Assert.DoesNotContain(Infer(), r => r.IsStrong);
    }

    [Fact]
    public void SameBaseNameYearsApart_IsAWeakSequel()
    {
        int old = AddEvent("Secret Wars (1984)", StoryEventOrigin.Provider, AddSeries("Secret Wars", ("1", 1984, 5)));
        int later = AddEvent("Secret Wars (2015)", StoryEventOrigin.Provider, AddSeries("Secret Wars (2015)", ("1", 2015, 7)));

        var r = Assert.Single(Infer());

        Assert.Equal((later, old, RelationType.Sequel), (r.SourceEventId, r.TargetEventId, r.Type));
        Assert.False(r.IsStrong);
        Assert.Equal("Same name, 1984 and 2015", r.Reason);
    }

    [Fact]
    public void SharedIssues_WithOverlappingDates_AreAWeakCrossover()
    {
        var shared = AddSeries("Avengers", ("1", 2010, 1), ("2", 2010, 2));
        AddEvent("Siege", StoryEventOrigin.Provider, shared);
        AddEvent("Heroic Age", StoryEventOrigin.Provider, shared.Skip(1));

        var r = Assert.Single(Infer());

        Assert.Equal(RelationType.Crossover, r.Type);
        Assert.False(r.IsStrong);
    }

    [Fact]
    public void DateOrderAndASharedWord_IsAWeakSequel_DateAloneIsNot()
    {
        int a = AddEvent("Infinity Gauntlet", StoryEventOrigin.Provider, AddSeries("Infinity Gauntlet", ("1", 1991, 7)));
        int b = AddEvent("Infinity War", StoryEventOrigin.Provider, AddSeries("Infinity War", ("1", 1992, 6)));
        AddEvent("Unrelated Thing", StoryEventOrigin.Provider, AddSeries("Other", ("1", 1993, 1)));

        var r = Assert.Single(Infer());

        Assert.Equal((b, a, RelationType.Sequel), (r.SourceEventId, r.TargetEventId, r.Type));
        Assert.Contains("both named “Infinity”", r.Reason);
    }

    [Fact]
    public void YourRelations_AndDismissedPairs_AreLeftAlone()
    {
        var hulk = AddSeries("Hulk", ("105", 2007, 5), ("106", 2007, 6));
        int planet = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk.Take(1));
        int war = AddEvent("World War Hulk", StoryEventOrigin.Provider, hulk.Skip(1));
        using (var context = NewContext())
        {
            EventRelationResolver.TryCreate(context, planet, war, RelationType.Related);
        }

        Assert.Empty(Infer());

        using (var context = NewContext())
        {
            context.EventRelations.RemoveRange(context.EventRelations);
            context.SaveChanges();
            EventConnectorSweep.Dismiss(context, war, planet);
        }

        Assert.Empty(Infer());
    }

    [Fact]
    public void Focus_OnlyReturnsRelationsTouchingThatEvent()
    {
        var hulk = AddSeries("Hulk", ("105", 2007, 5), ("106", 2007, 6));
        int planet = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk.Take(1));
        AddEvent("World War Hulk", StoryEventOrigin.Provider, hulk.Skip(1));
        var thor = AddSeries("Thor", ("10", 2008, 1), ("11", 2008, 2));
        int a = AddEvent("Thor Saga One", StoryEventOrigin.Provider, thor.Take(1));
        AddEvent("Thor Saga Two", StoryEventOrigin.Provider, thor.Skip(1));

        Assert.Equal(2, Infer().Count);
        Assert.All(Infer(planet), r => Assert.Contains(planet, new[] { r.SourceEventId, r.TargetEventId }));
        Assert.Single(Infer(a));
    }
}
