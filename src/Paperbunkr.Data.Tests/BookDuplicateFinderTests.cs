using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

public class BookDuplicateFinderTests
{
    private static int _id;

    private static Book B(string title, string? author = null, BookFormat format = BookFormat.Epub, string? isbn = null) =>
        new() { Id = ++_id, Title = title, Author = author, Format = format, ISBN = isbn, FilePath = $@"C:\b\{_id}.{format}" };

    [Fact]
    public void SameTitleAndAuthor_InTwoFormats_IsOneMixedFormatGroup()
    {
        var groups = BookDuplicateFinder.Find(new[] { B("Dune", "Frank Herbert"), B("Dune", "Frank Herbert", BookFormat.Pdf), B("Emma", "Jane Austen") });

        var group = Assert.Single(groups);
        Assert.Equal("Same title and author", group.Reason);
        Assert.True(group.MixedFormats);
        Assert.Equal(2, group.Entries.Count);
        Assert.Equal("Dune — Frank Herbert", group.Label);
    }

    [Fact]
    public void CaseArticlesAndPunctuation_AreIgnored()
    {
        var groups = BookDuplicateFinder.Find(new[] { B("The Hobbit!", "J.R.R. Tolkien"), B("hobbit", "JRR Tolkien", BookFormat.Mobi) });

        Assert.Single(groups);
    }

    [Fact]
    public void DifferentAuthors_OrBlankVsNamed_AreNotDuplicates()
    {
        Assert.Empty(BookDuplicateFinder.Find(new[] { B("Dune", "Frank Herbert"), B("Dune", "Brian Herbert") }));
        Assert.Empty(BookDuplicateFinder.Find(new[] { B("Dune", "Frank Herbert"), B("Dune", null) }));
    }

    [Fact]
    public void SameFormatTwice_IsReportedButNotMixed()
    {
        var group = Assert.Single(BookDuplicateFinder.Find(new[] { B("Dune", "F"), B("Dune", "F") }));

        Assert.False(group.MixedFormats);
    }

    [Fact]
    public void SameIsbn_WinsOverTitle_AndFormatsHyphensAreIgnored()
    {
        var a = B("Dune", "Frank Herbert", isbn: "978-0-441-17271-9");
        var b = B("Dune: Deluxe Edition", "F. Herbert", BookFormat.Pdf, isbn: "9780441172719");
        var c = B("Dune", "Frank Herbert", BookFormat.Fb2); // same title+author as `a`, but `a` is already claimed by the ISBN group

        var groups = BookDuplicateFinder.Find(new[] { a, b, c });

        var group = Assert.Single(groups);
        Assert.Equal("Same ISBN", group.Reason);
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(i => i), group.Entries.Select(e => e.BookId).OrderBy(i => i));
    }

    [Fact]
    public void BlankTitles_AndSingletons_AreQuiet()
    {
        Assert.Empty(BookDuplicateFinder.Find(new[] { B("???", "x"), B("!!!", "x") }));
        Assert.Empty(BookDuplicateFinder.Find(new[] { B("Only one") }));
        Assert.Empty(BookDuplicateFinder.Find(Array.Empty<Book>()));
    }

    [Fact]
    public void Groups_AreOrderedByLabel_AndEntriesByFormat()
    {
        var groups = BookDuplicateFinder.Find(new[]
        {
            B("Zebra", "z", BookFormat.Pdf), B("Zebra", "z", BookFormat.Epub), B("Apple", "a"), B("Apple", "a"),
        });

        Assert.Equal(new[] { "Apple — a", "Zebra — z" }, groups.Select(g => g.Label));
        Assert.Equal(new[] { BookFormat.Epub, BookFormat.Pdf }, groups[1].Entries.Select(e => e.Format));
    }
}
