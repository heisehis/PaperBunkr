using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary><see cref="SameXDifferentY"/> (docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §1), checked against what ComicRack's script returns.</summary>
public class SameXDifferentYTests
{
    private static int _nextId;

    private static Issue Book(string series, string number, string? format = null, string? volume = null, params (string Name, string Value)[] custom)
    {
        var issue = new Issue { Id = ++_nextId, Series = new Series { Name = series }, Number = number, Format = format, Volume = volume };
        foreach ((string name, string value) in custom)
        {
            issue.CustomValues.Add(new IssueCustomValue { Name = name, Value = value });
        }

        return issue;
    }

    private static List<int> Ids(params Issue[] books) => books.Select(b => b.Id).Order().ToList();

    private static List<int> Run(IReadOnlyList<Issue> library, string x, string y) => SameXDifferentY.Compute(library, x, y).Order().ToList();

    [Fact]
    public void WithNoY_EveryBookOfAGroupOfTwoOrMore_IsReturned()
    {
        var a1 = Book("A", "1", "Annual");
        var a2 = Book("A", "1", "Annual");
        var a3 = Book("A", "2", "Annual");

        Assert.Equal(Ids(a1, a2), Run([a1, a2, a3], "Series|Number|Format", string.Empty));
    }

    [Fact]
    public void AGroupOfOne_IsNeverReturned()
    {
        var only = Book("A", "1");

        Assert.Empty(Run([only], "Series", string.Empty));
        Assert.Empty(Run([only], "Series", "Format"));
    }

    [Fact]
    public void WithY_OnlyTheGroupsWhereYDiffersAreReturned_AllOfTheirBooks()
    {
        var same1 = Book("Same", "1", "TPB", custom: ("comicvine_volume", "10"));
        var same2 = Book("Same", "2", "TPB", custom: ("comicvine_volume", "10"));
        var mixed1 = Book("Mixed", "1", "TPB", custom: ("comicvine_volume", "20"));
        var mixed2 = Book("Mixed", "2", "Annual", custom: ("comicvine_volume", "20"));
        var mixed3 = Book("Mixed", "3", "TPB", custom: ("comicvine_volume", "20"));

        // The shape of "6.4 Cleanup: Series with mismatch Formats": same comicvine_volume, different Format.
        Assert.Equal(Ids(mixed1, mixed2, mixed3), Run([same1, same2, mixed1, mixed2, mixed3], "comicvine_volume", "Format"));
    }

    [Fact]
    public void XAttributesCombine_SoBooksMustAgreeOnAllOfThem()
    {
        var a = Book("A", "1", "TPB");
        var b = Book("A", "1", "Annual");
        var c = Book("A", "1", "TPB");

        Assert.Equal(Ids(a, c), Run([a, b, c], "Series|Number|Format", string.Empty));
    }

    [Fact]
    public void AnyOneYAttributeDiffering_IsEnough()
    {
        var a = Book("A", "1", "TPB", "1");
        var b = Book("A", "2", "TPB", "2");

        Assert.Equal(Ids(a, b), Run([a, b], "Series", "Format|Volume"));
        Assert.Empty(Run([a, b], "Series", "Format"));
    }

    [Fact]
    public void FieldNamesMatchByEnumNameOrLabel_IgnoringCase()
    {
        var a = Book("A", "1");
        var b = Book("A", "1");

        Assert.Equal(Ids(a, b), Run([a, b], "series|number", string.Empty));
        Assert.Equal(Ids(a, b), Run([a, b], "SeriesName|Number", string.Empty));
    }

    [Fact]
    public void ACustomValueIsLookedUpWhenTheNameIsNotAField()
    {
        var a = Book("A", "1", custom: ("comicvine_issue", "4000"));
        var b = Book("B", "9", custom: ("comicvine_issue", "4000"));
        var c = Book("C", "3", custom: ("comicvine_issue", "5000"));

        Assert.Equal(Ids(a, b), Run([a, b, c], "comicvine_issue", string.Empty));
    }

    [Fact]
    public void ABookWithNoneOfTheXAttributes_JoinsNoGroup()
    {
        var a = Book("A", "1");
        var b = Book("B", "1");

        Assert.Empty(Run([a, b], "comicvine_issue", string.Empty));
    }

    [Fact]
    public void AMissingCustomValueOnTheYSide_ReadsAsNone_SoItDiffersFromAPresentOne()
    {
        var has = Book("A", "1", custom: ("datamanager_processed", "yes"));
        var lacks = Book("A", "2");

        Assert.Equal(Ids(has, lacks), Run([has, lacks], "Series", "datamanager_processed"));
    }

    [Fact]
    public void TwoBooksThatBothLackTheYValue_DoNotDiffer()
    {
        var a = Book("A", "1");
        var b = Book("A", "2");

        Assert.Empty(Run([a, b], "Series", "datamanager_processed"));
    }

    [Fact]
    public void AnUnknownXName_IsIgnored_NotAnError()
    {
        var a = Book("A", "1");
        var b = Book("A", "2");

        Assert.Equal(Ids(a, b), Run([a, b], "Series|no_such_thing", string.Empty));
    }

    [Fact]
    public void AnEmptyX_ReturnsNothing()
    {
        var a = Book("A", "1");
        var b = Book("A", "1");

        Assert.Empty(Run([a, b], string.Empty, "Format"));
        Assert.Empty(Run([a, b], " | ", string.Empty));
    }

    [Fact]
    public void NumbersAndVolumesGroupByTheirValue()
    {
        var a = Book("A", "1", volume: "2000");
        var b = Book("A", "5", volume: "2000");
        var c = Book("A", "6", volume: "2001");

        Assert.Equal(Ids(a, b), Run([a, b, c], "Series|Volume", string.Empty));
    }
}
