using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary><see cref="ProposedValues"/> (docs/superpowers/specs/2026-10-11-proposed-values-design.md): which values count as proposed, and what committing them does.</summary>
public class ProposedValuesTests
{
    private static MetadataProposal Proposal(MetadataProposalField field, string value, MetadataProposalStatus status = MetadataProposalStatus.Accepted, int minutesAgo = 0) =>
        new() { Field = field, ProposedValue = value, Status = status, CreatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo) };

    private static Issue Book(params MetadataProposal[] proposals)
    {
        var issue = new Issue();
        issue.MetadataProposals.AddRange(proposals);
        return issue;
    }

    private static IReadOnlyCollection<MetadataProposalField> All => ProposedValues.CommittableFields;

    // --- Which values count ---

    [Fact]
    public void AnAcceptedProposalForAnEmptyField_IsProposed()
    {
        var issue = Book(Proposal(MetadataProposalField.Number, "7"));

        var pending = Assert.Single(ProposedValues.Pending(issue, All));
        Assert.Equal((MetadataProposalField.Number, "7"), pending);
        Assert.True(ProposedValues.Has(issue, All));
    }

    [Fact]
    public void ThePendingOrRejectedProposal_IsNotProposed()
    {
        var issue = Book(
            Proposal(MetadataProposalField.Number, "7", MetadataProposalStatus.Pending),
            Proposal(MetadataProposalField.Year, "2001", MetadataProposalStatus.Rejected));

        Assert.False(ProposedValues.Has(issue, All));
    }

    [Fact]
    public void AFieldThatAlreadyHasAStoredValue_IsNotProposed()
    {
        var issue = Book(Proposal(MetadataProposalField.Number, "7"));
        issue.Number = "3";

        Assert.False(ProposedValues.Has(issue, All));
    }

    [Fact]
    public void ABlankStoredValue_CountsAsEmpty()
    {
        var issue = Book(Proposal(MetadataProposalField.Title, "Found"));
        issue.Title = "  ";

        Assert.True(ProposedValues.Has(issue, All));
    }

    [Fact]
    public void ABlankProposedValue_IsNotAValue()
    {
        Assert.False(ProposedValues.Has(Book(Proposal(MetadataProposalField.Title, " ")), All));
    }

    [Fact]
    public void TheNewestAcceptedProposalWins()
    {
        var issue = Book(Proposal(MetadataProposalField.Number, "old", minutesAgo: 10), Proposal(MetadataProposalField.Number, "new", minutesAgo: 1));

        Assert.Equal("new", Assert.Single(ProposedValues.Pending(issue, All)).Value);
    }

    [Fact]
    public void SeriesAndOtherSeriesLevelProposals_AreNotCommittable()
    {
        var issue = Book(Proposal(MetadataProposalField.Series, "Batman"), Proposal(MetadataProposalField.Summary, "text"), Proposal(MetadataProposalField.Genre, "Horror"));

        Assert.False(ProposedValues.Has(issue, All));
    }

    // --- The field list ---

    [Fact]
    public void ParseFields_ReadsACommaOrSemicolonList_IgnoringCaseAndSpaces()
    {
        Assert.Equal([MetadataProposalField.Number, MetadataProposalField.Year], ProposedValues.ParseFields(" YEAR ; number "));
    }

    [Fact]
    public void ParseFields_KeepsTheScriptsOrder()
    {
        Assert.Equal([MetadataProposalField.Format, MetadataProposalField.Count], ProposedValues.ParseFields("count,format"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("series")]
    public void ParseFields_WithNoValidName_MeansEveryField(string? list)
    {
        Assert.Equal(ProposedValues.CommittableFields, ProposedValues.ParseFields(list));
    }

    [Fact]
    public void ParseFields_IgnoresTheUnknownNamesAmongValidOnes()
    {
        Assert.Equal([MetadataProposalField.Title], ProposedValues.ParseFields("series, title, bogus"));
    }

    [Fact]
    public void OnlyTheNamedFieldsAreLookedAt()
    {
        var issue = Book(Proposal(MetadataProposalField.Number, "7"));

        Assert.False(ProposedValues.Has(issue, [MetadataProposalField.Year]));
        Assert.True(ProposedValues.Has(issue, [MetadataProposalField.Year, MetadataProposalField.Number]));
    }

    // --- Commit ---

    [Fact]
    public void Commit_WritesEveryProposedValueIntoItsField()
    {
        var issue = Book(
            Proposal(MetadataProposalField.Title, "The Title"),
            Proposal(MetadataProposalField.Format, "Annual"),
            Proposal(MetadataProposalField.Volume, "2"),
            Proposal(MetadataProposalField.Number, "5"),
            Proposal(MetadataProposalField.Count, "12"),
            Proposal(MetadataProposalField.Year, "1999"));

        int set = ProposedValues.Commit(issue, All);

        Assert.Equal(6, set);
        Assert.Equal("The Title", issue.Title);
        Assert.Equal("Annual", issue.Format);
        Assert.Equal("2", issue.Volume);
        Assert.Equal("5", issue.Number);
        Assert.Equal(12, issue.Count);
        Assert.Equal(1999, issue.Year);
        Assert.False(ProposedValues.Has(issue, All));
    }

    [Fact]
    public void Commit_LeavesAStoredValueAlone_AndOnlyTouchesTheNamedFields()
    {
        var issue = Book(Proposal(MetadataProposalField.Number, "5"), Proposal(MetadataProposalField.Year, "1999"), Proposal(MetadataProposalField.Title, "T"));
        issue.Number = "mine";

        int set = ProposedValues.Commit(issue, [MetadataProposalField.Number, MetadataProposalField.Year]);

        Assert.Equal(1, set);
        Assert.Equal("mine", issue.Number);
        Assert.Equal(1999, issue.Year);
        Assert.Null(issue.Title);
    }

    [Fact]
    public void Commit_SkipsACountOrYearThatIsNotAWholeNumber()
    {
        var issue = Book(Proposal(MetadataProposalField.Count, "many"), Proposal(MetadataProposalField.Year, "19x9"));

        Assert.Equal(0, ProposedValues.Commit(issue, All));
        Assert.Null(issue.Count);
        Assert.Null(issue.Year);
    }

    [Fact]
    public void Commit_WithNothingProposed_DoesNothing()
    {
        Assert.Equal(0, ProposedValues.Commit(new Issue(), All));
    }
}
