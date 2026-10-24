using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

public class DropOffBreakdownResolverTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private static DropOffSeriesInput S(
        int id, int read, int unread, int daysAgo = 1, ReadingStatus status = ReadingStatus.Unknown,
        string? publisher = "Marvel", string? genre = "Superhero", ContentType type = ContentType.Comic)
        => new(id, status, type, publisher, genre, read, unread, Now.AddDays(-daysAgo));

    [Fact]
    public void ClassifiesStoppedFinishedAndNeither()
    {
        Assert.True(DropOffBreakdownResolver.Classify(S(1, 3, 5, daysAgo: 22), Now));            // stalled past 21d
        Assert.Null(DropOffBreakdownResolver.Classify(S(2, 3, 5, daysAgo: 21), Now));            // exactly 21d is still recent
        Assert.Null(DropOffBreakdownResolver.Classify(S(3, 0, 5, daysAgo: 90), Now));            // never started
        Assert.False(DropOffBreakdownResolver.Classify(S(4, 5, 0, daysAgo: 90), Now));           // all read
        Assert.True(DropOffBreakdownResolver.Classify(S(5, 0, 5, status: ReadingStatus.Dropped), Now)); // dropped even unread
        Assert.False(DropOffBreakdownResolver.Classify(S(6, 2, 4, daysAgo: 200, status: ReadingStatus.Completed), Now));
    }

    [Fact]
    public void SmallBucketsFoldIntoOther()
    {
        var series = new List<DropOffSeriesInput>();
        for (int i = 0; i < 6; i++)
        {
            series.Add(S(i, 2, 5, daysAgo: 40, publisher: "Marvel"));        // 6 stopped Marvel
        }

        for (int i = 10; i < 14; i++)
        {
            series.Add(S(i, 4, 0, publisher: "Boom"));                       // 4 finished Boom (below the 5 floor)
        }

        series.Add(S(20, 4, 0, publisher: "Image"));                         // 1 finished Image

        var data = DropOffBreakdownResolver.Compute(series, Now);

        var marvel = data.ByPublisher.Single(r => r.Label == "Marvel");
        Assert.Equal(6, marvel.Stopped);
        Assert.Equal(1.0, marvel.StoppedRate);
        var other = data.ByPublisher.Single(r => r.Label == "Other");
        Assert.Equal(0, other.Stopped);
        Assert.Equal(5, other.Finished);
        Assert.DoesNotContain(data.ByPublisher, r => r.Label == "Boom");
        Assert.Equal(6, data.StoppedTotal);
        Assert.Equal(5, data.FinishedTotal);
    }

    [Fact]
    public void StallPointsBucketReadCountOfStoppedSeriesOnly()
    {
        var series = new[]
        {
            S(1, 1, 4, daysAgo: 60), S(2, 2, 4, daysAgo: 60), S(3, 4, 4, daysAgo: 60), S(4, 5, 4, daysAgo: 60),
            S(5, 7, 4, daysAgo: 60), S(6, 12, 4, daysAgo: 60),
            S(7, 3, 0),                                         // finished: not a stall point
        };

        var data = DropOffBreakdownResolver.Compute(series, Now);

        int Count(string label) => data.StallPoints.Single(b => b.Label == label).Count;
        Assert.Equal(1, Count("1"));
        Assert.Equal(1, Count("2"));
        Assert.Equal(0, Count("3"));
        Assert.Equal(2, Count("4-5"));
        Assert.Equal(1, Count("6-10"));
        Assert.Equal(1, Count("11+"));
    }

    [Fact]
    public void UnknownContentTypeAndMissingPublisherGetUnknownLabels()
    {
        var series = Enumerable.Range(0, 5).Select(i => S(i, 3, 0, publisher: null, type: ContentType.Unknown)).ToList();

        var data = DropOffBreakdownResolver.Compute(series, Now);

        Assert.Equal(5, data.ByPublisher.Single(r => r.Label == "Unknown").Finished);
        Assert.Equal(5, data.ByContentType.Single(r => r.Label == "Unknown").Finished);
    }

    [Fact]
    public void EmptyInputHasNoData()
    {
        var data = DropOffBreakdownResolver.Compute(Array.Empty<DropOffSeriesInput>(), Now);

        Assert.False(data.HasData);
        Assert.Empty(data.ByPublisher);
    }
}
