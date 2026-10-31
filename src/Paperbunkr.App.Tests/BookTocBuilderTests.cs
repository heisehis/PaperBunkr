using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

public class BookTocBuilderTests
{
    // Two spine files: the first packs three chapters (anchors), the second is unlisted by the nav.
    private static readonly IReadOnlyList<BookChapter> Chapters =
    [
        new BookChapter
        {
            Title = "One",
            Html = "<h2 id=\"c1\">One</h2><p id=\"pb-p1\">aaaa</p><h2 id=\"c2\">Two</h2><p id=\"pb-p2\">bbbbbbbb</p><h2 id=\"c3\">Three</h2><p id=\"pb-p3\">cc</p>",
            Sections =
            [
                new BookSection { Title = "One", Fragment = "c1" },
                new BookSection { Title = "Two", Fragment = "c2" },
                new BookSection { Title = "Three", Fragment = "c3" },
            ],
        },
        new BookChapter { Title = "Chapter 2", Html = "<p>tail</p>" },
    ];

    [Fact]
    public void Build_ListsEveryNavEntryAndTheUnlistedFile()
    {
        var entries = BookTocBuilder.Build(Chapters);

        Assert.Equal(["One", "Two", "Three", "Chapter 2"], entries.Select(e => e.Title));
        Assert.Equal([0, 0, 0, 1], entries.Select(e => e.ChapterIndex));
        Assert.Equal("c2", entries[1].Fragment);
        Assert.Null(entries[3].Fragment);
        Assert.True(entries[1].Length > entries[2].Length); // "Two" covers more text than "Three"
    }

    [Fact]
    public void ActiveIndex_IsTheLastEntryAtOrBeforeTheCurrentBlock()
    {
        var entries = BookTocBuilder.Build(Chapters);
        string html = Chapters[0].Html!;

        Assert.Equal(0, BookTocBuilder.ActiveIndex(entries, 0, html, "pb-p1"));
        Assert.Equal(1, BookTocBuilder.ActiveIndex(entries, 0, html, "pb-p2"));
        Assert.Equal(2, BookTocBuilder.ActiveIndex(entries, 0, html, "pb-p3"));
        Assert.Equal(0, BookTocBuilder.ActiveIndex(entries, 0, html, null)); // no block yet: the file's first entry
        Assert.Equal(3, BookTocBuilder.ActiveIndex(entries, 1, "<p>tail</p>", null));
    }
}
