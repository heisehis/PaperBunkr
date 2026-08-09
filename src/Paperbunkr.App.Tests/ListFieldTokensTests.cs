using Paperbunkr.App.Models;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ListFieldTokens"/>, the shared tokenizer backing every bulk-edit list-field
/// diff/merge (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md §4).
/// </summary>
public class ListFieldTokensTests
{
    [Fact]
    public void Parse_NullOrWhitespace_ReturnsEmptySet()
    {
        Assert.Empty(ListFieldTokens.Parse(null));
        Assert.Empty(ListFieldTokens.Parse(string.Empty));
        Assert.Empty(ListFieldTokens.Parse("   "));
    }

    [Fact]
    public void Parse_TrimsWhitespaceAroundEachToken()
    {
        var tokens = ListFieldTokens.Parse("Jack Kirby ,  Stan Lee,Steve Ditko  ");

        Assert.Equal(3, tokens.Count);
        Assert.Contains("Jack Kirby", tokens);
        Assert.Contains("Stan Lee", tokens);
        Assert.Contains("Steve Ditko", tokens);
    }

    [Fact]
    public void Parse_IgnoresEmptyTokensFromDoubledCommas()
    {
        var tokens = ListFieldTokens.Parse("Writer A,,Writer B,");

        Assert.Equal(2, tokens.Count);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveForMembership()
    {
        var tokens = ListFieldTokens.Parse("Green Lantern, GREEN LANTERN");

        Assert.Single(tokens);
    }

    [Fact]
    public void Join_RoundTripsThroughParse()
    {
        var tokens = ListFieldTokens.Parse("Hal Jordan, John Stewart, Kilowok");

        string joined = ListFieldTokens.Join(tokens);
        var reparsed = ListFieldTokens.Parse(joined);

        Assert.Equal(tokens, reparsed);
    }
}
