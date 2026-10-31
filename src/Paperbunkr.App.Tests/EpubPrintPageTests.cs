using cYo.Projects.ComicRack.Engine.IO.Provider.Books;

namespace Paperbunkr.App.Tests;

public class EpubPrintPageTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"paperbunkr_printpages_{Guid.NewGuid():N}.epub");

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("nav")]
    [InlineData("ncx")]
    public void ReadsThePrintPageList(string where)
    {
        EpubFixture.Create(_path, packedChapter2: true, printPageList: where);

        using var source = new EpubBookSource(_path);

        Assert.Equal([new BookPrintPage("1", 0, null), new BookPrintPage("2", 1, "pg2")], source.PrintPages);
    }

    [Fact]
    public void ABookWithoutAListHasNoPrintPages()
    {
        EpubFixture.Create(_path);

        using var source = new EpubBookSource(_path);

        Assert.Empty(source.PrintPages);
    }

    [Fact]
    public void APackedFileListsEachChapterAsASection()
    {
        EpubFixture.Create(_path, packedChapter2: true);

        using var source = new EpubBookSource(_path);

        Assert.Equal(["c2a", "c2b"], source.Chapters[1].Sections.Select(s => s.Fragment));
    }
}
