using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests.ReadingLists;

public sealed class ReadingListSourceMarkTests
{
    [Theory]
    [InlineData("ComicVine", "ComicVine")]
    [InlineData("Metron", "Metron")]
    [InlineData("MetronLists", "Metron")]
    public void ExistingSourceMark_ReturnsItsBundledServiceKey(string source, string expected) =>
        Assert.Equal(expected, ReadingListSourceRegistry.GetMarkKey(source));

    [Theory]
    [InlineData("ComicBookReadingOrders")]
    [InlineData("ComicArc")]
    [InlineData("ReadingOrdersNet")]
    [InlineData("ReadThingsRight")]
    [InlineData("unknown")]
    [InlineData(null)]
    public void MissingOrUnknownSourceMark_RemainsTextOnly(string? source) =>
        Assert.Null(ReadingListSourceRegistry.GetMarkKey(source));
}
