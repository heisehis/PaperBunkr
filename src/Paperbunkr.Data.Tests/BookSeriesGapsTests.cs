using Paperbunkr.Data.Books;

namespace Paperbunkr.Data.Tests;

public class BookSeriesGapsTests
{
    private static IReadOnlyList<int> Gaps(params float?[] indices) => BookSeriesGaps.Find(indices);

    [Fact]
    public void HolesBetweenOwnedNumbers_AreReported()
    {
        Assert.Equal(new[] { 3 }, Gaps(1, 2, 4));
        Assert.Equal(new[] { 2, 4 }, Gaps(1, 3, 5));
        Assert.Equal(new[] { 2, 3, 4 }, Gaps(1, 5));
    }

    [Fact]
    public void NeverATail_OrAHeadBeforeTheFirstOwned()
    {
        Assert.Empty(Gaps(1, 2, 3));
        Assert.Empty(Gaps(4, 5, 6));   // own 4-6 of a longer series: #1-3 are not "missing holes"
        Assert.Equal(new[] { 5 }, Gaps(4, 6));
    }

    [Fact]
    public void AnUnnumberedBook_MeansWeCannotTell()
    {
        Assert.Empty(Gaps(1, 3, null));
        Assert.Empty(Gaps(null, null));
    }

    [Fact]
    public void FractionalNumbers_NeitherFillNorCreateGaps()
    {
        Assert.Equal(new[] { 3 }, Gaps(1, 2, 2.5f, 4));
        Assert.Equal(new[] { 2 }, Gaps(1, 1.5f, 3));
        Assert.Empty(Gaps(1, 1.5f, 2));
    }

    [Fact]
    public void SingletonsDuplicatesAndEmpty_AreQuiet()
    {
        Assert.Empty(Gaps());
        Assert.Empty(Gaps(3));
        Assert.Empty(Gaps(2, 2, 2));
        Assert.Equal(new[] { 2 }, Gaps(1, 1, 3, 3));
    }

    [Fact]
    public void AnAbsurdSpread_IsTreatedAsBadData()
    {
        Assert.Empty(Gaps(1, 9999));
        Assert.NotEmpty(Gaps(1, 100));
    }

    [Fact]
    public void ZeroIsAValidFirstNumber_NegativesAreIgnored()
    {
        Assert.Equal(new[] { 1 }, Gaps(0, 2));
        Assert.Equal(new[] { 2 }, Gaps(-1, 1, 3));
    }
}
