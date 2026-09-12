using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="TypeAheadSearch.TryMatch{T}"/> - the pure buffer/timeout/match core ported
/// from CE's own <c>KeySearch.Select</c> (docs/superpowers/specs/2026-09-12-grid-typeahead-
/// rangeselect-quit-design.md). No Avalonia control types needed, matching
/// <see cref="GridKeyboardNavigationTests"/>'s own precedent for testing the pure core directly.
/// </summary>
public class TypeAheadSearchTests
{
    private sealed class Item
    {
        public required string Name { get; init; }
    }

    private static Item[] Items(params string[] names) =>
        System.Array.ConvertAll(names, n => new Item { Name = n });

    private static string NameOf(Item item) => item.Name;

    [Fact]
    public void SingleChar_MatchesFirstStartingWithIt()
    {
        var buffer = new TypeAheadSearch.Buffer();
        var items = Items("Batman", "Superman", "Wonder Woman");

        var match = TypeAheadSearch.TryMatch(buffer, 'b', items, NameOf, nowTicks: 0);

        Assert.Same(items[0], match);
    }

    [Fact]
    public void MultipleChars_BufferAccumulates_NarrowsMatch()
    {
        var buffer = new TypeAheadSearch.Buffer();
        var items = Items("Batman", "Batwoman");

        TypeAheadSearch.TryMatch(buffer, 'b', items, NameOf, nowTicks: 0);
        TypeAheadSearch.TryMatch(buffer, 'a', items, NameOf, nowTicks: 10);
        var match = TypeAheadSearch.TryMatch(buffer, 't', items, NameOf, nowTicks: 20);

        Assert.Same(items[0], match);
    }

    [Fact]
    public void NonMatchingChar_LeavesBufferUnchanged_NotDiscardingPriorMatch()
    {
        var buffer = new TypeAheadSearch.Buffer();
        var items = Items("Batman");

        TypeAheadSearch.TryMatch(buffer, 'b', items, NameOf, nowTicks: 0);
        var noMatch = TypeAheadSearch.TryMatch(buffer, 'z', items, NameOf, nowTicks: 10);
        Assert.Null(noMatch);

        // Buffer still "b" (the "z" was never committed) - typing "a" next still matches Batman.
        var match = TypeAheadSearch.TryMatch(buffer, 'a', items, NameOf, nowTicks: 20);
        Assert.Same(items[0], match);
    }

    [Fact]
    public void IdleTimeout_ResetsBuffer()
    {
        var buffer = new TypeAheadSearch.Buffer();
        var items = Items("Batman", "Superman");

        TypeAheadSearch.TryMatch(buffer, 'b', items, NameOf, nowTicks: 0);
        // Well past the 2.5s idle window - "s" should match fresh, not "bs" (which matches nothing).
        var match = TypeAheadSearch.TryMatch(buffer, 's', items, NameOf, nowTicks: 3000);

        Assert.Same(items[1], match);
    }

    [Fact]
    public void Backspace_ShrinksBuffer()
    {
        var buffer = new TypeAheadSearch.Buffer();
        var items = Items("Batman", "Superman");

        TypeAheadSearch.TryMatch(buffer, 'b', items, NameOf, nowTicks: 0);
        TypeAheadSearch.TryMatch(buffer, 'a', items, NameOf, nowTicks: 10);
        TypeAheadSearch.TryMatch(buffer, '\b', items, NameOf, nowTicks: 20); // buffer back to "b"
        var match = TypeAheadSearch.TryMatch(buffer, 'a', items, NameOf, nowTicks: 30);

        Assert.Same(items[0], match);
    }

    [Fact]
    public void ArticlesAreIgnored_MatchesPastLeadingThe()
    {
        var buffer = new TypeAheadSearch.Buffer();
        var items = Items("The Amazing Spider-Man", "Superman");

        TypeAheadSearch.TryMatch(buffer, 'a', items, NameOf, nowTicks: 0);
        var match = TypeAheadSearch.TryMatch(buffer, 'm', items, NameOf, nowTicks: 10);

        Assert.Same(items[0], match);
    }

    [Fact]
    public void NoMatchAtAll_ReturnsNull()
    {
        var buffer = new TypeAheadSearch.Buffer();
        var items = Items("Batman");

        var match = TypeAheadSearch.TryMatch(buffer, 'z', items, NameOf, nowTicks: 0);

        Assert.Null(match);
    }
}
