using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

public class StorageResolverTests
{
    private const long Mb = 1024 * 1024;

    private static Issue Comic(int id, int series, string number, long? size, string path, string? publisher = "Image", int pages = 20, bool acknowledged = false)
        => new()
        {
            Id = id, SeriesId = series, Number = number, FileSize = size, FilePath = path, Publisher = publisher,
            PageCount = pages, DuplicateAcknowledged = acknowledged, Series = new Series { Id = series, Name = "Series " + series },
        };

    [Fact]
    public void TotalsGroupByFileTypeAndPublisher_AndCountUnmeasured()
    {
        var issues = new[]
        {
            Comic(1, 1, "1", 10 * Mb, @"C:\c\a.cbz"),
            Comic(2, 1, "2", 30 * Mb, @"C:\c\b.cbz"),
            Comic(3, 2, "1", 5 * Mb, @"C:\c\c.pdf", publisher: "Marvel"),
            Comic(4, 2, "2", null, @"C:\c\d.cbr"),            // never scanned: unmeasured
        };

        var data = StorageResolver.Compute(issues, bookCount: 7);

        Assert.Equal(45 * Mb, data.TotalBytes);
        Assert.Equal(3, data.MeasuredFiles);
        Assert.Equal(1, data.UnmeasuredFiles);
        Assert.Equal(7, data.BookCount);
        Assert.Equal("CBZ", data.ByFileType[0].Label);
        Assert.Equal(40 * Mb, data.ByFileType[0].Bytes);
        Assert.Equal("Image", data.ByPublisher[0].Label);
        Assert.Equal(40 * Mb, data.ByPublisher[0].Bytes);
        Assert.Equal(new[] { 2, 1, 3 }, data.Largest.Select(l => l.IssueId));
        Assert.Equal("Series 1 #2", data.Largest[0].Title);
    }

    [Fact]
    public void PublisherFallsBackToSeriesThenUnknown()
    {
        var a = Comic(1, 1, "1", Mb, @"C:\c\a.cbz", publisher: null);
        a.Series!.Publisher = "Boom";
        var b = Comic(2, 2, "1", 2 * Mb, @"C:\c\b.cbz", publisher: null);

        var data = StorageResolver.Compute(new[] { a, b }, 0);

        Assert.Contains(data.ByPublisher, s => s.Label == "Boom");
        Assert.Contains(data.ByPublisher, s => s.Label == "Unknown");
    }

    [Fact]
    public void ReclaimableIsEverythingButTheRecommendedCopy()
    {
        // Same series/number/format-less metadata => one duplicate group of two. The longer file (more pages) is kept.
        var keep = Comic(1, 1, "5", 40 * Mb, @"C:\c\a.cbz", pages: 40);
        var extra = Comic(2, 1, "5", 25 * Mb, @"C:\c\b.cbz", pages: 20);
        var unrelated = Comic(3, 1, "6", 9 * Mb, @"C:\c\c.cbz");

        var data = StorageResolver.Compute(new[] { keep, extra, unrelated }, 0);

        Assert.Equal(25 * Mb, data.ReclaimableBytes);
        Assert.Equal(1, data.DuplicateGroups);
    }

    [Fact]
    public void AcknowledgedDuplicateGroupsAreSkipped()
    {
        var a = Comic(1, 1, "5", 40 * Mb, @"C:\c\a.cbz", acknowledged: true);
        var b = Comic(2, 1, "5", 25 * Mb, @"C:\c\b.cbz", acknowledged: true);

        var data = StorageResolver.Compute(new[] { a, b }, 0);

        Assert.Equal(0, data.ReclaimableBytes);
        Assert.Equal(0, data.DuplicateGroups);
    }

    [Fact]
    public void EmptyLibraryHasNoData()
    {
        var data = StorageResolver.Compute(Array.Empty<Issue>(), 0);

        Assert.False(data.HasData);
        Assert.Empty(data.Largest);
    }
}
