using System.Xml.Linq;
using Paperbunkr.Data.SmartLists.CeImport;

namespace Paperbunkr.Data.Tests.CeSmartLists;

public sealed class CeComicDbSmartListReaderTests
{
    [Fact]
    public void ReadsNestedSmartListsWithFolderPath_AndIgnoresSavedLists()
    {
        XDocument xml = XDocument.Parse("""
            <ComicDatabase xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <ComicLists>
                <Item xsi:type="ComicLibraryListItem" Name="Library" />
                <Item xsi:type="ComicListItemFolder" Name="Marvel"><Items>
                  <Item xsi:type="ComicListItemFolder" Name="New"><Items>
                    <Item xsi:type="ComicSmartListItem" Name="Releases" MatcherMode="And"><Matchers>
                      <ComicBookMatcher xsi:type="ComicBookPublisherMatcher" MatchOperator="0"><MatchValue>Marvel</MatchValue></ComicBookMatcher>
                    </Matchers></Item>
                    <Item xsi:type="ComicIdListItem" Name="Static" />
                  </Items></Item>
                </Items></Item>
              </ComicLists>
            </ComicDatabase>
            """);

        var draft = Assert.Single(CeComicDbSmartListReader.Read(xml));

        Assert.Equal("Marvel > New > Releases", draft.Name);
        Assert.False(draft.IsEmpty);
        Assert.Equal("Marvel > New > Releases", draft.List.Name);
    }
}
