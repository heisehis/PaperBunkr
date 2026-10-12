using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>The <see cref="SmartListField.HasProposedValue"/> smart-list field (docs/superpowers/specs/2026-10-11-proposed-values-design.md), evaluated over a hand-built snapshot.</summary>
public class ProposedValueFieldTests
{
    private static Issue Book(string label, params (MetadataProposalField Field, string Value, MetadataProposalStatus Status)[] proposals)
    {
        var issue = new Issue { Series = new Series { Name = label }, Title = null };
        foreach (var (field, value, status) in proposals)
        {
            issue.MetadataProposals.Add(new MetadataProposal { Field = field, ProposedValue = value, Status = status });
        }

        return issue;
    }

    private static List<string> Match(IReadOnlyList<Issue> library, string fields, SmartListOperator op = SmartListOperator.Is, bool not = false)
    {
        for (int i = 0; i < library.Count; i++)
        {
            library[i].Id = i + 1;
        }

        var list = new SmartList
        {
            Name = "t",
            RootGroup = new SmartListConditionGroup
            {
                Mode = SmartListGroupMode.And,
                Conditions = [new SmartListCondition { Field = SmartListField.HasProposedValue, Operator = op, Value = fields, Not = not }],
            },
        };
        var snapshot = new SmartListQueryBuilder.LibrarySnapshot { Issues = library };
        return SmartListQueryBuilder.Evaluate(snapshot, list).Select(i => i.Series!.Name).ToList();
    }

    private const MetadataProposalStatus Accepted = MetadataProposalStatus.Accepted;
    private const MetadataProposalStatus Pending = MetadataProposalStatus.Pending;

    [Fact]
    public void ItMatchesTheBooksWithAnAcceptedProposalForAnEmptyField()
    {
        var library = new List<Issue>
        {
            Book("has-number", (MetadataProposalField.Number, "5", Accepted)),
            Book("pending-only", (MetadataProposalField.Number, "5", Pending)),
            Book("nothing"),
        };

        Assert.Equal(["has-number"], Match(library, string.Empty));
    }

    [Fact]
    public void TheFieldListNarrowsTheSearch()
    {
        var library = new List<Issue>
        {
            Book("number", (MetadataProposalField.Number, "5", Accepted)),
            Book("year", (MetadataProposalField.Year, "1999", Accepted)),
        };

        Assert.Equal(["year"], Match(library, "year"));
        Assert.Equal(["number", "year"], Match(library, "number, year"));
        Assert.Equal(["number", "year"], Match(library, "bogus"));
    }

    [Fact]
    public void AStoredValueMeansThereIsNothingToCommit()
    {
        var withStored = Book("stored", (MetadataProposalField.Number, "5", Accepted));
        withStored.Number = "3";

        Assert.Empty(Match([withStored], "number"));
    }

    [Fact]
    public void IsNotAndTheNotFlag_BothFindTheBooksWithNothingProposed()
    {
        var library = new List<Issue>
        {
            Book("has", (MetadataProposalField.Number, "5", Accepted)),
            Book("none"),
        };

        Assert.Equal(["none"], Match(library, string.Empty, SmartListOperator.IsNot));
        Assert.Equal(["none"], Match(library, string.Empty, not: true));
    }

    [Fact]
    public void ASeriesProposal_DoesNotCount()
    {
        var library = new List<Issue> { Book("series", (MetadataProposalField.Series, "Batman", Accepted)) };

        Assert.Empty(Match(library, string.Empty));
    }
}
