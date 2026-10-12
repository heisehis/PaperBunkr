using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary><see cref="FormatGuesser"/> (docs/superpowers/specs/2026-10-11-format-presets-design.md).</summary>
public class FormatGuesserTests
{
    [Theory]
    [InlineData("Batman Annual", "Annual")]
    [InlineData("batman annual 2021", "Annual")]
    [InlineData("Watchmen Director's Cut", "Director's Cut")]
    [InlineData("Watchmen Directors Cut", "Director's Cut")]
    [InlineData("Saga TPB", "Trade Paper Back")]
    [InlineData("Saga Trade Paperback Vol 1", "Trade Paper Back")]
    [InlineData("Saga trade-paper-back", "Trade Paper Back")]
    [InlineData("Joker One-Shot", "One Shot")]
    [InlineData("Joker One Shot", "One Shot")]
    [InlineData("Savage Sword (Black & White)", "Black & White")]
    [InlineData("Savage Sword black and white", "Black & White")]
    [InlineData("Savage Sword B&W", "Black & White")]
    public void ANameThatSaysTheFormat_GivesIt(string name, string expected)
    {
        Assert.Equal(expected, FormatGuesser.Guess(name));
    }

    [Theory]
    [InlineData("Batman")]
    [InlineData("Annualized Returns")]
    [InlineData("The Annuals")]
    [InlineData("Oneshotting")]
    [InlineData("")]
    [InlineData(null)]
    public void ANameThatOnlyLooksLikeIt_GivesNothing(string? name)
    {
        Assert.Null(FormatGuesser.Guess(name));
    }

    [Fact]
    public void TheMoreSpecificRuleWins_WhenTwoMatch()
    {
        Assert.Equal("Director's Cut", FormatGuesser.Guess("Batman Annual Director's Cut"));
        Assert.Equal("Trade Paper Back", FormatGuesser.Guess("Batman Annual TPB"));
    }

    [Fact]
    public void AnyOfTheNamesCanCarryIt()
    {
        Assert.Equal("Annual", FormatGuesser.Guess("Batman", null, "Batman Annual 001 (2011)"));
    }

    private static Issue Book(string series, string? title = null, string? file = null, string? format = null) =>
        new() { Series = new Series { Name = series }, Title = title, FilePath = file, Format = format };

    [Fact]
    public void ForABook_TheSeriesTitleAndFileNameAreAllRead()
    {
        Assert.Equal("Annual", FormatGuesser.Guess(Book("Batman Annual")));
        Assert.Equal("Annual", FormatGuesser.Guess(Book("Batman", title: "Annual: Night of the Owls")));
        Assert.Equal("Annual", FormatGuesser.Guess(Book("Batman", file: @"C:\lib\Batman Annual 001.cbz")));
        Assert.Null(FormatGuesser.Guess(Book("Batman", file: @"C:\lib\Annual\Batman 001.cbz")));
    }

    [Fact]
    public void ABookThatAlreadyHasAFormat_IsLeftAlone_WhetherStoredOrProposed()
    {
        Assert.Null(FormatGuesser.Guess(Book("Batman Annual", format: "Special")));

        var proposed = Book("Batman Annual");
        proposed.MetadataProposals.Add(new MetadataProposal { Field = MetadataProposalField.Format, ProposedValue = "Giant", Status = MetadataProposalStatus.Accepted });
        Assert.Null(FormatGuesser.Guess(proposed));
    }

    [Fact]
    public void EveryRulesFormat_IsOneOfComicRacksOwnFormatNames()
    {
        Assert.All(FormatGuesser.Rules, r => Assert.Contains(r.Format, FormatSignalCatalog.CeDefaultFormats));
    }
}
