using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Tests;

public class BookUnitSlicerTests
{
    private const string FileA =
        "<div class=\"wrap\"><p id=\"pb-p1\">Contents</p>" +
        "<h2 id=\"c1\">One</h2><p id=\"pb-p2\">aaa</p>" +
        "<h2 id=\"c2\">Two</h2><p id=\"pb-p3\">bbb</p></div>";

    private const string FileB = "<p id=\"pb-p1\">tail</p>";

    private static readonly BookTocEntry[] Rows =
    [
        new(0, "One", "c1", null, 1),
        new(0, "Two", "c2", null, 1),
        new(0, "Ghost", "missing", null, 1),
        new(1, "Chapter 2", null, null, 1),
    ];

    [Fact]
    public void Build_CutsAFileAtItsAnchorsAndKeepsThePreambleWithTheFirstUnit()
    {
        var set = BookUnitSlicer.Build(Rows, [FileA, FileB]);

        Assert.Equal(3, set.Units.Count);
        Assert.StartsWith("<div class=\"wrap\"><p id=\"pb-p1\">Contents", set.Units[0].Slice(FileA));
        Assert.Contains("id=\"pb-p2\"", set.Units[0].Slice(FileA));
        Assert.StartsWith("<h2 id=\"c2\">Two", set.Units[1].Slice(FileA));
        Assert.Equal(FileB, set.Units[2].Slice(FileB));
    }

    [Fact]
    public void Build_AMissingAnchorDoesNotCutAndItsRowOpensTheUnitItFallsIn()
    {
        var set = BookUnitSlicer.Build(Rows, [FileA, FileB]);

        Assert.Equal([0, 1, 1, 2], set.RowToUnit);
        Assert.Equal(1, set.RowOf(1));
        Assert.Equal("Two", set.Units[1].Title);
    }

    [Fact]
    public void UnitOf_FindsTheUnitHoldingABlockAndFallsBackToTheFilesFirstUnit()
    {
        var set = BookUnitSlicer.Build(Rows, [FileA, FileB]);

        Assert.Equal(0, set.UnitOf(0, FileA, "pb-p2"));
        Assert.Equal(1, set.UnitOf(0, FileA, "pb-p3"));
        Assert.Equal(1, set.UnitOf(0, FileA, "c2")); // a contents anchor works as a block id too
        Assert.Equal(0, set.UnitOf(0, FileA, null));
        Assert.Equal(2, set.UnitOf(1, FileB, "pb-p1"));
    }

    [Fact]
    public void Build_AFileWithoutRowsIsOneUnit()
    {
        var set = BookUnitSlicer.Build([new BookTocEntry(0, "Only", null, null, 1)], [FileA, FileB]);

        Assert.Equal(2, set.Units.Count);
        Assert.Equal("Chapter 2", set.Units[1].Title);
        Assert.Equal(FileB, set.Units[1].Slice(FileB));
    }
}
