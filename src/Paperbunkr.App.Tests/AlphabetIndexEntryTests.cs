using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #26 - the A-Z rail's letters and which ones are empty.</summary>
public class AlphabetIndexEntryTests
{
    [Theory]
    [InlineData("Absolute Batman", "A")]
    [InlineData("  batman", "B")]          // leading whitespace ignored, case folded
    [InlineData("2099", "#")]
    [InlineData("(Untitled)", "#")]
    [InlineData("", "#")]
    [InlineData(null, "#")]
    [InlineData("Émile", "#")]            // non-ASCII letters share the # bucket, same as the click handler's rule
    [InlineData("The Flash", "F")]        // CE's leading-article skip, matching the Series sort (2026-09-26 library audit)
    [InlineData("L'Incal", "I")]
    [InlineData("The", "T")]              // an article alone is a name, not a prefix
    [InlineData("Theory of Everything", "T")]
    public void LetterFor_UsesTheClickHandlersBucketRule(string? name, string expected)
        => Assert.Equal(expected, AlphabetIndexEntry.LetterFor(name));

    [Fact]
    public void Build_AlwaysReturnsAllTwentySevenInOrder_MarkingOnlyPresentLetters()
    {
        var entries = AlphabetIndexEntry.Build(new[] { "Absolute Batman", "Absolute Carnage", "Warhammer 40k", "2099" });

        Assert.Equal(27, entries.Count);
        Assert.Equal("A", entries[0].Letter);
        Assert.Equal("Z", entries[25].Letter);
        Assert.Equal("#", entries[26].Letter);

        var present = entries.Where(e => e.HasItems).Select(e => e.Letter).ToList();
        Assert.Equal(new[] { "A", "W", "#" }, present);
    }

    [Fact]
    public void Build_WithNoNames_HasNoPresentLetters()
        => Assert.DoesNotContain(AlphabetIndexEntry.Build(Array.Empty<string?>()), e => e.HasItems);

    [Fact]
    public void Build_WithGroupHeaders_TreatsEachSingleLetterHeaderAsPresent()
    {
        var entries = AlphabetIndexEntry.Build(new[] { "A", "C", "#" });

        Assert.Equal(new[] { "A", "C", "#" }, entries.Where(e => e.HasItems).Select(e => e.Letter));
    }
}
