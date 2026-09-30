using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>Covers <see cref="StoryEventIdentityResolver"/> (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §2, Q2/Q9/Q17).</summary>
public class StoryEventIdentityResolverTests : IdentityTestDb
{
    private DuplicatePair SinglePair()
    {
        using var context = NewContext();
        return Assert.Single(StoryEventIdentityResolver.FindPairs(context));
    }

    [Fact]
    public void SharedProviderId_BetweenProviderEvents_IsSilent()
    {
        var hulk = AddSeries("Hulk", "1", "2");
        var other = AddSeries("Other", "1");
        AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk, comicVineId: "4512");
        AddEvent("Something Else Entirely", StoryEventOrigin.Provider, other, comicVineId: "4512");

        var pair = SinglePair();

        Assert.Equal(DuplicateEvidence.SharedProviderId, pair.Evidence);
        Assert.True(pair.IsSilent);
        Assert.Equal("Same ComicVine arc", pair.Reason);
    }

    [Fact]
    public void NameAndOverlap_IsSilent_AndPredictsThePrefixFreeName()
    {
        var hulk = AddSeries("Hulk", "92", "93", "94");
        AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, hulk);
        AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk.Take(2));

        var pair = SinglePair();

        Assert.Equal(DuplicateEvidence.NameAndOverlap, pair.Evidence);
        Assert.True(pair.IsSilent);
        Assert.Equal("Names match, 2 of 2 issues shared", pair.Reason);
        Assert.Equal("Planet Hulk", pair.KeptName);
    }

    [Fact]
    public void AUserEvent_AlwaysGoesToReview()
    {
        var hulk = AddSeries("Hulk", "1", "2");
        AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk, comicVineId: "4512");
        AddEvent("Planet Hulk", StoryEventOrigin.User, hulk, comicVineId: "4512");

        Assert.False(SinglePair().IsSilent);
    }

    [Fact]
    public void NameOnly_AndOverlapOnly_GoToReview()
    {
        var hulk = AddSeries("Hulk", "1", "2", "3", "4");
        var ww = AddSeries("World War Hulk", "1");
        AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk.Take(1));
        AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, ww);            // same name keys? no - WWH isn't "Hulk"
        AddEvent("Planet Hulk Prelude", StoryEventOrigin.Provider, hulk.Take(2));
        AddEvent("Planet Hulk (2006)", StoryEventOrigin.Provider, hulk.Skip(2));

        using var context = NewContext();
        var pairs = StoryEventIdentityResolver.FindPairs(context);

        Assert.Contains(pairs, p => p.Evidence == DuplicateEvidence.NameOnly && !p.IsSilent);        // "Planet Hulk" vs "Planet Hulk (2006)"
        Assert.Contains(pairs, p => p.Evidence == DuplicateEvidence.OverlapOnly && !p.IsSilent);     // "Planet Hulk" inside the prelude
        Assert.DoesNotContain(pairs, p => p.IsSilent);
    }

    [Fact]
    public void ContradictingIds_AreFlagged_AndNeverSilent()
    {
        var hulk = AddSeries("Hulk", "1", "2");
        AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk, comicVineId: "4512", metronId: "77");
        AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, hulk, comicVineId: "6620", metronId: "77");

        var pair = SinglePair();

        Assert.False(pair.IsSilent);
        Assert.Equal("Sources disagree: ComicVine arc 4512 vs 6620", pair.Reason);
    }

    [Fact]
    public void ConflictNote_BlocksSilent_AndListsTheEventForReview()
    {
        var hulk = AddSeries("Hulk", "1", "2");
        int a = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk, comicVineId: "4512");
        AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, hulk, comicVineId: "4512");
        using (var context = NewContext())
        {
            context.StoryEvents.Find(a)!.IdentityConflict = "Metron arc 77 vs 78";
            context.SaveChanges();
        }

        Assert.False(SinglePair().IsSilent);
        using var check = NewContext();
        var items = StoryEventIdentityResolver.FindReviewItems(check);
        Assert.Contains(items, i => i.IsConflictOnly && i.EventAId == a && i.Reason == "Sources disagree: Metron arc 77 vs 78");
        Assert.Contains(items, i => !i.IsConflictOnly);
    }

    [Fact]
    public void DismissedPairs_AreSkipped_BothWays()
    {
        var hulk = AddSeries("Hulk", "1");
        int a = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk);
        int b = AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, hulk);
        using (var context = NewContext())
        {
            StoryEventIdentityResolver.Dismiss(context, b, a);
            StoryEventIdentityResolver.Dismiss(context, a, b);      // idempotent
        }

        using var check = NewContext();
        Assert.Empty(StoryEventIdentityResolver.FindPairs(check));
        Assert.Single(check.StoryEventDuplicateDismissals);
    }

    [Fact]
    public void DifferentExplicitYears_AreNotANameMatch()
    {
        AddEvent("Secret Wars (1984)", StoryEventOrigin.Provider, AddSeries("Secret Wars", "1"));
        AddEvent("Secret Wars (2015)", StoryEventOrigin.Provider, AddSeries("Secret Wars (2015)", "1"));

        using var context = NewContext();
        Assert.Empty(StoryEventIdentityResolver.FindPairs(context));
    }

    [Fact]
    public void UnrelatedEvents_AreNeverPaired()
    {
        AddEvent("Planet Hulk", StoryEventOrigin.Provider, AddSeries("Hulk", "1"));
        AddEvent("Civil War", StoryEventOrigin.Provider, AddSeries("Civil War", "1"));

        using var context = NewContext();
        Assert.Empty(StoryEventIdentityResolver.FindPairs(context));
    }
}
