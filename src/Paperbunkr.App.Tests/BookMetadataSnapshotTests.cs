using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="BookMetadataSnapshot"/> is the undo/redo snapshot for a book row
/// (docs/superpowers/specs/2026-08-27-book-properties-editor-design.md) - Capture then Apply on a
/// fresh entity must reproduce every field, nulls included.
/// </summary>
public class BookMetadataSnapshotTests
{
    [Fact]
    public void CaptureThenApply_RoundTripsAllFields()
    {
        var original = new Book
        {
            Title = "Dune",
            Author = "Frank Herbert",
            Summary = "Spice.",
            PublishedDate = new DateTime(1965, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            BookSeriesId = 7,
        };

        var snapshot = BookMetadataSnapshot.Capture(original);

        var target = new Book { Title = "wrong", Author = "wrong", Summary = "wrong", PublishedDate = null, BookSeriesId = 99 };
        BookMetadataSnapshot.Apply(target, snapshot);

        Assert.Equal("Dune", target.Title);
        Assert.Equal("Frank Herbert", target.Author);
        Assert.Equal("Spice.", target.Summary);
        Assert.Equal(new DateTime(1965, 8, 1, 0, 0, 0, DateTimeKind.Utc), target.PublishedDate);
        Assert.Equal(7, target.BookSeriesId);
    }

    [Fact]
    public void CaptureThenApply_RoundTripsNulls()
    {
        var original = new Book { Title = "Standalone", Author = null, Summary = null, PublishedDate = null, BookSeriesId = null };

        var snapshot = BookMetadataSnapshot.Capture(original);

        var target = new Book { Title = "x", Author = "x", Summary = "x", PublishedDate = DateTime.UtcNow, BookSeriesId = 3 };
        BookMetadataSnapshot.Apply(target, snapshot);

        Assert.Equal("Standalone", target.Title);
        Assert.Null(target.Author);
        Assert.Null(target.Summary);
        Assert.Null(target.PublishedDate);
        Assert.Null(target.BookSeriesId);
    }
}
